import { useEffect } from 'react';
import { Navigate, Outlet, useLocation } from 'react-router';
import { IdentityBar } from '../components/IdentityBar';
import { LoadingScreen } from '../screens/status/StatusScreens';
import { nextQuery } from './next';
import { useSession } from './SessionProvider';

function here(location: { pathname: string; search: string; hash: string }) {
  return location.pathname + location.search + location.hash;
}

/** Signed in, or to the login screen with the way back (an ended session included: a 401 lands here). */
export function RequireAuth() {
  const { state } = useSession();
  const location = useLocation();
  if (state.status === 'loading') return <LoadingScreen />;
  // Signed out on purpose: the login screen, with no way back to the previous user's page.
  if (state.status === 'anonymous')
    return <Navigate to={state.reason === 'signedOut' ? '/login' : `/login${nextQuery(here(location))}`} replace />;
  return (
    <>
      <IdentityBar />
      <Outlet />
    </>
  );
}

/**
 * Inside a tenant, or to the selection. The subtree is keyed by the tenant: another tenant is another tree, never the
 * previous one's state. /me is asked again on each navigation and each time the window comes back (focus, or the tab
 * visible again) — and a data request refused for the tenant does the same (SubscriptionsScreen) — so a membership
 * lost during the session (disabled, or the tenant suspended) sends the interface back to the selection with nothing
 * of that tenant left on screen.
 */
export function RequireTenant() {
  const { state } = useSession();
  const location = useLocation();
  if (state.status !== 'authenticated') return null;
  if (!state.activeTenant) return <Navigate to={`/organizations${nextQuery(here(location))}`} replace />;
  return <TenantScope key={state.activeTenant.tenant_id} />;
}

function TenantScope() {
  const { revalidate } = useSession();
  const location = useLocation();
  useEffect(() => {
    revalidate().catch(() => undefined);
  }, [location.key, revalidate]);
  useEffect(() => {
    const check = () => {
      if (document.visibilityState === 'visible') revalidate().catch(() => undefined);
    };
    window.addEventListener('focus', check);
    document.addEventListener('visibilitychange', check);
    return () => {
      window.removeEventListener('focus', check);
      document.removeEventListener('visibilitychange', check);
    };
  }, [revalidate]);
  return <Outlet />;
}
