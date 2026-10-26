using Core.Identity;
using Core.Notifications;

namespace Core.WhiteBoxTests;

/// <summary>
/// [W] Sign-in help (the project owner's item 37, group ج): the in-memory limiter, whose window no HTTP test can wait
/// out, and the message, which never reaches a table. The limiter's numbers are marked assumptions <c>[—]</c> like
/// OPEN_ITEMS 106 — one per member per <c>[15 minutes]</c>, <c>[20]</c> per organization per <c>[24 hours]</c> — and
/// it resets with the process and is per instance (OPEN_ITEMS, group ج).
/// </summary>
public class SignInHelpTests
{
    private static readonly DateTime T0 = new(2026, 10, 25, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Tenant = Guid.CreateVersion7();

    private static SignInHelpLimiter Limiter() => new(new SignInHelpSettings());

    [Fact]
    public void TheDefaults_AreTheProposedNumbers()
    {
        var settings = new SignInHelpSettings();

        Assert.Equal(1, settings.PerMember);
        Assert.Equal(TimeSpan.FromMinutes(15), settings.PerMemberWindow);
        Assert.Equal(20, settings.PerTenant);
        Assert.Equal(TimeSpan.FromHours(24), settings.PerTenantWindow);
    }

    [Fact]
    public void OneMember_OnceInTheWindow_ThenAgainAfterIt()
    {
        var limiter = Limiter();
        var member = Guid.CreateVersion7();

        Assert.True(limiter.TryTake(Tenant, member, T0));
        Assert.False(limiter.TryTake(Tenant, member, T0.AddMinutes(14)));
        Assert.True(limiter.TryTake(Tenant, member, T0.AddMinutes(15).AddSeconds(1)));
    }

    [Fact]
    public void OneOrganization_IsStoppedAtItsCeiling_AndAnotherIsNot()
    {
        var limiter = Limiter();
        for (var i = 0; i < 20; i++)
            Assert.True(limiter.TryTake(Tenant, Guid.CreateVersion7(), T0.AddMinutes(i)));

        Assert.False(limiter.TryTake(Tenant, Guid.CreateVersion7(), T0.AddHours(1)));
        Assert.True(limiter.TryTake(Guid.CreateVersion7(), Guid.CreateVersion7(), T0.AddHours(1)));
        Assert.True(limiter.TryTake(Tenant, Guid.CreateVersion7(), T0.AddHours(24).AddSeconds(1)));
    }

    // A refusal by one limit takes nothing from the other: a member refused by the organization's ceiling has not
    // spent their own allowance, and a member refused by theirs has not spent the organization's.
    [Fact]
    public void ARefusal_ConsumesNeitherAllowance()
    {
        var limiter = Limiter();
        var member = Guid.CreateVersion7();
        Assert.True(limiter.TryTake(Tenant, member, T0));
        for (var i = 0; i < 5; i++)
            Assert.False(limiter.TryTake(Tenant, member, T0.AddMinutes(1)));

        for (var i = 0; i < 19; i++)
            Assert.True(limiter.TryTake(Tenant, Guid.CreateVersion7(), T0.AddMinutes(2)));
        var refusedByTheCeiling = Guid.CreateVersion7();
        Assert.False(limiter.TryTake(Tenant, refusedByTheCeiling, T0.AddMinutes(3)));
        Assert.True(limiter.TryTake(Guid.CreateVersion7(), refusedByTheCeiling, T0.AddMinutes(3)));
    }

    // ---- The message.

    private const string Link = "https://platform.example.com/app/password/forgot";

    [Fact]
    public void TheMessage_GoesToTheAddressGiven_WithTheUsernameAndTheForgotLink_ArabicFirst()
    {
        var message = SignInHelpEmail.For("aya@example.test", "aya.k", Link);

        Assert.Equal(NotificationChannel.Email, message.Channel);
        Assert.Equal("aya@example.test", message.Recipient);
        var arabic = message.Body.IndexOf("اسم المستخدم", StringComparison.Ordinal);
        var english = message.Body.IndexOf("username", StringComparison.OrdinalIgnoreCase);
        Assert.True(arabic >= 0 && english > arabic, message.Body);
        Assert.Equal(2, CountOf(message.Body, "aya.k"));
        Assert.Equal(2, CountOf(message.Body, Link));
    }

    // No token and no reset link: the member starts the recovery themselves (the project owner's decision).
    [Fact]
    public void TheMessage_CarriesNoTokenAndNoResetLink()
    {
        var message = SignInHelpEmail.For("aya@example.test", "aya.k", Link);

        Assert.DoesNotContain("token", message.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/password/reset", message.Body, StringComparison.Ordinal);
        Assert.DoesNotContain('#', message.Body);
    }

    private static int CountOf(string text, string part)
    {
        var count = 0;
        for (var at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal))
            count++;
        return count;
    }
}
