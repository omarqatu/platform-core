import { Navigate, Route, Routes } from 'react-router';
import { LoginScreen } from './screens/auth/LoginScreen';
import { NotFoundScreen } from './screens/status/StatusScreens';
import { SubscriptionsScreen } from './screens/subscriptions/SubscriptionsScreen';
import { TenantSelectScreen } from './screens/tenants/TenantSelectScreen';
import { RequireAuth, RequireTenant } from './session/guards';

/**
 * The interface's paths. /login is open; /organizations needs a session; the organization's screens live under /app/,
 * one subtree per organization (RequireTenant). Anything else is "not found". No interface path is an API path: Api
 * serves index.html only for a page no API route matches (WebInterface), so /subscriptions would be the API's list,
 * not this screen, on a reload (OPEN_ITEMS 37).
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
