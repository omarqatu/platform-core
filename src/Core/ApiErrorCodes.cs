namespace Core;

/// <summary>
/// Every code the API returns in an error body — <c>{ "error": "&lt;code&gt;" }</c> — in one place. The API returns no
/// human text: the web interface translates each code (web/src/i18n/apiErrors.ts). The values are a contract: never
/// rename one. checks/local/export-api-error-codes.py exports this file to checks/local/api-error-codes.json, which
/// check-api-error-codes compares with the interface's messages, and which fails on a code written as a literal
/// anywhere else in src/.
/// </summary>
public static class ApiErrorCodes
{
    // Request and session
    public const string CsrfRejected = "csrf_rejected";
    public const string InvalidRequest = "invalid_request";
    public const string InvalidCredentials = "invalid_credentials";
    public const string NoActiveTenant = "no_active_tenant";
    public const string TenantNotActive = "tenant_not_active";
    public const string StepUpRequired = "step_up_required";
    public const string NotAMember = "not_a_member";
    // Amendment 01 §2.1, §5.2: the cookie's security stamp is not the user's current one — the password changed.
    public const string SessionExpired = "session_expired";
    // Amendment 01 §5.2 (OPEN_ITEMS 16): the window's failures from this address reached the ceiling.
    public const string TooManyAttempts = "too_many_attempts";

    // Permission and data (3.5, 5, 3.3)
    public const string NotPermitted = "not_permitted";
    public const string MembershipScopeMissing = "membership_scope_missing";
    public const string InvalidValue = "invalid_value";
    public const string InvalidReference = "invalid_reference";
    public const string InvalidCursor = "invalid_cursor";
    public const string Conflict = "conflict";

    // Members: items a and g (3.10)
    public const string LastOwner = "last_owner";
    public const string LastAllMember = "last_all_member";

    // Provisioning (4.4, 4.5)
    public const string InvalidInvitation = "invalid_invitation";
    public const string InvitationExpired = "invitation_expired";
    public const string EmailMismatch = "email_mismatch";
    public const string AccountExists = "account_exists";
    public const string UsernameTaken = "username_taken";
    public const string AlreadyMember = "already_member";
    public const string RoleTemplatesUnavailable = "role_templates_unavailable";

    // Password recovery (Amendment 01 §5.1): unknown, used or expired — one code, so the response says nothing
    // about which.
    public const string InvalidToken = "invalid_token";

    // The password rule (OPEN_ITEMS 62): shorter than PasswordPolicy.MinimumLength. Length is the whole rule, so
    // the code says length and nothing vaguer; a composition rule, if one ever arrives, gets a code of its own.
    public const string PasswordTooShort = "password_too_short";
}
