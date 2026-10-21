import { useEffect } from 'react';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { BrowserRouter, MemoryRouter, useLocation } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { App } from './App';
import { I18nProvider } from './i18n/I18nProvider';
import { SessionProvider } from './session/SessionProvider';
import type { PreferenceStore } from './theme/preferenceStore';
import { ThemeProvider } from './theme/ThemeProvider';

/** Appearance kept in memory: the tests never touch the document's cookies for it. */
const noStore: PreferenceStore = { read: () => null, write: () => undefined };

const A = { tenant_id: 'tenant-a', name: 'Al-Amin' };
const B = { tenant_id: 'tenant-b', name: 'Maan' };

/** A fake API: the session's state, and the answers a test overrides. */
function fakeApi(options: { me?: typeof A | null; signedIn?: boolean; tenants?: (typeof A)[] } = {}) {
  const state = { signedIn: options.signedIn ?? true, active: options.me === undefined ? A : options.me };
  const tenants = (options.tenants ?? [A, B]).map((t) => ({ ...t, tenant_status: 'active', membership_status: 'active' }));
  const overrides = new Map<string, () => Response | Promise<Response>>();
  const calls: { method: string; path: string; headers: Record<string, string> }[] = [];
  const rows = (tenant: typeof A) =>
    Array.from({ length: 3 }, (_, i) => ({ id: `${tenant.tenant_id}-${i}`, client_id: 'c', service_name: `${tenant.name} row ${i}`, ends_on: '2027-01-02', status: 'active' }));

  const fetch = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const method = init?.method ?? 'GET';
    const path = String(input).replace(/^\/api/, '');
    calls.push({ method, path, headers: (init?.headers ?? {}) as Record<string, string> });
    const override = overrides.get(`${method} ${path.split('?')[0]}`);
    if (override) return override();
    if (method === 'POST' && path === '/tenants/deselect') {
      if (!state.signedIn) return new Response(null, { status: 401 });
      state.active = null;
      return new Response(null, { status: 204 });
    }
    if (method === 'POST' && path === '/auth/logout') {
      state.signedIn = false;
      return new Response(null, { status: 204 });
    }
    if (!state.signedIn) return new Response(null, { status: 401 });
    if (path === '/me') return Response.json({ username: 'omar', active_tenant: state.active });
    if (path === '/tenants') return Response.json({ tenants });
    // The caller's permissions, which the list screens load with their rows (OPEN_ITEMS 77): a member who may write.
    if (path === '/me/scope')
      return state.active
        ? Response.json({ scope_mode: 'all', can_manage_scope: false, assignments: [], permissions: ['subscriptions.read', 'subscriptions.write'] })
        : Response.json({ error: 'no_active_tenant' }, { status: 409 });
    const select = /^\/tenants\/(.+)\/select$/.exec(path);
    if (method === 'POST' && select) {
      state.active = [A, B].find((t) => t.tenant_id === select[1]) ?? null;
      return new Response(null, { status: 204 });
    }
    if (path.startsWith('/subscriptions/clients'))
      return Response.json({ scope_mode: 'all', visible_count: 1, total_count: 1, has_more_in_scope: false, items: [{ id: 'c', name: 'Client' }] });
    if (path.startsWith('/subscriptions')) {
      const items = state.active ? rows(state.active) : [];
      return Response.json({ scope_mode: 'all', visible_count: items.length, total_count: items.length, has_more_in_scope: false, items });
    }
    return new Response(null, { status: 404 });
  });
  vi.stubGlobal('fetch', fetch);
  return { state, overrides, calls };
}

// Written by an effect, so it can trail the commit that shows the new screen: after a navigation it is read inside
// waitFor, never straight after a findBy (OPEN_ITEMS 66). Where the screen must not move, a plain read is the claim.
const probe = { location: '' };
function LocationProbe() {
  const current = useLocation();
  useEffect(() => {
    probe.location = current.pathname + current.search;
  });
  return null;
}

function renderApp(path: string) {
  return render(
    <I18nProvider initial="en">
      <ThemeProvider store={noStore}>
        <MemoryRouter initialEntries={[path]}>
          <SessionProvider>
            <App />
            <LocationProbe />
          </SessionProvider>
        </MemoryRouter>
      </ThemeProvider>
    </I18nProvider>,
  );
}

const cells = () => screen.queryAllByRole('cell').map((c) => c.textContent);

afterEach(() => vi.unstubAllGlobals());

describe('switching the organization', () => {
  it('drops everything shown for the previous one, deselects it in the API, then shows the selection', async () => {
    const api = fakeApi();
    renderApp('/app/subscriptions');
    await screen.findByText('Al-Amin row 0');

    // From the switch on, any Al-Amin row added to the document is recorded — a stale cache would show one.
    const seen: string[] = [];
    const observer = new MutationObserver((records) => {
      for (const r of records)
        for (const node of r.addedNodes)
          if (node instanceof Element)
            for (const cell of [node, ...node.querySelectorAll('td')])
              if (cell.tagName === 'TD' && cell.textContent?.startsWith('Al-Amin')) seen.push(cell.textContent);
    });
    observer.observe(document.body, { childList: true, subtree: true });

    act(() => screen.getByRole('button', { name: 'Switch organization' }).click());

    // In the same render as the click: not one row of Al-Amin.
    expect(cells().some((c) => c?.startsWith('Al-Amin'))).toBe(false);
    expect(screen.queryByRole('table')).toBeNull();

    // The selection shows once the API has forgotten the tenant: deselect comes before the selection's first request.
    await screen.findByRole('heading', { name: 'Choose an organization' });
    await waitFor(() => expect(probe.location.startsWith('/organizations')).toBe(true));
    const deselect = api.calls.findIndex((c) => c.method === 'POST' && c.path === '/tenants/deselect');
    const listed = api.calls.findIndex((c, i) => i > 0 && c.path === '/tenants' && api.calls.slice(0, i).some((p) => p.path.startsWith('/subscriptions')));
    expect(deselect).toBeGreaterThan(-1);
    expect(deselect).toBeLessThan(listed);
    expect(api.calls[deselect]!.headers['X-Requested-With']).toBe('platform-web');

    fireEvent.click(await screen.findByRole('button', { name: /Maan/ }));
    await screen.findByText('Maan row 0');
    observer.disconnect();
    expect(cells().some((c) => c?.startsWith('Al-Amin'))).toBe(false);
    expect(seen).toEqual([]);
    expect(screen.getByTestId('identity-organization').textContent).toBe('Maan');
  });
});

describe('signing out', () => {
  it('asks the API through the client (CSRF header), clears everything, and shows the login screen', async () => {
    const api = fakeApi();
    renderApp('/app/subscriptions');
    await screen.findByText('Al-Amin row 0');

    fireEvent.click(screen.getByRole('button', { name: 'Sign out' }));

    await screen.findByRole('heading', { name: 'Sign in' });
    await waitFor(() => expect(probe.location).toBe('/login'));
    expect(screen.queryByRole('table')).toBeNull();
    expect(screen.queryByTestId('identity-user')).toBeNull();
    const logout = api.calls.find((c) => c.method === 'POST' && c.path === '/auth/logout');
    expect(logout?.headers['X-Requested-With']).toBe('platform-web');
  });

  it('afterwards, a protected page leads to the login screen', async () => {
    fakeApi({ signedIn: false });
    renderApp('/app/subscriptions');
    await screen.findByRole('heading', { name: 'Sign in' });
    await waitFor(() => expect(probe.location).toBe('/login?next=%2Fapp%2Fsubscriptions'));
  });
});

describe('a 401 during the session', () => {
  it('leads to the login screen with a message, and the way back kept', async () => {
    const api = fakeApi();
    renderApp('/app/subscriptions');
    await screen.findByText('Al-Amin row 0');

    api.state.signedIn = false; // Api restarted, or the cookie expired
    act(() => window.dispatchEvent(new Event('focus'))); // the user comes back to the tab

    await screen.findByText('Your session has ended. Sign in again.');
    await waitFor(() => expect(probe.location).toBe('/login?next=%2Fapp%2Fsubscriptions'));
    expect(screen.queryByRole('table')).toBeNull();
  });
});

describe('changing one\u2019s own password', () => {
  it('is reachable with no organization chosen, from the shell', async () => {
    fakeApi({ me: null });
    renderApp('/organizations');
    await screen.findByRole('heading', { name: 'Choose an organization' });

    fireEvent.click(screen.getByRole('link', { name: 'Change password' }));

    await screen.findByRole('heading', { name: 'Change your password' });
    await waitFor(() => expect(probe.location).toBe('/app/password/change'));
  });

  it('a reload after the password changed elsewhere still says why (the cold load reads the code)', async () => {
    // The cookie is HTTP-only: on a cold load a stale session is indistinguishable from none, except by the
    // code the API sends with the 401 (Amendment 01 §2.1).
    const api = fakeApi();
    api.overrides.set('GET /me', () => Response.json({ error: 'session_expired' }, { status: 401 }));
    renderApp('/app/subscriptions');

    await screen.findByText('Your session has ended. Sign in again.');
    await waitFor(() => expect(probe.location).toBe('/login?next=%2Fapp%2Fsubscriptions'));
  });

  it('a cold load with no session at all says nothing — it is not an ended session', async () => {
    fakeApi({ signedIn: false });
    renderApp('/app/subscriptions');

    await screen.findByRole('heading', { name: 'Sign in' });
    expect(screen.queryByText('Your session has ended. Sign in again.')).toBeNull();
  });

  it('a session_expired on it lands on the login screen, not on a blank page', async () => {
    const api = fakeApi({ me: null });
    api.overrides.set('PUT /me/password', () => Response.json({ error: 'session_expired' }, { status: 401 }));
    renderApp('/app/password/change');
    await screen.findByRole('heading', { name: 'Change your password' });

    fireEvent.change(screen.getByLabelText('Current password'), { target: { value: 'the current one' } });
    fireEvent.change(screen.getByLabelText('New password'), { target: { value: 'a new password' } });
    fireEvent.change(screen.getByLabelText('Repeat the new password'), { target: { value: 'a new password' } });
    fireEvent.click(screen.getByRole('button', { name: 'Change the password' }));

    await screen.findByText('Your session has ended. Sign in again.');
    await waitFor(() => expect(probe.location).toBe('/login?next=%2Fapp%2Fpassword%2Fchange'));
  });
});

describe('a membership lost during the session', () => {
  it('when /me no longer names the organization, everything is dropped and the selection shows', async () => {
    const api = fakeApi();
    renderApp('/app/subscriptions');
    await screen.findByText('Al-Amin row 0');

    api.state.active = null; // the membership was disabled
    act(() => window.dispatchEvent(new Event('focus'))); // the user comes back to the tab

    await waitFor(() => expect(probe.location).toBe('/organizations?next=%2Fapp%2Fsubscriptions'));
    expect(screen.queryByRole('table')).toBeNull();
    expect(screen.getByTestId('identity-organization').textContent).toBe('None chosen');
  });
});

describe('signing in', () => {
  it('one organization: entered straight away', async () => {
    const api = fakeApi({ signedIn: false, me: null, tenants: [A] });
    api.overrides.set('POST /auth/login', () => {
      api.state.signedIn = true;
      return new Response(null, { status: 204 });
    });
    renderApp('/login');
    fireEvent.change(await screen.findByLabelText('Username'), { target: { value: 'sara' } });
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'secret' } });
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));

    await screen.findByText('Al-Amin row 0');
    await waitFor(() => expect(probe.location).toBe('/app/subscriptions'));
  });

  // OPEN_ITEMS 94: a navigation the user makes while the single organization is being entered is theirs — the entry
  // finishing afterwards does not take them back to the list. The selection is held open here so the click lands
  // inside that window on every run, not one run in five.
  it('one organization: a navigation made while it is being entered is kept', async () => {
    const api = fakeApi({ signedIn: false, me: null, tenants: [A] });
    api.overrides.set('POST /auth/login', () => {
      api.state.signedIn = true;
      return new Response(null, { status: 204 });
    });
    let answer: () => void = () => undefined;
    api.overrides.set(
      'POST /tenants/tenant-a/select',
      () =>
        new Promise<Response>((resolve) => {
          answer = () => {
            api.state.active = A;
            resolve(new Response(null, { status: 204 }));
          };
        }),
    );
    renderApp('/login');
    fireEvent.change(await screen.findByLabelText('Username'), { target: { value: 'sara' } });
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'secret' } });
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    await waitFor(() => expect(api.calls.some((c) => c.method === 'POST' && c.path === '/tenants/tenant-a/select')).toBe(true));

    // The user leaves the selection before the entry answers…
    fireEvent.click(screen.getByRole('link', { name: 'Change password' }));
    await screen.findByRole('heading', { name: 'Change your password' });
    // …and then it does.
    await act(async () => answer());

    await waitFor(() => expect(api.calls.filter((c) => c.path === '/me').length).toBeGreaterThan(1));
    expect(probe.location).toBe('/app/password/change');
    expect(screen.getByRole('heading', { name: 'Change your password' })).toBeDefined();
  });

  // The same, on the router the app runs on, and in the window the browser has: BrowserRouter renders a location
  // change inside a transition (react-router 8), so the address changes at once while the selection screen is still
  // mounted. Inside one act() React holds its renders, so here the entry answers in exactly that window — the screen
  // still mounted, the address already the user's — and only the address can say the user has gone.
  it('one organization, on BrowserRouter: a navigation made while it is being entered is kept, mounted or not', async () => {
    window.history.replaceState(null, '', '/login');
    const api = fakeApi({ signedIn: false, me: null, tenants: [A] });
    api.overrides.set('POST /auth/login', () => {
      api.state.signedIn = true;
      return new Response(null, { status: 204 });
    });
    let answer: () => void = () => undefined;
    api.overrides.set(
      'POST /tenants/tenant-a/select',
      () =>
        new Promise<Response>((resolve) => {
          answer = () => {
            api.state.active = A;
            resolve(new Response(null, { status: 204 }));
          };
        }),
    );
    render(
      <I18nProvider initial="en">
        <ThemeProvider store={noStore}>
          <BrowserRouter>
            <SessionProvider>
              <App />
              <LocationProbe />
            </SessionProvider>
          </BrowserRouter>
        </ThemeProvider>
      </I18nProvider>,
    );
    fireEvent.change(await screen.findByLabelText('Username'), { target: { value: 'sara' } });
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'secret' } });
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    await waitFor(() => expect(api.calls.some((c) => c.method === 'POST' && c.path === '/tenants/tenant-a/select')).toBe(true));
    const meBefore = api.calls.filter((c) => c.path === '/me').length;

    await act(async () => {
      // The user's click: the address changes now; the new route renders only when this act ends.
      fireEvent.click(screen.getByRole('link', { name: 'Change password' }));
      expect(window.location.pathname).toBe('/app/password/change');
      // The entry answers, and finishes (its GET /me), while the selection screen is still mounted.
      answer();
      for (let i = 0; i < 50 && api.calls.filter((c) => c.path === '/me').length === meBefore; i++)
        await new Promise((r) => setTimeout(r, 5));
      await new Promise((r) => setTimeout(r, 20));
      expect(screen.getByRole('heading', { name: 'Choose an organization' })).toBeDefined();
    });

    await screen.findByRole('heading', { name: 'Change your password' });
    expect(window.location.pathname).toBe('/app/password/change');
    expect(probe.location).toBe('/app/password/change');
    window.history.replaceState(null, '', '/');
  });

  it('several organizations: the selection', async () => {
    const api = fakeApi({ signedIn: false, me: null });
    api.overrides.set('POST /auth/login', () => {
      api.state.signedIn = true;
      return new Response(null, { status: 204 });
    });
    renderApp('/login?next=%2Fapp%2Fsubscriptions');
    fireEvent.change(await screen.findByLabelText('Username'), { target: { value: 'omar' } });
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'secret' } });
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));

    await screen.findByRole('heading', { name: 'Choose an organization' });
    expect(screen.getAllByRole('button', { name: /Al-Amin|Maan/ })).toHaveLength(2);
    fireEvent.click(screen.getByRole('button', { name: /Maan/ }));
    await screen.findByText('Maan row 0');
    await waitFor(() => expect(probe.location).toBe('/app/subscriptions'));
  });

  it('a refusal: one generic message, focused, and the password cleared', async () => {
    const api = fakeApi({ signedIn: false, me: null });
    api.overrides.set('POST /auth/login', () => Response.json({ error: 'invalid_credentials' }, { status: 401 }));
    renderApp('/login');
    fireEvent.change(await screen.findByLabelText('Username'), { target: { value: 'nobody' } });
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'secret' } });
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));

    const alert = await screen.findByRole('alert');
    expect(alert.textContent).toBe('The username or password is incorrect.');
    await waitFor(() => expect(document.activeElement).toBe(alert));
    expect((screen.getByLabelText('Password') as HTMLInputElement).value).toBe('');
    expect(probe.location).toBe('/login');
  });

  it('a malformed request reads the same as a wrong password', async () => {
    const api = fakeApi({ signedIn: false, me: null });
    api.overrides.set('POST /auth/login', () => Response.json({ error: 'invalid_request' }, { status: 400 }));
    renderApp('/login');
    fireEvent.change(await screen.findByLabelText('Username'), { target: { value: 'x' } });
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'y' } });
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    expect((await screen.findByRole('alert')).textContent).toBe('The username or password is incorrect.');
  });

  it('a return path outside the interface is ignored', async () => {
    const api = fakeApi({ signedIn: false, me: null, tenants: [A] });
    api.overrides.set('POST /auth/login', () => {
      api.state.signedIn = true;
      return new Response(null, { status: 204 });
    });
    renderApp('/login?next=%2F%2Fevil.com');
    fireEvent.change(await screen.findByLabelText('Username'), { target: { value: 'sara' } });
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'secret' } });
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    await screen.findByText('Al-Amin row 0');
    await waitFor(() => expect(probe.location).toBe('/app/subscriptions'));
  });
});

describe('the selection refuses', () => {
  it.each([
    ['membership_scope_missing', 500, 'Your membership has no scope. Contact your administrator.'],
    ['step_up_required', 403, 'This organization requires a stronger sign-in method, which is not available yet.'],
    ['tenant_not_active', 403, 'This organization is not active.'],
    ['not_a_member', 403, 'You are not an active member of this organization.'],
  ])('%s: an explicit message, and the screen stays', async (code, status, text) => {
    const api = fakeApi({ me: null });
    api.overrides.set('POST /tenants/tenant-a/select', () => Response.json({ error: code }, { status }));
    renderApp('/organizations');
    fireEvent.click(await screen.findByRole('button', { name: /Al-Amin/ }));

    const alert = await screen.findByRole('alert');
    expect(alert.textContent).toBe(text);
    await waitFor(() => expect(document.activeElement).toBe(alert));
    expect(probe.location).toBe('/organizations');
    expect(screen.getByTestId('identity-organization').textContent).toBe('None chosen');
  });

  it('no active membership at all: a status screen with a way out', async () => {
    fakeApi({ me: null, tenants: [] });
    renderApp('/organizations');
    await screen.findByRole('heading', { name: 'No active membership' });
    expect(screen.getAllByRole('button', { name: 'Sign out' }).length).toBeGreaterThan(0);
  });
});

describe('the other states', () => {
  it('not_permitted: the message alone', async () => {
    const api = fakeApi();
    api.overrides.set('GET /subscriptions', () => Response.json({ error: 'not_permitted' }, { status: 403 }));
    renderApp('/app/subscriptions');
    await screen.findByRole('heading', { name: 'Not permitted' });
    expect(screen.getByRole('alert').textContent).toBe('You do not have permission to do this.');
  });

  it('an unknown path: not found', async () => {
    fakeApi();
    renderApp('/no/such/page');
    await screen.findByRole('heading', { name: 'Page not found' });
  });
});

describe('storage', () => {
  it('nothing is written to localStorage or sessionStorage', async () => {
    const api = fakeApi({ signedIn: false, me: null, tenants: [A] });
    api.overrides.set('POST /auth/login', () => {
      api.state.signedIn = true;
      return new Response(null, { status: 204 });
    });
    const setLocal = vi.spyOn(Storage.prototype, 'setItem');
    renderApp('/login');
    fireEvent.change(await screen.findByLabelText('Username'), { target: { value: 'sara' } });
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'secret' } });
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    await screen.findByText('Al-Amin row 0');
    expect(setLocal).not.toHaveBeenCalled();
    setLocal.mockRestore();
  });
});
