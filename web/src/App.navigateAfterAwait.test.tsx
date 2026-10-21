import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { BrowserRouter } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { App } from './App';
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
});
