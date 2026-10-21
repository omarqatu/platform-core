import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { I18nProvider } from '../../i18n/I18nProvider';
import type { Lang } from '../../i18n/locale';
import type { ScopedList } from '../subscriptions/SubscriptionsScreen';
import { SessionProvider } from '../../session/SessionProvider';
import { ClientsScreen, ClientsView, type ListActions } from './ClientsScreen';
import type { ClientItem } from './clientForm';

const ARABIC_INDIC_DIGITS = /[٠-٩۰-۹]/;

function list(items: ClientItem[], scope: 'all' | 'assigned', visible = items.length, more = false): ScopedList<ClientItem> {
  return { scope_mode: scope, visible_count: visible, total_count: scope === 'all' ? visible : null, has_more_in_scope: more, items };
}

const two: ClientItem[] = [
  { id: 'c1', name: 'Client A', is_active: true },
  { id: 'c2', name: 'شركة النور', is_active: false },
];

function show(lang: Lang, clients: ScopedList<ClientItem>, actions?: ListActions) {
  return render(
    <I18nProvider initial={lang}>
      <MemoryRouter>
        <ClientsView state={{ status: 'ready', data: clients }} actions={actions} />
      </MemoryRouter>
    </I18nProvider>,
  ).container;
}

// Owner or admin: both permissions (subscriptions.clients.write and .manage).
const actions = (setStatus: ListActions['setStatus']): ListActions => ({
  create: () => null,
  edit: true,
  setStatus,
});

const menuOf = (client: string) => {
  fireEvent.click(screen.getByRole('button', { name: `Actions for ${client}` }));
  return screen.getAllByRole('menuitem').map((i) => i.getAttribute('aria-label'));
};

afterEach(() => {
  vi.unstubAllGlobals();
  document.cookie = 'lang=; Path=/; Max-Age=0';
});

describe('the client list', () => {
  it('declares an assigned scope with no total figure anywhere', () => {
    const page = show('en', list(two, 'assigned'));
    const scope = page.querySelector('.scope')!;

    expect(scope.getAttribute('data-scope-mode')).toBe('assigned');
    expect(scope.textContent).toBe('You are shown only the clients assigned to you: 2 clients.');
    expect(page.innerHTML).not.toContain('data-total');
  });

  it('declares the whole organization with its count', () => {
    const scope = show('en', list(two, 'all')).querySelector('.scope')!;

    expect(scope.getAttribute('data-scope-mode')).toBe('all');
    expect(scope.getAttribute('data-total')).toBe('2');
    expect(scope.textContent).toBe("You are shown all of the organization's clients: 2 clients.");
  });

  it('says so when nothing is assigned', () => {
    const scope = show('en', list([], 'assigned')).querySelector('.scope')!;

    expect(scope.textContent).toBe('You are shown only the clients assigned to you, and none are assigned to you now.');
  });

  // Disabled, not deleted: the row stays, with a badge that says which it is.
  it('lists a disabled client with its badge', () => {
    const page = show('en', list(two, 'all'));
    const statuses = [...page.querySelectorAll('[data-status]')].map((s) => [s.getAttribute('data-status'), s.textContent]);

    expect(page.querySelectorAll('tbody tr')).toHaveLength(2);
    expect(statuses).toEqual([
      ['active', 'Active'],
      ['disabled', 'Disabled'],
    ]);
  });

  it('shows each client in its own direction', () => {
    const cells = [...show('en', list(two, 'all')).querySelectorAll('tbody tr td:first-child bdi')];

    expect(cells.map((c) => c.textContent)).toEqual(['Client A', 'شركة النور']);
    expect(cells.every((c) => c.getAttribute('dir') === 'auto')).toBe(true);
  });
});

describe('disabling and enabling', () => {
  it('asks before disabling, and says what it does not change', () => {
    const confirm = vi.fn(() => false);
    vi.stubGlobal('confirm', confirm);
    const setStatus = vi.fn();
    show('en', list([two[0]], 'all'), actions(setStatus));

    menuOf('Client A');
    fireEvent.click(screen.getByRole('menuitem', { name: 'Disable Client A' }));

    expect(confirm).toHaveBeenCalledWith(
      'Stop the reminders for Client A? Its subscriptions are not changed, and you can enable it again.',
    );
    expect(setStatus).not.toHaveBeenCalled();
  });

  it('disables once the question is answered', () => {
    vi.stubGlobal('confirm', vi.fn(() => true));
    const setStatus = vi.fn();
    show('en', list([two[0]], 'all'), actions(setStatus));

    menuOf('Client A');
    fireEvent.click(screen.getByRole('menuitem', { name: 'Disable Client A' }));

    expect(setStatus).toHaveBeenCalledWith(two[0], false);
  });

  // Enabling resumes the reminders and catches nothing up, so there is nothing to warn about.
  it('enables without asking', () => {
    const confirm = vi.fn(() => true);
    vi.stubGlobal('confirm', confirm);
    const setStatus = vi.fn();
    show('en', list([two[1]], 'all'), actions(setStatus));

    menuOf('شركة النور');
    fireEvent.click(screen.getByRole('menuitem', { name: 'Enable شركة النور' }));

    expect(confirm).not.toHaveBeenCalled();
    expect(setStatus).toHaveBeenCalledWith(two[1], true);
  });
});

// Each way to act behind its own permission (Amendment 01 §4.2): editing with subscriptions.clients.write,
// disabling and enabling with subscriptions.clients.manage.
describe('the menu, by permission', () => {
  it('both permissions: edit, then disable — last, apart, as the destructive one', () => {
    show('en', list([two[0]], 'all'), actions(vi.fn()));

    expect(menuOf('Client A')).toEqual(['Edit Client A', 'Disable Client A']);
    expect(screen.getByRole('menuitem', { name: 'Edit Client A' }).getAttribute('href')).toBe('/app/clients/c1/edit');
    expect(screen.getByRole('menuitem', { name: 'Disable Client A' }).classList.contains('danger')).toBe(true);
    expect(screen.getByRole('separator')).toBeDefined();
  });

  it('a disabled client: edit, then enable — not as a destructive one', () => {
    show('en', list([two[1]], 'all'), actions(vi.fn()));

    expect(menuOf('شركة النور')).toEqual(['Edit شركة النور', 'Enable شركة النور']);
    expect(screen.getByRole('menuitem', { name: 'Enable شركة النور' }).classList.contains('danger')).toBe(false);
  });

  // The operator: subscriptions.clients.write without .manage.
  it('an operator: edit, and no disable or enable', () => {
    show('en', list(two, 'all'), { create: () => null, edit: true });

    expect(menuOf('Client A')).toEqual(['Edit Client A']);
    fireEvent.keyDown(screen.getByRole('menu'), { key: 'Escape' });
    expect(menuOf('شركة النور')).toEqual(['Edit شركة النور']);
  });

  // A custom role with .manage and not .write: the way back without the form.
  it('manage alone: disable or enable, and no edit', () => {
    show('en', list([two[0]], 'all'), { edit: false, setStatus: vi.fn() });

    expect(menuOf('Client A')).toEqual(['Disable Client A']);
    expect(screen.queryByRole('link', { name: 'New client' })).toBeNull();
  });

  it('names the button in Arabic', () => {
    show('ar', list([two[0]], 'all'), actions(vi.fn()));

    expect(screen.getByRole('button', { name: 'إجراءات Client A' })).toBeDefined();
  });
});

// The routed screen: the caller's permissions come from GET /me/scope, in the same load as the list (OPEN_ITEMS 77).
describe('the actions follow the permission (GET /me/scope)', () => {
  const rows = { scope_mode: 'all', visible_count: 1, total_count: 1, has_more_in_scope: false,
    items: [{ id: 'c1', name: 'Client A', is_active: true }] };

  function api(meScope: () => Response | Promise<Response>) {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) => {
        const path = String(input).replace(/^\/api/, '').split('?')[0]!;
        if (path === '/me') return Response.json({ username: 'khaled', active_tenant: { tenant_id: 't', name: 'Al-Amin' } });
        if (path === '/me/scope') return meScope();
        if (path === '/subscriptions/clients') return Response.json(rows);
        return new Response(null, { status: 404 });
      }),
    );
  }

  const permitted = (...permissions: string[]) => () =>
    Response.json({ scope_mode: 'all', can_manage_scope: false, assignments: [], permissions });

  function renderScreen() {
    render(
      <I18nProvider initial="en">
        <MemoryRouter>
          <SessionProvider>
            <ClientsScreen />
          </SessionProvider>
        </MemoryRouter>
      </I18nProvider>,
    );
  }

  it('a viewer: no "New client", no actions column, no menu', async () => {
    api(permitted('subscriptions.read'));
    renderScreen();

    await screen.findByText('Client A');
    expect(screen.queryByRole('link', { name: 'New client' })).toBeNull();
    expect(screen.queryByRole('columnheader', { name: 'Actions' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Actions for Client A' })).toBeNull();
  });

  it('an operator: "New client", and edit without disable', async () => {
    api(permitted('subscriptions.read', 'subscriptions.write', 'subscriptions.clients.write'));
    renderScreen();

    await screen.findByText('Client A');
    expect(screen.getByRole('link', { name: 'New client' })).toBeDefined();
    expect(menuOf('Client A')).toEqual(['Edit Client A']);
  });

  it('an owner: edit and disable', async () => {
    api(permitted('subscriptions.clients.manage', 'subscriptions.clients.write', 'subscriptions.read'));
    renderScreen();

    await screen.findByText('Client A');
    expect(menuOf('Client A')).toEqual(['Edit Client A', 'Disable Client A']);
  });

  it('shows no row and no action while the permissions are still loading', async () => {
    let answer: (r: Response) => void = () => undefined;
    api(() => new Promise<Response>((resolve) => (answer = resolve)));
    renderScreen();

    await screen.findByText('Loading…');
    expect(screen.queryByText('Client A')).toBeNull();
    expect(screen.queryByRole('link', { name: 'New client' })).toBeNull();
    answer(permitted('subscriptions.clients.write')());
    await screen.findByText('Client A');
  });

  it('shows the error path when /me/scope fails, never the list without its actions', async () => {
    api(() => Response.json({ error: 'internal_error' }, { status: 500 }));
    renderScreen();

    await screen.findByRole('alert');
    expect(screen.queryByText('Client A')).toBeNull();
    expect(screen.queryByRole('table')).toBeNull();
  });
});

describe('in Arabic', () => {
  it('lays out right-to-left, in glossary terms and Western digits', () => {
    const page = show('ar', list(two, 'all'));

    expect(document.documentElement.dir).toBe('rtl');
    const scope = page.querySelector('.scope')!.textContent!;
    expect(scope).toBe('يُعرض لك كل عملاء الجهة: عميلان.');
    expect(scope).not.toContain('مستأجر');
    cleanup();
    expect(show('ar', list(two, 'all', 11)).querySelector('.scope')!.textContent).not.toMatch(ARABIC_INDIC_DIGITS);
  });
});
