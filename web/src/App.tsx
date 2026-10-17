import { Navigate, Route, Routes } from 'react-router';
import { ACCEPT_PATH } from './invitations/pendingInvitation';
import { RESET_PATH } from './password/pendingResetToken';
import { LoginScreen } from './screens/auth/LoginScreen';
import { AcceptInvitationScreen } from './screens/invitations/AcceptInvitationScreen';
import { ChangePasswordScreen, CHANGE_PATH } from './screens/password/ChangePasswordScreen';
import { ForgotPasswordScreen, FORGOT_PATH } from './screens/password/ForgotPasswordScreen';
import { ResetPasswordScreen } from './screens/password/ResetPasswordScreen';
import { NotFoundScreen } from './screens/status/StatusScreens';
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
        </Route>
      </Route>
      <Route path="*" element={<NotFoundScreen />} />
    </Routes>
  );
}
