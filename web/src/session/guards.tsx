import { useEffect } from 'react';
import { Navigate, Outlet, useLocation, useNavigate } from 'react-router';
import { AppShell } from '../components/AppShell';
import { useStaleRender } from '../navigation/useStaleRender';
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
    <AppShell>
      <Outlet />
    </AppShell>
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
  const isStale = useStaleRender(here(location));
  if (state.status !== 'authenticated') return null;
  // Switching: the tenant's screens are already gone; the selection shows once the API has forgotten the tenant.
  if (!state.activeTenant && state.leaving) return <LoadingScreen />;
  // No tenant: to the selection — unless this is the tree the person has already left (navigation/useStaleRender):
  // the switch ending ("leaving" over) can render it again inside the router's transition, and the redirect would
  // land on top of the page they went to.
  if (!state.activeTenant) return <Redirect to={`/organizations${nextQuery(here(location))}`} isStale={isStale} />;
  return <TenantScope key={state.activeTenant.tenant_id} />;
}

/** A guard's redirect (replace), made in its effect unless the tree it is rendered in has been left. */
function Redirect({ to, isStale }: { to: string; isStale: () => boolean }) {
  const navigate = useNavigate();
  // Relied on: React runs a child's effects before its parent's in the same commit, so isStale() here still reads
  // the snapshot RequireTenant's useStaleRender recorded at the previous commit — the one this commit is compared
  // with. If that order ever changed, the parent would record this commit first, no commit would look stale, and
  // the redirect would again land on top of where the person went (useStaleRender.test and site 1's test fail).
  useEffect(() => {
    if (!isStale()) navigate(to, { replace: true });
  });
  return null;
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
