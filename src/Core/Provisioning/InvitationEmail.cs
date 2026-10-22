using Core.Notifications;

namespace Core.Provisioning;

/// <summary>
/// The message an invitation sends (OPEN_ITEMS 60; Amendment 01 D3, §5.5).
/// <para>
/// **Bilingual, Arabic first, then English** (the project owner's decision, 2026-10-21). Every other text in the
/// platform is written once per language and chosen by the reader's own setting — but an invitee has no account
/// yet, so <c>users.language</c> does not exist for them and their language is genuinely unknown. Guessing it
/// from the inviter's would be guessing; sending both is honest, and the message is four lines each way. (The
/// recovery e-mail is English only and hardcoded, which is the inconsistency OPEN_ITEMS 102 records: both should
/// one day be composed the same way, and that is not this change.)
/// </para>
/// <para>
/// The link's token lives in the **fragment** (<see cref="AcceptanceLink"/>), so a browser never sends it to a
/// server: not to an access log, not in a <c>Referer</c>. That protects it after the click. Before it, the
/// message passes through the provider — which is why <see cref="NotificationMessage.Headers"/> exists, dormant,
/// for a provider that rewrites links (Gmail SMTP, today's, does not).
/// </para>
/// <para>
/// Nothing here is logged: neither this class nor the dispatcher nor the sender ever writes the recipient or the
/// body, and there is no outbox row — <c>notification_log</c> does not exist yet (OPEN_ITEMS 89). The one place
/// the link exists outside the message is the creator's own screen, once (OPEN_ITEMS 101).
/// </para>
/// </summary>
public static class InvitationEmail
{
    public const string Subject = "دعوة للانضمام | An invitation to join";

    public static NotificationMessage For(string recipient, string link, DateTime expiresAt) =>
        new(NotificationChannel.Email, recipient, Subject, Body(link, expiresAt));

    /// <summary>
    /// Plain text, in both languages, with the link on its own line so every client makes it clickable. It says
    /// three things, because an invitation is not a membership (PLATFORM_CORE §0): that someone invited them,
    /// that nothing happens until they accept, and when the link stops working.
    /// </summary>
    internal static string Body(string link, DateTime expiresAt)
    {
        var day = expiresAt.ToString("yyyy-MM-dd");
        return $"""
                دُعيت للانضمام إلى جهة على المنصة.

                افتح هذا الرابط لقبول الدعوة وإنشاء حسابك. لا يُضاف أحد قبل أن يقبل، والرابط يتوقف عن العمل في {day}:

                {link}

                إن لم تكن تتوقع هذه الدعوة، تجاهل هذه الرسالة ولن يحدث شيء.

                —

                You have been invited to join an organization on the platform.

                Open this link to accept and set up your account. No one is added before they accept, and the link
                stops working on {day}:

                {link}

                If you were not expecting this, ignore this message and nothing will happen.
                """;
    }
}
