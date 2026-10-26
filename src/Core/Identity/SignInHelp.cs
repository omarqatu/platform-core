using Core.Notifications;

namespace Core.Identity;

/// <summary>
/// <c>POST /api/members/{membershipId}/sign-in-help</c> (the project owner's item 37, group ج), bound from
/// <c>SignInHelp</c>. Both limits are marked assumptions <c>[—]</c> until the project owner settles them at launch,
/// as the invitation limits are (OPEN_ITEMS 106).
/// </summary>
public sealed class SignInHelpSettings
{
    /// <summary>How many messages one member may be sent in <see cref="PerMemberWindow"/>. Marked `[—]`.</summary>
    public int PerMember { get; set; } = 1;

    public TimeSpan PerMemberWindow { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// How many messages one organization may send in <see cref="PerTenantWindow"/>, whoever sends them. Marked
    /// `[—]`. This is outbound mail in the organization's name, through one sending account every tenant shares.
    /// </summary>
    public int PerTenant { get; set; } = 20;

    public TimeSpan PerTenantWindow { get; set; } = TimeSpan.FromHours(24);
}

/// <summary>
/// The two sign-in-help limits, **in memory** (the project owner's decision, 2026-10-25). They reset when Api
/// restarts, and each Api instance counts on its own — OPEN_ITEMS 112 records both. They are not counted from
/// <c>audit_log</c>, which records every send: <c>audit_read</c> admits a reader only under <c>scope_all</c>, so for a
/// manager whose own mode is <c>assigned</c> the count would read zero, silently, and the limit would not exist.
/// <para>
/// <see cref="TryTake"/> checks both limits before taking from either, so a refusal by one consumes nothing of the
/// other — and the endpoint calls it only once every other check has passed, so a refused request consumes nothing.
/// </para>
/// </summary>
public sealed class SignInHelpLimiter(SignInHelpSettings settings)
{
    private readonly object gate = new();
    private readonly Dictionary<(Guid Tenant, Guid Membership), List<DateTime>> byMember = new();
    private readonly Dictionary<Guid, List<DateTime>> byTenant = new();

    public bool TryTake(Guid tenantId, Guid membershipId, DateTime now)
    {
        lock (gate)
        {
            var member = Recent(byMember, (tenantId, membershipId), now - settings.PerMemberWindow);
            var tenant = Recent(byTenant, tenantId, now - settings.PerTenantWindow);
            if (member.Count >= settings.PerMember || tenant.Count >= settings.PerTenant)
                return false;
            member.Add(now);
            tenant.Add(now);
            return true;
        }
    }

    /// <summary>The key's sends still inside the window, the older ones dropped so the lists stay bounded.</summary>
    private static List<DateTime> Recent<TKey>(Dictionary<TKey, List<DateTime>> sends, TKey key, DateTime since) where TKey : notnull
    {
        if (!sends.TryGetValue(key, out var list))
            sends[key] = list = [];
        list.RemoveAll(at => at <= since);
        return list;
    }
}

/// <summary>
/// The message sign-in help sends (item 37) — **bilingual, Arabic first, then English**, the invitation's shape
/// (<see cref="Provisioning.InvitationEmail"/>; single-language mail is OPEN_ITEMS 102, not this item). It carries
/// the member's username and the forgot-password page, and **no token**: the member starts the recovery themselves,
/// so the only token is the one their own request mints (the project owner's decision; Amendment 01 §5.1). Nothing
/// here is logged.
/// </summary>
public static class SignInHelpEmail
{
    public const string Subject = "المساعدة في تسجيل الدخول | Help signing in";

    public static NotificationMessage For(string recipient, string username, string forgotLink) =>
        new(NotificationChannel.Email, recipient, Subject, Body(username, forgotLink));

    private static string Body(string username, string forgotLink) => $"""
        طلب أحد مديري جهتك على المنصة إرسال هذه الرسالة إليك لمساعدتك في تسجيل الدخول.

        اسم المستخدم الخاص بك: {username}

        إن نسيت كلمة المرور، افتح هذه الصفحة واكتب عنوان البريد هذا، وستصلك رسالة لتعيينها من جديد:

        {forgotLink}

        إن لم تكن بحاجة إلى مساعدة، تجاهل هذه الرسالة ولن يتغير شيء.

        —

        A manager of your organization on the platform asked for this message to be sent to you, to help you sign in.

        Your username: {username}

        If you have forgotten your password, open this page and enter this e-mail address, and you will receive a
        message to set a new one:

        {forgotLink}

        If you do not need help, ignore this message and nothing will change.
        """;
}
