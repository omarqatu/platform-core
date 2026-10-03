import { Navigate, Route, Routes } from 'react-router';
import { LoginScreen } from './screens/auth/LoginScreen';
import { NotFoundScreen } from './screens/status/StatusScreens';
import { SubscriptionsScreen } from './screens/subscriptions/SubscriptionsScreen';
import { TenantSelectScreen } from './screens/tenants/TenantSelectScreen';
import { RequireAuth, RequireTenant } from './session/guards';

/**
 * The interface's paths. /login is open; /organizations needs a session; the organization's screens live under /app/,
 * one subtree per organization (RequireTenant). Anything else is "not found". The API lives under /api only, and Api
 * serves index.html for any other page no route matches (WebInterface) — except the original T8 screen
 * (/subscriptions/screen), the one server-rendered page, which the interface never uses.
 */
export function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginScreen />} />
      <Route element={<RequireAuth />}>
        <Route path="/organizations" element={<TenantSelectScreen />} />
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
