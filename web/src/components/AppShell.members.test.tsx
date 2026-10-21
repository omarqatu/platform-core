import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { App } from '../App';
import { I18nProvider } from '../i18n/I18nProvider';
import { SessionProvider } from '../session/SessionProvider';
import { ThemeProvider } from '../theme/ThemeProvider';
import type { PreferenceName, PreferenceStore } from '../theme/preferenceStore';

function memoryStore(): PreferenceStore {
  const values = new Map<PreferenceName, string>();
  return { read: (name) => values.get(name) ?? null, write: (name, value) => (value === null ? values.delete(name) : values.set(name, value)) };
}

const EMPTY = { scope_mode: 'all', visible_count: 0, total_count: 0, has_more_in_scope: false, items: [] };

/**
 * Signed in as sara in Al-Amin, holding `permissions`. `scope` is what GET /me/scope answers with — `null` makes
 * it fail, which is the case the sidebar must survive without adding a second error message to the screen.
 */
function api(permissions: string[] | null) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const path = String(input).replace(/^\/api/, '').split('?')[0]!;
      if (path === '/me') return Response.json({ username: 'sara', active_tenant: { tenant_id: 't', name: 'Al-Amin' } });
      if (path === '/me/scope')
        return permissions === null
          ? new Response(null, { status: 500 })
          : Response.json({ scope_mode: 'all', can_manage_scope: true, assignments: [], permissions });
      if (path.startsWith('/subscriptions')) return Response.json(EMPTY);
      if (path === '/members' || path === '/members/invitations') return Response.json(EMPTY);
      return new Response(null, { status: 404 });
    }),
  );
}

async function shell(permissions: string[] | null) {
  api(permissions);
  render(
    <I18nProvider initial="en">
      <ThemeProvider store={memoryStore()}>
        <MemoryRouter initialEntries={['/app/subscriptions']}>
          <SessionProvider>
            <App />
          </SessionProvider>
        </MemoryRouter>
      </ThemeProvider>
    </I18nProvider>,
  );
  await screen.findByRole('heading', { name: 'Subscriptions' });
}

const membersLink = () => screen.queryByRole('link', { name: 'Members' });

afterEach(() => {
  vi.unstubAllGlobals();
  document.cookie = 'lang=; Path=/; Max-Age=0';
});

/**
 * The sidebar's one conditional entry (OPEN_ITEMS 100). `GET /members` takes `core.members.manage` or
 * `core.scope.manage`, and the interface learns which from `GET /me/scope` alone (OPEN_ITEMS 77) — never from
 * the cookie, a header or anything the browser keeps.
 */
describe('the Members entry in the sidebar', () => {
  it('is offered to whoever manages members', async () => {
    await shell(['core.members.manage', 'subscriptions.read']);

    await waitFor(() => expect(membersLink()).toBeTruthy());
  });

  // The two permissions are orthogonal (§4.8): the platform's own seed has a member with scope management and no
  // member management, and they need the screen to assign clients to people.
  it('is offered to whoever manages scope alone', async () => {
    await shell(['core.scope.manage', 'subscriptions.read']);

    await waitFor(() => expect(membersLink()).toBeTruthy());
  });

  it('is not offered to whoever manages neither', async () => {
    await shell(['subscriptions.read', 'subscriptions.write']);

    // The other two entries are there, so this is the absence of one link and not of the whole sidebar.
    expect(screen.getByRole('link', { name: 'Clients' })).toBeTruthy();
    await waitFor(() => expect(membersLink()).toBeNull());
  });

  /**
   * The shell wraps every screen, including the ones reporting an error. A failed /me/scope is the screen's error
   * to tell — the sidebar must not turn it into a second message, and a missing link is the safe way to be wrong.
   */
  it('is not offered, and says nothing, when the permissions cannot be read', async () => {
    await shell(null);

    await waitFor(() => expect(membersLink()).toBeNull());
    expect(screen.getByRole('link', { name: 'Clients' })).toBeTruthy();
  });

  /**
   * A link shown and then taken away is worse than one that arrives a moment late: the entry starts absent and
   * only ever turns on, so it never flickers into view for someone who may not use it.
   */
  it('is absent before the permissions have answered, never shown first and withdrawn', async () => {
    let release: (() => void) | undefined;
    const held = new Promise<void>((resolve) => (release = resolve));
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) => {
        const path = String(input).replace(/^\/api/, '').split('?')[0]!;
        if (path === '/me') return Response.json({ username: 'sara', active_tenant: { tenant_id: 't', name: 'Al-Amin' } });
        if (path === '/me/scope') {
          await held;
          return Response.json({ scope_mode: 'all', can_manage_scope: true, assignments: [], permissions: ['core.members.manage'] });
        }
        if (path.startsWith('/subscriptions')) return Response.json(EMPTY);
        return new Response(null, { status: 404 });
      }),
    );
    render(
      <I18nProvider initial="en">
        <ThemeProvider store={memoryStore()}>
          <MemoryRouter initialEntries={['/app/subscriptions']}>
            <SessionProvider>
              <App />
            </SessionProvider>
          </MemoryRouter>
        </ThemeProvider>
      </I18nProvider>,
    );
    await screen.findByRole('link', { name: 'Clients' });

    expect(membersLink()).toBeNull();

    release!();
    await waitFor(() => expect(membersLink()).toBeTruthy());
  });
});
