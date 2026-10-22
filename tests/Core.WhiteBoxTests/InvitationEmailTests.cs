using Core.Notifications;
using Core.Provisioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Core.WhiteBoxTests;

/// <summary>
/// The invitation e-mail (OPEN_ITEMS 60) — what enters the dispatcher, the shape of the link, and the silence of
/// the logs. [W], because none of the three is observable from outside the implementation: the message never
/// reaches a database table (<c>notification_log</c> does not exist — OPEN_ITEMS 89) and the provider is not
/// called on the request's path.
/// </summary>
public class InvitationEmailTests
{
    private static readonly Uri Origin = new("https://platform.example.com");
    private static readonly DateTime Expires = new(2026, 11, 3, 9, 30, 0, DateTimeKind.Utc);
    private const string Token = "a-token-value-nobody-should-log";

    private static string Link() => AcceptanceLink.For(Origin, Guid.Parse("0199c000-0000-7000-8000-00000000000a"), Token);

    // ---- The link.

    /// <summary>
    /// The token is in the **fragment**, and the fragment is the whole protection: a browser never sends it to a
    /// server, so the token reaches no access log and no Referer header (DEVELOPER_GUIDE §9). A link that put it
    /// in the path or the query would be logged by every proxy between the invitee and the platform.
    /// </summary>
    [Fact]
    public void TheLink_CarriesTheTokenInTheFragmentAlone()
    {
        var link = Link();
        var uri = new Uri(link);

        Assert.Equal("/app/invitations/accept", uri.AbsolutePath);
        Assert.Empty(uri.Query);
        Assert.Contains(Token, uri.Fragment);
        Assert.DoesNotContain(Token, link[..link.IndexOf('#')]);
    }

    /// <summary>One definition for both writers: Ops prints this, and the e-mail below carries it.</summary>
    [Fact]
    public void TheLink_IsTheSameOneOpsPrints()
    {
        var tenant = Guid.Parse("0199c000-0000-7000-8000-00000000000a");

        Assert.Equal($"https://platform.example.com/app/invitations/accept#tenant={tenant:D}&token={Token}",
            AcceptanceLink.For(Origin, tenant, Token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("platform.example.com")]              // no scheme
    [InlineData("/app/invitations/accept")]           // a bare path: the OPEN_ITEMS 105 shape
    [InlineData("https://platform.example.com/app")]  // a path
    [InlineData("https://platform.example.com/?a=1")] // a query
    [InlineData("ftp://platform.example.com")]        // not http(s)
    public void AnOriginThatIsNotOne_IsRefused(string? value)
    {
        Assert.False(new InvitationSettings { PublicOrigin = value }.TryOrigin(out _));
    }

    [Fact]
    public void AnOrigin_IsAccepted()
    {
        Assert.True(new InvitationSettings { PublicOrigin = "https://platform.example.com" }.TryOrigin(out var origin));
        Assert.Equal("https://platform.example.com", origin.GetLeftPart(UriPartial.Authority));
    }

    // ---- The message that enters the dispatcher.

    [Fact]
    public void TheMessage_IsEmail_ToTheInvitee_CarryingTheLink()
    {
        var message = InvitationEmail.For("ahmad@example.test", Link(), Expires);

        Assert.Equal(NotificationChannel.Email, message.Channel);
        Assert.Equal("ahmad@example.test", message.Recipient);
        Assert.Contains(Link(), message.Body);
    }

    /// <summary>
    /// Bilingual, Arabic first (the project owner's decision): an invitee has no account, so
    /// <c>users.language</c> does not exist for them and their language is unknown. Guessing it from the
    /// inviter's would be guessing.
    /// </summary>
    [Fact]
    public void TheMessage_IsInBothLanguages_ArabicFirst()
    {
        var body = InvitationEmail.For("ahmad@example.test", Link(), Expires).Body;

        var arabic = body.IndexOf("دُعيت", StringComparison.Ordinal);
        var english = body.IndexOf("You have been invited", StringComparison.Ordinal);
        Assert.True(arabic >= 0, "the Arabic text is missing");
        Assert.True(english >= 0, "the English text is missing");
        Assert.True(arabic < english, "Arabic comes first");
        Assert.Contains("دعوة للانضمام", InvitationEmail.Subject);
        Assert.Contains("An invitation to join", InvitationEmail.Subject);
    }

    /// <summary>An invitation is not a membership (§0): the message says so, in both languages.</summary>
    [Fact]
    public void TheMessage_SaysNobodyIsAddedBeforeTheyAccept_AndWhenTheLinkDies()
    {
        var body = InvitationEmail.For("ahmad@example.test", Link(), Expires).Body;

        Assert.Contains("لا يُضاف أحد قبل أن يقبل", body);
        Assert.Contains("No one is added before they accept", body);
        Assert.Contains("2026-11-03", body);
    }

    /// <summary>
    /// Dormant by decision, and asserted so it stays a decision: Gmail SMTP rewrites no links, so nothing sets a
    /// header today. The field exists for the provider change — a relay that rewrites links by default would put
    /// the token through its own servers, and a dashboard setting is something somebody has to remember.
    /// </summary>
    [Fact]
    public void TheMessage_SetsNoHeadersToday_AndTheFieldExistsForWhenItMust()
    {
        var message = InvitationEmail.For("ahmad@example.test", Link(), Expires);

        Assert.Null(message.Headers);
        var withHeader = message with { Headers = new Dictionary<string, string> { ["X-PM-TrackLinks"] = "None" } };
        Assert.Equal("None", withHeader.Headers!["X-PM-TrackLinks"]);
    }

    // ---- The logs.

    /// <summary>
    /// Nothing the delivery path writes carries the recipient, the body or the token. The one in the sender is
    /// the case that actually happens in a misconfigured deployment — no provider account — and it is the one
    /// most likely to be written carelessly, because it is an error path.
    /// </summary>
    [Fact]
    public async Task TheSender_LogsNoAddress_NoBody_NoToken_WhenThereIsNoProviderAccount()
    {
        var log = new CapturingLogger<EmailNotificationSender>();
        var sender = new EmailNotificationSender(
            new NoProviderAccounts(),
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
            Options.Create(new EmailSenderOptions()),
            log);

        var delivery = await sender.SendAsync(InvitationEmail.For("ahmad@example.test", Link(), Expires), CancellationToken.None);

        Assert.False(delivery.Sent);
        var written = string.Join("\n", log.Lines);
        Assert.NotEmpty(written);
        Assert.DoesNotContain("ahmad@example.test", written, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Token, written, StringComparison.Ordinal);
        Assert.DoesNotContain("invitations/accept", written, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class NoProviderAccounts : IPlatformProviderAccounts
    {
        public Task<PlatformProvider?> ForAsync(NotificationChannel channel, CancellationToken cancellationToken) =>
            Task.FromResult<PlatformProvider?>(null);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Lines.Add(formatter(state, exception));
            if (exception is not null) Lines.Add(exception.ToString());
        }
    }
}
