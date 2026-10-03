import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { abortAll, ApiError, getJson, request, setUnauthorizedHandler } from '../api/client';

/** GET /me: the user, and the active tenant while the user can still enter it. */
export interface MeTenant {
  tenant_id: string;
  name: string;
}

interface MeResponse {
  username: string;
  active_tenant: MeTenant | null;
}

/** GET /tenants: the user's own memberships (app.user_id alone). */
export interface TenantChoice {
  tenant_id: string;
  name: string;
  tenant_status: string;
  membership_status: string;
}

export type SessionState =
  | { status: 'loading' }
  | { status: 'anonymous'; reason: 'none' | 'expired' | 'signedOut' }
  | { status: 'authenticated'; username: string; activeTenant: MeTenant | null };

interface SessionValue {
  state: SessionState;
  signIn: (username: string, password: string) => Promise<void>;
  selectTenant: (tenantId: string) => Promise<void>;
  /** Back to the tenant selection: everything shown for the current tenant is dropped first. */
  switchTenant: () => void;
  signOut: () => Promise<void>;
  /** Asks the API again; a tenant the user can no longer enter sends the interface back to the selection. */
  revalidate: () => Promise<void>;
}

const SessionContext = createContext<SessionValue | null>(null);

/** The memberships the selection offers, and counts: active memberships in active tenants. */
export function enterable(tenants: TenantChoice[]): TenantChoice[] {
  return tenants.filter((t) => t.tenant_status === 'active' && t.membership_status === 'active');
}

/**
 * The session, from the API alone: the cookie is HTTP-only and the interface keeps nothing in storage. All that the
 * interface shows for a tenant lives under that tenant's subtree (RequireTenant, keyed by the tenant): when the
 * tenant goes — a switch, a logout, an ended session, a membership lost — the state changes first, so the subtree is
 * gone in the same render, and every request in flight is aborted (abortAll).
 */
export function SessionProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<SessionState>({ status: 'loading' });
  const stateRef = useRef(state);
  useEffect(() => {
    stateRef.current = state;
  }, [state]);

  const load = useCallback(async (): Promise<SessionState> => {
    try {
      const me = await getJson<MeResponse>('/me');
      return { status: 'authenticated', username: me.username, activeTenant: me.active_tenant };
    } catch (error) {
      if (error instanceof ApiError && error.status === 401) return { status: 'anonymous', reason: 'none' };
      throw error;
    }
  }, []);

  // The session as the API sees it, once at start.
  useEffect(() => {
    let live = true;
    load().then(
      (next) => live && setState((current) => (current.status === 'loading' ? next : current)),
      () => live && setState((current) => (current.status === 'loading' ? { status: 'anonymous', reason: 'none' } : current)),
    );
    return () => {
      live = false;
    };
  }, [load]);

  // Any 401 but login's: the session has ended (expired, or Api restarted with new keys — OPEN_ITEMS 18).
  useEffect(() => {
    setUnauthorizedHandler(() => {
      abortAll();
      setState((current) =>
        current.status === 'anonymous' ? current : { status: 'anonymous', reason: current.status === 'authenticated' ? 'expired' : 'none' },
      );
    });
    return () => setUnauthorizedHandler(null);
  }, []);

  const signIn = useCallback(
    async (username: string, password: string) => {
      await request<void>('POST', '/auth/login', { username, password });
      abortAll();
      setState(await load());
    },
    [load],
  );

  const selectTenant = useCallback(
    async (tenantId: string) => {
      await request<void>('POST', `/tenants/${encodeURIComponent(tenantId)}/select`);
      const next = await load();
      if (next.status !== 'authenticated' || next.activeTenant?.tenant_id !== tenantId) throw new ApiError(0, null);
      abortAll();
      setState(next);
    },
    [load],
  );

  const switchTenant = useCallback(() => {
    abortAll();
    setState((current) => (current.status === 'authenticated' ? { ...current, activeTenant: null } : current));
  }, []);

  const signOut = useCallback(async () => {
    try {
      await request<void>('POST', '/auth/logout');
    } finally {
      abortAll();
      setState({ status: 'anonymous', reason: 'signedOut' });
    }
  }, []);

  const revalidate = useCallback(async () => {
    const current = stateRef.current;
    if (current.status !== 'authenticated') return;
    const next = await load();
    // An ended session was handled by the 401 handler already (reason 'expired'); nothing to add.
    if (next.status !== 'authenticated') return;
    const tenant = (s: SessionState) => (s.status === 'authenticated' ? s.activeTenant?.tenant_id : undefined);
    if (tenant(next) !== tenant(current)) {
      abortAll();
      setState({ ...next, activeTenant: null });
    }
  }, [load]);

  const value = useMemo(
    () => ({ state, signIn, selectTenant, switchTenant, signOut, revalidate }),
    [state, signIn, selectTenant, switchTenant, signOut, revalidate],
  );
  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>;
}

export function useSession(): SessionValue {
  const value = useContext(SessionContext);
  if (!value) throw new Error('useSession() is used outside SessionProvider.');
  return value;
}

/** The authenticated session; only under RequireAuth. */
export function useAuthenticated(): Extract<SessionState, { status: 'authenticated' }> {
  const { state } = useSession();
  if (state.status !== 'authenticated') throw new Error('useAuthenticated() is used outside RequireAuth.');
  return state;
}
