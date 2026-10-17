namespace Core.Identity;

/// <summary>
/// The password rule, decided by the project owner on 2026-10-17 (OPEN_ITEMS 62): <b>minimum length 12, and no
/// composition rules</b> — no required symbol, digit or case. Length beats complexity, and composition rules push
/// people into predictable shapes. The maximum stays what every password endpoint already held, 1024.
/// <para>
/// The rule is the <b>server's</b>: every endpoint that writes a password checks it and answers
/// <see cref="ApiErrorCodes.PasswordTooShort"/>. The screens check the same rule before they send, so the
/// interface explains it without a round trip — the server is the enforcement, the screen is the courtesy.
/// The four entry points are <c>POST /auth/password/reset</c>, <c>PUT /me/password</c>,
/// <c>POST /invitations/accept</c> (the new-account branch) and <c>POST /provision/tenants</c> (Development and
/// CI only, T4.17 — and the one that creates the accounts the tests run on).
/// </para>
/// <para>
/// <b>Not <c>POST /auth/login</c></b>: existing passwords are not forced to rotate (that is a separate question
/// the project owner has not answered), so a password shorter than this still signs in. Checking the rule there
/// would lock those accounts out and would tell an anonymous caller what the rule is.
/// </para>
/// <para>
/// The interface keeps its own copies of the number — <c>MINIMUM_PASSWORD_LENGTH</c> in
/// <c>web/src/password/policy.ts</c>, and the number inside each sentence that states the rule — because the
/// screens check before they send. <c>checks/local/check-password-policy.sh</c> holds all of them to this
/// constant (OPEN_ITEMS 65): this one decides, and the check only reports that the rest agree.
/// </para>
/// </summary>
public static class PasswordPolicy
{
    /// <summary>The rule: twelve characters. Counted as .NET counts them — UTF-16 units, as <c>Length</c> does.</summary>
    public const int MinimumLength = 12;

    /// <summary>The ceiling every password endpoint already applied, unchanged by the rule.</summary>
    public const int MaximumLength = 1024;

    /// <summary>Whether a password may be written. A null or absent password is not acceptable either.</summary>
    public static bool IsAcceptable(string? password) =>
        password is { Length: >= MinimumLength and <= MaximumLength };

    /// <summary>
    /// Whether a password breaks <b>the rule</b>, as opposed to being a malformed field. The endpoints keep
    /// their own ceiling in the shape of the request — a password over <see cref="MaximumLength"/>, or absent,
    /// is <c>invalid_request</c> as it always was, because calling it "too short" would be untrue — and ask
    /// this afterwards for the one case <see cref="ApiErrorCodes.PasswordTooShort"/> names.
    /// </summary>
    public static bool IsTooShort(string password) => password.Length < MinimumLength;
}
