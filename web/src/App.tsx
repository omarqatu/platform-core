import { Navigate, Route, Routes } from 'react-router';
import { ACCEPT_PATH } from './invitations/pendingInvitation';
import { RESET_PATH } from './password/pendingResetToken';
import { LoginScreen } from './screens/auth/LoginScreen';
import { ClientFormScreen, EDIT_PATH as CLIENT_EDIT_PATH, NEW_PATH as CLIENT_NEW_PATH } from './screens/clients/ClientFormScreen';
import { ClientsScreen, LIST_PATH as CLIENTS_PATH } from './screens/clients/ClientsScreen';
import { AcceptInvitationScreen } from './screens/invitations/AcceptInvitationScreen';
import { InviteMemberScreen } from './screens/members/InviteMemberScreen';
import { MemberRolesScreen } from './screens/members/MemberRolesScreen';
import { INVITE_PATH as MEMBER_INVITE_PATH, LIST_PATH as MEMBERS_PATH, MembersScreen } from './screens/members/MembersScreen';
import { ChangePasswordScreen, CHANGE_PATH } from './screens/password/ChangePasswordScreen';
import { ForgotPasswordScreen, FORGOT_PATH } from './screens/password/ForgotPasswordScreen';
import { ResetPasswordScreen } from './screens/password/ResetPasswordScreen';
import { NotFoundScreen } from './screens/status/StatusScreens';
import { EDIT_PATH, NEW_PATH, SubscriptionFormScreen } from './screens/subscriptions/SubscriptionFormScreen';
import { RENEW_PATH, RenewSubscriptionScreen } from './screens/subscriptions/RenewSubscriptionScreen';
import { SubscriptionsScreen } from './screens/subscriptions/SubscriptionsScreen';
import { TenantSelectScreen } from './screens/tenants/TenantSelectScreen';
import { RequireAuth, RequireTenant } from './session/guards';

/**
 * The interface's paths. /login is open; so is /app/invitations/accept, signed in or not, and in no organization,
 * and so are the two open screens of password recovery (/app/password/forgot and /app/password/reset — the link
 * the recovery e-mail carries, Identity:ForgotPassword:ResetLinkTemplate); /organizations and changing one's own
 * password need a session; the organization's screens live under /app/, one subtree per organization
 * (RequireTenant). Anything else is "not found". The API lives under /api only, and Api
 * serves index.html for any other page no route matches (WebInterface) — except the original T8 screen
 * (/subscriptions/screen), the one server-rendered page, which the interface never uses.
 */
export function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginScreen />} />
      {/* Open, signed in or not, and outside every organization: accepting enters none. */}
      <Route path={ACCEPT_PATH} element={<AcceptInvitationScreen />} />
      {/* Recovering a password: both open, and neither enters an organization. */}
      <Route path={FORGOT_PATH} element={<ForgotPasswordScreen />} />
      <Route path={RESET_PATH} element={<ResetPasswordScreen />} />
      <Route element={<RequireAuth />}>
        <Route path="/organizations" element={<TenantSelectScreen />} />
        {/* Changing one's own password needs a session and no organization (PUT /me/password, app.user_id alone). */}
        <Route path={CHANGE_PATH} element={<ChangePasswordScreen />} />
        <Route element={<RequireTenant />}>
          <Route index element={<Navigate to="/app/subscriptions" replace />} />
          <Route path="/app" element={<Navigate to="/app/subscriptions" replace />} />
          <Route path="/app/subscriptions" element={<SubscriptionsScreen />} />
          {/* Creating and editing a subscription (Amendment 02 §3.2). Keyed by the mode: one never keeps the other's
              fields. */}
          <Route path={NEW_PATH} element={<SubscriptionFormScreen key="create" mode="create" />} />
          <Route path={EDIT_PATH} element={<SubscriptionFormScreen key="edit" mode="edit" />} />
          {/* Renewing one (Amendment 01 §2.9): a screen of its own, because it writes a new row rather than
              editing this one, and it takes one field. */}
          <Route path={RENEW_PATH} element={<RenewSubscriptionScreen />} />
          {/* The clients (Amendment 01 §2.8): the list, and the form, keyed by its mode. */}
          <Route path={CLIENTS_PATH} element={<ClientsScreen />} />
          <Route path={CLIENT_NEW_PATH} element={<ClientFormScreen key="create" mode="create" />} />
          <Route path={CLIENT_EDIT_PATH} element={<ClientFormScreen key="edit" mode="edit" />} />
          {/* The member screens (OPEN_ITEMS 100). Each is guarded by its endpoint's permission, not by the
              router: the sidebar hides the link, and whoever reaches the address anyway meets the server's
              refusal through ForbiddenScreen. */}
          <Route path={MEMBERS_PATH} element={<MembersScreen />} />
          <Route path={MEMBER_INVITE_PATH} element={<InviteMemberScreen />} />
          <Route path={`${MEMBERS_PATH}/:membershipId/roles`} element={<MemberRolesScreen />} />
        </Route>
      </Route>
      <Route path="*" element={<NotFoundScreen />} />
    </Routes>
  );
}
