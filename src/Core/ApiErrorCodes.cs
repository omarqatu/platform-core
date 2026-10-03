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
    public const string InvalidRequest = "invalid_request";
    public const string InvalidCredentials = "invalid_credentials";
    public const string NoActiveTenant = "no_active_tenant";
    public const string TenantNotActive = "tenant_not_active";
    public const string StepUpRequired = "step_up_required";
    public const string NotAMember = "not_a_member";

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
    public const string AlreadyMember = "already_member";
    public const string RoleTemplatesUnavailable = "role_templates_unavailable";
}
