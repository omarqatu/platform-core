import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { BrowserRouter } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { App } from './App';
import { capturePendingInvitation } from './invitations/pendingInvitation';
import { capturePendingResetToken } from './password/pendingResetToken';
import { I18nProvider } from './i18n/I18nProvider';
import { SessionProvider } from './session/SessionProvider';
import type { PreferenceStore } from './theme/preferenceStore';
import { ThemeProvider } from './theme/ThemeProvider';

// A navigation after an await, at every place the interface makes one (OPEN_ITEMS 96; DEVELOPER_GUIDE §15). Each
// test is the org-entry race of OPEN_ITEMS 94 at one place, on the router the app runs on: a request held open, the
// person goes somewhere else while it waits, then the answer arrives. BrowserRouter renders a location change inside
// a transition, so inside one act() React holds the render: the address is already the person's while the screen
// that made the request is still mounted — the window the browser has — and only the address can say they have gone.
//   * Guarded places (useStayed): the person stays where they went.
//   * Places that are not races: the navigation lands where the success leads, wherever the person went — pinned,
//     so a guard added there later is caught.

const noStore: PreferenceStore = { read: () => null, write: () => undefined };
const A = { tenant_id: 'tenant-a', name: 'Al-Amin' };

const subscription = {
  id: 's1', client_id: 'c1', service_name: 'VPS', ends_on: '2027-03-07', reminder_type: 'subscription', category_id: 'k1',
  subcategory_id: null, provider: null, cost: null, currency: null, billing_cycle: 'yearly', auto_renew: false,
  login_url: null, status: 'active', renewed_from_id: null,
};
const list = (items: unknown[]) => ({ scope_mode: 'all', visible_count: items.length, total_count: items.length, has_more_in_scope: false, items });

/** A fake API, signed in to A by default, with one answer a test holds open. */
function fakeApi(options: { signedIn?: boolean } = {}) {
  const state = { signedIn: options.signedIn ?? true, active: options.signedIn === false ? null : (A as typeof A | null) };
  const overrides = new Map<string, () => Response | Promise<Response>>();
  const calls: string[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const method = init?.method ?? 'GET';
      const path = String(input).replace(/^\/api/, '');
      const key = `${method} ${path.split('?')[0]}`;
      calls.push(key);
      const override = overrides.get(key);
      if (override) return override();
      if (key === 'POST /auth/logout') {
        state.signedIn = false;
        return new Response(null, { status: 204 });
      }
      if (!state.signedIn) return new Response(null, { status: 401 });
      if (key === 'POST /tenants/tenant-a/select') {
        state.active = A;
        return new Response(null, { status: 204 });
      }
      if (key === 'GET /me') return Response.json({ username: 'omar', active_tenant: state.active });
      if (key === 'GET /tenants') return Response.json({ tenants: [{ ...A, tenant_status: 'active', membership_status: 'active' }] });
      if (key === 'GET /me/scope')
        return Response.json({ scope_mode: 'all', can_manage_scope: false, assignments: [], permissions: ['subscriptions.read', 'subscriptions.write', 'subscriptions.clients.write'] });
      if (key === 'GET /subscriptions/categories')
        return Response.json({ categories: [{ id: 'k1', code: 'hosting', name_ar: 'استضافة', name_en: 'Hosting', subcategories: [] }] });
      if (key === 'GET /subscriptions/clients') return Response.json(list([{ id: 'c1', name: 'Nour Co', is_active: true }]));
      if (key === 'GET /subscriptions/s1') return Response.json(subscription);
      if (key === 'GET /subscriptions') return Response.json(list([{ ...subscription, category_id: 'k1' }]));
      return new Response(null, { status: 404 });
    }),
  );
  /** Holds `key` open: the request waits until `answer()`, which answers it with `response` (a 204 by default). */
  const hold = (key: string, response: () => Response = () => new Response(null, { status: 204 }), onAnswer?: () => void) => {
    let release: () => void = () => undefined;
    overrides.set(
      key,
      () =>
        new Promise<Response>((resolve) => {
          release = () => {
            onAnswer?.();
            resolve(response());
          };
        }),
    );
    return { answer: () => release(), asked: () => calls.includes(key) };
  };
  return { state, hold, calls };
}

function renderAt(path: string) {
  window.history.replaceState(null, '', path);
  render(
    <I18nProvider initial="en">
      <ThemeProvider store={noStore}>
        <BrowserRouter>
          <SessionProvider>
            <App />
          </SessionProvider>
        </BrowserRouter>
      </ThemeProvider>
    </I18nProvider>,
  );
}

/**
 * The race at one place: inside one act, the person moves (the address changes at once), then the held request
 * answers and its continuation runs while `stillThere` — the screen that made the request — is still mounted.
 */
async function moveWhileItWaits(move: () => void, to: string, answer: () => void, stillThere: () => unknown) {
  await act(async () => {
    move();
    expect(window.location.pathname).toBe(to);
    answer();
    for (let i = 0; i < 10; i++) await new Promise((r) => setTimeout(r, 5));
    expect(stillThere()).toBeTruthy();
  });
}

/** Where the person ends up, a moment after the act: read in waitFor, so a navigation still to come is waited for. */
async function landsOn(path: string) {
  await new Promise((r) => setTimeout(r, 50));
  await waitFor(() => expect(window.location.pathname + window.location.search).toBe(path));
}

afterEach(() => {
  vi.unstubAllGlobals();
  window.history.replaceState(null, '', '/');
});

describe('a navigation after an await: kept where the person went (guarded)', () => {
  // Site 4, the client form (ClientFormScreen.tsx): saved, and while it saves the person taps "Subscriptions" in the
  // sidebar. Without the guard the save took them back to the client list.
  it('saving a client: a sidebar link tapped while it saves is kept', async () => {
    const api = fakeApi();
    const save = api.hold('POST /subscriptions/clients', () => Response.json({ id: 'c2' }, { status: 201 }));
    renderAt('/app/clients/new');
    fireEvent.change(await screen.findByLabelText(/^Name/), { target: { value: 'Rawda' } });
    fireEvent.click(screen.getByRole('button', { name: 'Create' }));
    await waitFor(() => expect(save.asked()).toBe(true));

    await moveWhileItWaits(
      () => fireEvent.click(screen.getByRole('link', { name: 'Subscriptions' })),
      '/app/subscriptions',
      save.answer,
      () => screen.getByRole('heading', { name: 'New client' }),
    );

    await landsOn('/app/subscriptions');
    await screen.findByText('VPS');
  });

  // Site 5, the subscription form (SubscriptionFormScreen.tsx): an edit saved, and while it saves the person taps
  // "Clients". Without the guard the save took them back to the subscription list.
  it('saving a subscription: a sidebar link tapped while it saves is kept', async () => {
    const api = fakeApi();
    const save = api.hold('PUT /subscriptions/s1');
    renderAt('/app/subscriptions/s1/edit');
    await screen.findByDisplayValue('VPS');
    fireEvent.click(screen.getByRole('button', { name: 'Save changes' }));
    await waitFor(() => expect(save.asked()).toBe(true));

    await moveWhileItWaits(
      () => fireEvent.click(screen.getByRole('link', { name: 'Clients' })),
      '/app/clients',
      save.answer,
      () => screen.getByRole('heading', { name: 'Edit subscription' }),
    );

    await landsOn('/app/clients');
    await screen.findByText('Nour Co');
  });

  // Site 6, renewing (RenewSubscriptionScreen.tsx): the renewal sent, and while it waits the person taps "Clients".
  // Without the guard the renewal took them back to the subscription list.
  it('renewing a subscription: a sidebar link tapped while it renews is kept', async () => {
    const api = fakeApi();
    const renew = api.hold('POST /subscriptions/s1/renew', () => Response.json({ id: 's2' }, { status: 201 }));
    renderAt('/app/subscriptions/s1/renew');
    fireEvent.change(await screen.findByLabelText(/^New end date/), { target: { value: '2028-03-07' } });
    fireEvent.click(screen.getByRole('button', { name: 'Renew' }));
    await waitFor(() => expect(renew.asked()).toBe(true));

    await moveWhileItWaits(
      () => fireEvent.click(screen.getByRole('link', { name: 'Clients' })),
      '/app/clients',
      renew.answer,
      () => screen.getByRole('heading', { name: 'Renew subscription' }),
    );

    await landsOn('/app/clients');
    await screen.findByText('Nour Co');
  });

  // Site 3, signing in (LoginScreen.tsx), by Back: the person goes back while the sign-in waits — here to a page that
  // needs a session, so the app brings them to a new sign-in with that page as the way back. Without the guard the
  // first sign-in's navigation still ran, and the way back was the page they had left, not the one they went to.
  it('signing in: Back while it signs in is kept — the way back is the page they went back to', async () => {
    const api = fakeApi({ signedIn: false });
    const signIn = api.hold('POST /auth/login', undefined, () => (api.state.signedIn = true));
    window.history.replaceState(null, '', '/app/subscriptions');
    window.history.pushState(null, '', '/app/clients');
    renderAt('/app/clients');
    fireEvent.change(await screen.findByLabelText('Username'), { target: { value: 'omar' } });
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'secret' } });
    expect(window.location.pathname + window.location.search).toBe('/login?next=%2Fapp%2Fclients');
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    await waitFor(() => expect(signIn.asked()).toBe(true));

    await act(async () => {
      window.history.back(); // jsdom moves the history a moment later, as a browser does
      for (let i = 0; i < 40 && window.location.pathname !== '/app/subscriptions'; i++) await new Promise((r) => setTimeout(r, 5));
      expect(window.location.pathname).toBe('/app/subscriptions');
      signIn.answer();
      for (let i = 0; i < 10; i++) await new Promise((r) => setTimeout(r, 5));
    });

    await landsOn('/organizations?next=%2Fapp%2Fsubscriptions');
    await screen.findByRole('heading', { name: 'Choose an organization' });
  });
});


describe('a navigation after an await: where the success leads, wherever the person went (not a race — pinned)', () => {
  // Site 3, signing in, by "Forgot your password?": the person doubted their password while the sign-in waited, and
  // the sign-in succeeded. Left on the recovery page, a signed-in person would be asked to reset a password that
  // works; where signing in leads is the right end (LoginScreen's RECOVERY exception to useStayed).
  it('signing in: "Forgot your password?" tapped while it signs in still ends where signing in leads', async () => {
    const api = fakeApi({ signedIn: false });
    const signIn = api.hold('POST /auth/login', undefined, () => (api.state.signedIn = true));
    renderAt('/login');
    fireEvent.change(await screen.findByLabelText('Username'), { target: { value: 'omar' } });
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'secret' } });
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    await waitFor(() => expect(signIn.asked()).toBe(true));

    await moveWhileItWaits(
      () => fireEvent.click(screen.getByRole('link', { name: 'Forgot your password?' })),
      '/app/password/forgot',
      signIn.answer,
      () => screen.getByLabelText('Username'),
    );

    // One organization: the selection enters it by itself, and the list shows.
    await landsOn('/app/subscriptions');
    await screen.findByText('VPS');
  });

  // Site 2, signing out (AppShell.tsx): the person goes back while the sign-out waits — here to an open page. Their
  // last intention was to end the session; every page in the shell needs it, and the end of a sign-out is the sign-in
  // screen, whatever was tapped meanwhile.
  it('signing out: Back while it signs out still ends on the sign-in screen', async () => {
    const api = fakeApi();
    const signOut = api.hold('POST /auth/logout', undefined, () => (api.state.signedIn = false));
    window.history.replaceState(null, '', '/app/password/forgot');
    window.history.pushState(null, '', '/app/subscriptions');
    renderAt('/app/subscriptions');
    await screen.findByText('VPS');
    fireEvent.click(screen.getByRole('button', { name: 'Sign out' }));
    await waitFor(() => expect(signOut.asked()).toBe(true));

    // Back, all the way: the open page is on screen before the sign-out answers. (Inside the transition, the shell's
    // own guard, RequireAuth, would also send them to the sign-in screen as the session ends — so only a move that
    // has finished shows what the sign-out's own navigation does.)
    window.history.back();
    await screen.findByRole('heading', { name: 'Forgot your password' });
    await act(async () => {
      signOut.answer();
      for (let i = 0; i < 10; i++) await new Promise((r) => setTimeout(r, 5));
    });

    await landsOn('/login');
    await screen.findByRole('heading', { name: 'Sign in' });
  });

  // Site 7, accepting an invitation with a new account (AcceptInvitationScreen.tsx): the person taps "Sign in to
  // accept" while the account is being created. The account exists now and the invitation is used: what follows is
  // signing in to it — the success's own page, with what it knows (accepted: the selection enters nothing by
  // itself) — not the sign-in they tapped, whose way back is an invitation already cleared.
  it('accepting an invitation: "Sign in to accept" tapped while it accepts still ends on the success\'s sign-in', async () => {
    const api = fakeApi({ signedIn: false });
    const accept = api.hold('POST /invitations/accept');
    window.history.replaceState(null, '', '/app/invitations/accept#tenant=tenant-a&token=secret-token');
    capturePendingInvitation();
    renderAt(window.location.pathname);
    fireEvent.change(await screen.findByLabelText(/^Full name/), { target: { value: 'Rami Haddad' } });
    fireEvent.change(screen.getByLabelText(/^Email/), { target: { value: 'rami@example.com' } });
    fireEvent.change(screen.getByLabelText(/^Username/), { target: { value: 'rami' } });
    fireEvent.change(screen.getByLabelText(/^Password/), { target: { value: 'a-long-enough-password' } });
    fireEvent.click(screen.getByRole('button', { name: 'Create account and accept' }));
    await waitFor(() => expect(accept.asked()).toBe(true));

    await moveWhileItWaits(
      () => fireEvent.click(screen.getByRole('link', { name: 'Sign in to accept' })),
      '/login',
      accept.answer,
      () => screen.getByRole('heading', { name: 'Accept the invitation' }),
    );

    await landsOn('/login');
    await screen.findByRole('heading', { name: 'Sign in' });
  });

  // Site 8, setting a new password from a recovery link (ResetPasswordScreen.tsx): the person goes back while it is
  // set. The password is changed now; the one thing left is to sign in with it, on the screen that says it is set.
  it('resetting the password: Back while it is set still ends on the sign-in screen', async () => {
    const api = fakeApi({ signedIn: false });
    const reset = api.hold('POST /auth/password/reset');
    window.history.replaceState(null, '', '/app/password/forgot');
    window.history.pushState(null, '', '/app/password/reset#token=recovery-token');
    capturePendingResetToken();
    renderAt(window.location.pathname);
    fireEvent.change(await screen.findByLabelText(/^New password/), { target: { value: 'a-long-enough-password' } });
    fireEvent.change(screen.getByLabelText(/^Repeat the new password/), { target: { value: 'a-long-enough-password' } });
    fireEvent.click(screen.getByRole('button', { name: 'Set the password' }));
    await waitFor(() => expect(reset.asked()).toBe(true));

    await act(async () => {
      window.history.back();
      for (let i = 0; i < 40 && window.location.pathname !== '/app/password/forgot'; i++) await new Promise((r) => setTimeout(r, 5));
      expect(window.location.pathname).toBe('/app/password/forgot');
      reset.answer();
      for (let i = 0; i < 10; i++) await new Promise((r) => setTimeout(r, 5));
    });

    await landsOn('/login');
    await screen.findByRole('heading', { name: 'Sign in' });
  });
});

