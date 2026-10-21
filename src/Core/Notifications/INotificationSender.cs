namespace Core.Notifications;

/// <summary>
/// The delivery channels of Amendment 01 D3. Email is built (A3); WhatsApp, SMS and Telegram are the declared
/// order after it. The value is the <c>channel</c> column of <c>provider_accounts</c> and of
/// <c>tenant_notification_settings</c>, so the names must match the database's CHECK constraint.
/// </summary>
public enum NotificationChannel
{
    Email,
}

public static class NotificationChannels
{
    /// <summary>The channel as the database writes it (<c>provider_accounts_channel_check</c>).</summary>
    public static string Code(this NotificationChannel channel) => channel switch
    {
        NotificationChannel.Email => "email",
        _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "no code for this channel"),
    };
}

/// <summary>
/// One message for one recipient. The body is plain text: no channel here renders anything else.
/// <para>
/// <paramref name="Headers"/> is **dormant and deliberate** (the project owner's decision, 2026-10-21). Production
/// sends through Gmail SMTP, which rewrites no links and has no dashboard to switch click tracking off in, so
/// nothing sets it today. It exists because the guard has to outlive the provider: the day this moves to a relay
/// that rewrites links by default — SendGrid, Mailgun, Postmark all can — an invitation's token would transit that
/// provider's servers, and the fragment the link hides it in would not save it. Disabling tracking per message is
/// a header (<c>X-PM-TrackLinks: None</c>, <c>X-Mailgun-Track-Clicks: no</c>), and a header needs somewhere to
/// live before anyone needs it. A dashboard setting is not a guard: it is something somebody has to remember.
/// </para>
/// </summary>
public sealed record NotificationMessage(
    NotificationChannel Channel,
    string Recipient,
    string Subject,
    string Body,
    IReadOnlyDictionary<string, string>? Headers = null);

/// <summary>
/// What the provider answered. <c>ProviderMessageId</c> and <c>Error</c> are the two
/// columns the outbox of §5.5 keeps (<c>notification_log.provider_message_id</c>, <c>.error</c>) — the sender
/// returns them so the delivery job can write them; the paths that send directly (invitations, passwords) only
/// read <c>Sent</c>.
/// </summary>
public sealed record NotificationDelivery(bool Sent, string? ProviderMessageId, string? Error)
{
    public static NotificationDelivery Delivered(string? providerMessageId = null) =>
        new(true, providerMessageId, null);

    public static NotificationDelivery Failed(string error) => new(false, null, error);
}

/// <summary>
/// The sender of Amendment 01 §5.5 — in <c>Core</c>, not in the module, because invitations and passwords use it
/// too. One implementation per channel; the provider account comes from <c>provider_accounts</c> and the secret
/// from the settings key its <c>credential_ref</c> names, never from the database (§2.6).
/// <para>
/// An implementation never throws for a delivery that failed: it returns <see cref="NotificationDelivery.Failed"/>
/// with the reason, because a channel failing must not stop the others (§5.3) and must not turn a uniform 204
/// into a 500 (§5.1).
/// </para>
/// </summary>
public interface INotificationSender
{
    NotificationChannel Channel { get; }

    Task<NotificationDelivery> SendAsync(NotificationMessage message, CancellationToken cancellationToken);
}
