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
  no_active_tenant: { id: 'errors.no_active_tenant', defaultMessage: 'Choose an organization first.' },
  tenant_not_active: { id: 'errors.tenant_not_active', defaultMessage: 'This organization is not active.' },
  step_up_required: {
    id: 'errors.step_up_required',
    defaultMessage: 'This organization requires a stronger sign-in method, which is not available yet.',
  },
  not_a_member: { id: 'errors.not_a_member', defaultMessage: 'You are not an active member of this organization.' },
  session_expired: {
    id: 'errors.session_expired',
    defaultMessage: 'Your session has ended because the password was changed. Sign in again.',
  },
  too_many_attempts: {
    id: 'errors.too_many_attempts',
    defaultMessage: 'Too many attempts from this device. Wait a few minutes and try again.',
  },
  not_permitted: { id: 'errors.not_permitted', defaultMessage: 'You do not have permission to do this.' },
  membership_scope_missing: {
    id: 'errors.membership_scope_missing',
    defaultMessage: 'Your membership has no scope. Contact your administrator.',
  },
  invalid_value: { id: 'errors.invalid_value', defaultMessage: 'One of the values is not valid.' },
  invalid_reference: { id: 'errors.invalid_reference', defaultMessage: 'The selected item is not available.' },
  invalid_cursor: { id: 'errors.invalid_cursor', defaultMessage: 'The list has changed. Reload the page.' },
  conflict: { id: 'errors.conflict', defaultMessage: 'This already exists.' },
  last_owner: { id: 'errors.last_owner', defaultMessage: 'The organization must keep at least one active owner.' },
  last_all_member: {
    id: 'errors.last_all_member',
    defaultMessage: 'The organization must keep at least one active member with the All clients scope.',
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
  username_taken: { id: 'errors.username_taken', defaultMessage: 'This username is taken. Choose another.' },
  already_member: { id: 'errors.already_member', defaultMessage: 'You are already a member of this organization.' },
  role_templates_unavailable: {
    id: 'errors.role_templates_unavailable',
    defaultMessage: 'The organization could not be set up. Contact support.',
  },
  invalid_token: {
    id: 'errors.invalid_token',
    defaultMessage: 'This link is not valid or has expired. Ask for a new one.',
  },
  // The password rule (OPEN_ITEMS 62). The number is written into the sentence rather than interpolated, because
  // errorMessage() is formatted with no values at every call site. The screens show this same message when they
  // catch the rule before sending, so the wording is one wording whoever caught it.
  password_too_short: {
    id: 'errors.password_too_short',
    defaultMessage: 'The password must be at least 12 characters. Nothing else is required: no symbol, no digit, no capital letter.',
  },
  // The subscription's lifecycle (Amendment 01 §2.9): one message for both acts, because the state is the reason
  // for both refusals and the reader needs to know the state, not which button they pressed.
  subscription_not_active: {
    id: 'errors.subscription_not_active',
    defaultMessage: 'This subscription has already been cancelled or renewed, so it cannot be changed.',
  },
  unknown: { id: 'errors.unknown', defaultMessage: 'Something went wrong. Try again.' },
});

export type ApiErrorCode = Exclude<keyof typeof apiErrors, 'unknown'>;

/** The message for an API error code; "unknown" for a code this interface does not know, or none. */
export function errorMessage(code: string | null | undefined): MessageDescriptor {
  return code && Object.hasOwn(apiErrors, code) && code !== 'unknown' ? apiErrors[code as ApiErrorCode] : apiErrors.unknown;
}
