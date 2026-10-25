using System.Net;
using System.Net.Sockets;
using System.Text;
using Core.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Core.WhiteBoxTests;

/// <summary>
/// [W] The sender's recipient (the project owner's item 35, group ج): one message goes to <b>exactly one</b>
/// recipient — the address it was given — or to none, reported as <c>malformed recipient</c> and never thrown.
/// <para>
/// Measured on main before this change: <c>new MailMessage(from, "adnan", …)</c> throws <c>FormatException</c>
/// outside the sender's <c>try</c>, breaking <see cref="INotificationSender"/>'s own contract, and
/// <c>"a@x.com, b@y.com"</c> is parsed as two recipients and sent to both. The regex is not what catches the second
/// case here; counting the recipients that reach the provider is. The provider below is a fake SMTP server on
/// loopback that records every <c>RCPT TO</c> it is sent.
/// </para>
/// </summary>
public class SenderRecipientTests
{
    // The control: a well-formed address reaches the provider as one recipient, and is delivered.
    [Fact]
    public async Task AWellFormedAddress_ReachesTheProvider_AsExactlyOneRecipient()
    {
        await using var smtp = FakeSmtp.Start();
        var log = new CapturingLogger<EmailNotificationSender>();

        var delivery = await Sender(smtp.Port, log).SendAsync(Message("someone@b35.test"), CancellationToken.None);

        Assert.True(delivery.Sent, delivery.Error);
        Assert.Equal(["<someone@b35.test>"], await smtp.RecipientsAsync());
    }

    public static TheoryData<string> Malformed => new()
    {
        "adnan",                     // the production rows: FormatException on main
        "adnan@",
        "a@b35.test, b@b35.test",    // two recipients on main
        "a@b35.test;b@b35.test",
        "a b@b35.test",              // sent to b@b35.test on main
        "\"Adnan\" <adnan@b35.test>", // a display name: sent to an address that is not the message's own text
    };

    [Theory]
    [MemberData(nameof(Malformed))]
    public async Task AMalformedRecipient_IsReportedNotThrown_AndNothingReachesTheProvider(string recipient)
    {
        await using var smtp = FakeSmtp.Start();
        var log = new CapturingLogger<EmailNotificationSender>();
        var sender = Sender(smtp.Port, log);

        var delivery = await sender.SendAsync(Message(recipient), CancellationToken.None);

        Assert.Empty(await smtp.RecipientsAsync());
        Assert.False(delivery.Sent);
        Assert.Equal("malformed recipient", delivery.Error);
        // Loud, and still without the address: an address in a log is the enumeration forgot's 204 prevents.
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Error);
        Assert.DoesNotContain(log.Entries, e => e.Text.Contains("b35.test", StringComparison.OrdinalIgnoreCase)
                                                || e.Text.Contains("adnan", StringComparison.OrdinalIgnoreCase));
    }

    private static NotificationMessage Message(string recipient) =>
        new(NotificationChannel.Email, recipient, "Subject", "Body");

    private static EmailNotificationSender Sender(int port, ILogger<EmailNotificationSender> log) =>
        new(new Account(),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Notifications:Email:Providers:b35:Host"] = "127.0.0.1",
                ["Notifications:Email:Providers:b35:Port"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Notifications:Email:Providers:b35:UseStartTls"] = "false",
            }).Build(),
            Options.Create(new EmailSenderOptions()),
            log);

    private sealed class Account : IPlatformProviderAccounts
    {
        public Task<PlatformProvider?> ForAsync(NotificationChannel channel, CancellationToken cancellationToken) =>
            Task.FromResult<PlatformProvider?>(new PlatformProvider("smtp", "no-reply@platform.test", "b35"));
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Text)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception) + (exception is null ? "" : "\n" + exception)));
    }

    /// <summary>
    /// The smallest SMTP server <c>SmtpClient</c> will talk to: every command answered with success, every
    /// <c>RCPT TO</c> recorded. One connection at most — a sender that opens none leaves the list empty.
    /// </summary>
    private sealed class FakeSmtp : IAsyncDisposable
    {
        private readonly TcpListener listener;
        private readonly CancellationTokenSource stop = new();
        private readonly Task serving;
        private readonly List<string> recipients = [];

        private FakeSmtp()
        {
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            serving = ServeAsync();
        }

        public static FakeSmtp Start() => new();

        public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

        public async Task<IReadOnlyList<string>> RecipientsAsync()
        {
            await stop.CancelAsync();
            try { await serving; } catch (OperationCanceledException) { }
            lock (recipients) return [.. recipients];
        }

        private async Task ServeAsync()
        {
            using var client = await listener.AcceptTcpClientAsync(stop.Token);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
            await writer.WriteLineAsync("220 fake");
            while (await reader.ReadLineAsync(stop.Token) is { } line)
            {
                var verb = line.Split(' ', 2)[0].ToUpperInvariant();
                if (verb == "RCPT")
                    lock (recipients) recipients.Add(line[(line.IndexOf(':') + 1)..].Trim());
                if (verb == "DATA")
                {
                    await writer.WriteLineAsync("354 go on");
                    while (await reader.ReadLineAsync(stop.Token) is { } body && body != ".") { }
                    await writer.WriteLineAsync("250 queued");
                    continue;
                }
                if (verb == "QUIT")
                {
                    await writer.WriteLineAsync("221 bye");
                    return;
                }
                await writer.WriteLineAsync("250 ok");
            }
        }

        public async ValueTask DisposeAsync()
        {
            await stop.CancelAsync();
            listener.Stop();
            try { await serving; } catch (Exception) { }
            stop.Dispose();
        }
    }
}
