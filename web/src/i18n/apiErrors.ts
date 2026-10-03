import { defineMessages, type MessageDescriptor } from 'react-intl';

/**
 * One message per code the API returns in { "error": "<code>" } — the codes of src/Core/ApiErrorCodes.cs, exported to
 * checks/local/api-error-codes.json. check-api-error-codes requires both sets to be equal (with "unknown" added here).
 * Every id is written out: an id built at run time (`errors.${code}`) would escape extraction and the checks.
 */
export const apiErrors = defineMessages({
  invalid_request: { id: 'errors.invalid_request', defaultMessage: 'The request is not valid.' },
  csrf_rejected: {
    id: 'errors.csrf_rejected',
    defaultMessage: 'The request was refused for security reasons. Reload the page and try again.',
  },
  invalid_credentials: { id: 'errors.invalid_credentials', defaultMessage: 'The username or password is incorrect.' },
  no_active_tenant: { id: 'errors.no_active_tenant', defaultMessage: 'Choose a tenant first.' },
  tenant_not_active: { id: 'errors.tenant_not_active', defaultMessage: 'This tenant is not active.' },
  step_up_required: {
    id: 'errors.step_up_required',
    defaultMessage: 'This tenant requires another sign-in method.',
  },
  not_a_member: { id: 'errors.not_a_member', defaultMessage: 'You are not an active member of this tenant.' },
  not_permitted: { id: 'errors.not_permitted', defaultMessage: 'You do not have permission to do this.' },
  membership_scope_missing: {
    id: 'errors.membership_scope_missing',
    defaultMessage: 'Your membership has no scope. Contact your administrator.',
  },
  invalid_value: { id: 'errors.invalid_value', defaultMessage: 'One of the values is not valid.' },
  invalid_reference: { id: 'errors.invalid_reference', defaultMessage: 'The selected item is not available.' },
  invalid_cursor: { id: 'errors.invalid_cursor', defaultMessage: 'The list has changed. Reload the page.' },
  conflict: { id: 'errors.conflict', defaultMessage: 'This already exists.' },
  last_owner: { id: 'errors.last_owner', defaultMessage: 'The tenant must keep at least one active owner.' },
  last_all_member: {
    id: 'errors.last_all_member',
    defaultMessage: 'The tenant must keep at least one active member who sees all data.',
  },
  invalid_invitation: { id: 'errors.invalid_invitation', defaultMessage: 'This invitation is not valid.' },
  invitation_expired: { id: 'errors.invitation_expired', defaultMessage: 'This invitation has expired.' },
  email_mismatch: {
    id: 'errors.email_mismatch',
    defaultMessage: 'This invitation was sent to a different email address.',
  },
  account_exists: {
    id: 'errors.account_exists',
    defaultMessage: 'An account with this email already exists. Sign in, then accept the invitation.',
  },
  already_member: { id: 'errors.already_member', defaultMessage: 'You are already a member of this tenant.' },
  role_templates_unavailable: {
    id: 'errors.role_templates_unavailable',
    defaultMessage: 'The tenant could not be set up. Contact support.',
  },
  unknown: { id: 'errors.unknown', defaultMessage: 'Something went wrong. Try again.' },
});

export type ApiErrorCode = Exclude<keyof typeof apiErrors, 'unknown'>;

/** The message for an API error code; "unknown" for a code this interface does not know, or none. */
export function errorMessage(code: string | null | undefined): MessageDescriptor {
  return code && Object.hasOwn(apiErrors, code) && code !== 'unknown' ? apiErrors[code as ApiErrorCode] : apiErrors.unknown;
}
