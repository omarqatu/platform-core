using Core.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Core.WhiteBoxTests;

// [W] The e-mail sender (PLATFORM_CORE Amendment 01 §2.6, §5.5). What is proven here needs no provider and no
// database: that a delivery which cannot happen is reported, never thrown — a channel failing must not stop the
// others (§5.3) and must not turn forgot's uniform 204 into a 500 (§5.1) — and that the secret is read from the
// settings key provider_accounts names, never from the database.
public class T3_NotificationSenderTests
{
    private sealed class Account(PlatformProvider? provider) : IPlatformProviderAccounts
    {
        public Task<PlatformProvider?> ForAsync(NotificationChannel channel, CancellationToken cancellationToken) =>
            Task.FromResult(provider);
    }

    private static EmailNotificationSender Sender(PlatformProvider? provider, params (string Key, string Value)[] settings) =>
        new(new Account(provider),
            new ConfigurationBuilder().AddInMemoryCollection(
                settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value))).Build(),
            Options.Create(new EmailSenderOptions()),
            NullLogger<EmailNotificationSender>.Instance);

    private static readonly NotificationMessage Message =
        new(NotificationChannel.Email, "someone@example.test", "Subject", "Body");

    // No platform row for the channel: reported, not thrown.
    [Fact]
    public async Task WithNoPlatformProviderAccount_TheDeliveryFails_AndNothingIsThrown()
    {
        var delivery = await Sender(null).SendAsync(Message, CancellationToken.None);

        Assert.False(delivery.Sent);
        Assert.Equal("no active platform email provider", delivery.Error);
    }

    // The row names a settings key that holds nothing: reported, and the message names the key, never a secret.
    [Fact]
    public async Task WhenTheCredentialRefNamesNothing_TheDeliveryFails_NamingTheKeyAndNoSecret()
    {
        var sender = Sender(new PlatformProvider("smtp", "no-reply@platform.test", "platform-email"));

        var delivery = await sender.SendAsync(Message, CancellationToken.None);

        Assert.False(delivery.Sent);
        Assert.Equal("no credentials under platform-email", delivery.Error);
    }

    // The credentials come from Notifications:Email:Providers:<credential_ref> — the database names the key and
    // the settings hold the value (§2.6). Here the key exists but the host does not answer, which is the failure
    // the sender must report rather than throw; what matters is that it got past the lookup.
    [Fact]
    public async Task TheCredentialsAreReadFromTheKeyTheProviderAccountNames()
    {
        var sender = Sender(
            new PlatformProvider("smtp", "no-reply@platform.test", "platform-email"),
            ("Notifications:Email:Providers:platform-email:Host", "127.0.0.1"),
            ("Notifications:Email:Providers:platform-email:Port", "1"),
            ("Notifications:Email:Providers:platform-email:UseStartTls", "false"));

        var delivery = await sender.SendAsync(Message, CancellationToken.None);

        Assert.False(delivery.Sent);
        Assert.NotEqual("no credentials under platform-email", delivery.Error);
        Assert.NotNull(delivery.Error);
    }

    // A channel this sender does not serve is refused by it, rather than sent down the wrong wire.
    [Fact]
    public async Task ItRefusesAMessageForAnotherChannel()
    {
        var delivery = await Sender(new PlatformProvider("smtp", "s@platform.test", "platform-email"))
            .SendAsync(Message with { Channel = (NotificationChannel)99 }, CancellationToken.None);

        Assert.False(delivery.Sent);
    }
}
