import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { I18nProvider } from '../../i18n/I18nProvider';
import type { Lang } from '../../i18n/locale';
import { SessionProvider } from '../../session/SessionProvider';
import {
  DUE_SOON_DAYS,
  localDay,
  subscriptionStatus,
  SubscriptionsScreen,
  SubscriptionsView,
  type ClientItem,
  type ListActions,
  type ScopedList,
  type SubscriptionItem,
} from './SubscriptionsScreen';

const ARABIC_INDIC_DIGITS = /[٠-٩۰-۹]/;

function list<T>(items: T[], scope: 'all' | 'assigned', visible = items.length, more = false): ScopedList<T> {
  return { scope_mode: scope, visible_count: visible, total_count: scope === 'all' ? visible : null, has_more_in_scope: more, items };
}

const clients: ClientItem[] = [
  { id: 'c1', name: 'Client A', is_active: true },
  { id: 'c2', name: 'شركة النور', is_active: true },
];
const subscriptions = (n: number): SubscriptionItem[] =>
  Array.from({ length: n }, (_, i) => ({
    id: `s${i}`,
    client_id: i % 2 ? 'c2' : 'c1',
    service_name: i === 0 ? 'استضافة' : `A service ${i}`,
    ends_on: '2027-01-02',
    status: 'active' as const,
  }));

function show(lang: Lang, subs: ScopedList<SubscriptionItem>, cs = list(clients, subs.scope_mode)) {
  return render(
    <I18nProvider initial={lang}>
      <SubscriptionsView state={{ status: 'ready', data: { subscriptions: subs, clients: cs } }} />
    </I18nProvider>,
  ).container;
}

afterEach(() => (document.cookie = 'lang=; Path=/; Max-Age=0'));

describe('the T8 screen, right-to-left', () => {
  it('declares an assigned scope in Arabic, with no total figure anywhere', () => {
    const page = show('ar', list(subscriptions(10), 'assigned'));
    const scope = page.querySelector('.scope')!;
    expect(document.documentElement.dir).toBe('rtl');
    expect(scope.getAttribute('data-scope-mode')).toBe('assigned');
    expect(scope.textContent).toBe('تُعرض لك اشتراكات العملاء المُسندين إليك فقط: عميلان، 10 اشتراكات.');
    expect(page.innerHTML).not.toContain('data-total');
    expect(page.querySelectorAll('tbody tr')).toHaveLength(10);
  });

  it('agrees the counted noun in every Arabic plural form', () => {
    const declaration = (clientsVisible: number, n: number) => {
      const page = show('ar', list(subscriptions(Math.min(n, 3)), 'assigned', n), list(clients, 'assigned', clientsVisible));
      const text = page.querySelector('.scope')!.textContent;
      cleanup();
      return text;
    };
    expect(declaration(1, 1)).toContain('عميل واحد، اشتراك واحد.');
    expect(declaration(2, 2)).toContain('عميلان، اشتراكان.');
    expect(declaration(3, 11)).toContain('3 عملاء، 11 اشتراكاً.');
    expect(declaration(3, 100)).toContain('3 عملاء، 100 اشتراك.');
  });

  it('declares an all scope with the total, in Latin digits', () => {
    const page = show('ar', list(subscriptions(20), 'all'));
    const scope = page.querySelector('.scope')!;
    expect(scope.getAttribute('data-total')).toBe('20');
    expect(scope.textContent).toBe('تُعرض لك كل اشتراكات الجهة: 20 اشتراكاً.');
    expect(page.textContent).not.toMatch(ARABIC_INDIC_DIGITS);
  });

  it('shows what users entered as is, in its own direction, and dates through Intl in Arabic with Latin digits', () => {
    const page = show('ar', list(subscriptions(2), 'assigned'));
    const [service, client, date] = page.querySelectorAll('tbody tr')[0]!.querySelectorAll('td');
    expect(service!.querySelector('bdi')!.getAttribute('dir')).toBe('auto');
    expect(service!.textContent).toBe('استضافة');
    expect(client!.querySelector('bdi')!.getAttribute('dir')).toBe('auto');
    expect(client!.textContent).toBe('Client A');
    const time = date!.querySelector('time')!;
    expect(time.getAttribute('datetime')).toBe('2027-01-02');
    expect(time.textContent).toBe(
      new Intl.DateTimeFormat('ar-u-nu-latn', { year: 'numeric', month: 'long', day: 'numeric', timeZone: 'UTC' }).format(
        new Date('2027-01-02T00:00:00Z'),
      ),
    );
    expect(time.textContent).toContain('2027');
    expect(time.textContent).not.toMatch(ARABIC_INDIC_DIGITS);
  });
});

describe('the T8 screen, left-to-right', () => {
  it('declares the same scope in English', () => {
    const page = show('en', list(subscriptions(10), 'assigned'));
    expect(document.documentElement.dir).toBe('ltr');
    expect(document.documentElement.lang).toBe('en');
    expect(page.querySelector('h1')!.textContent).toBe('Subscriptions');
    expect(page.querySelector('.scope')!.textContent).toBe(
      'You are shown only the subscriptions of the clients assigned to you: 2 clients, 10 subscriptions.',
    );
    expect(page.querySelector('time')!.textContent).toBe('January 2, 2027');
  });

  it('keeps user data untranslated', () => {
    const page = show('en', list(subscriptions(2), 'assigned'));
    expect(page.querySelector('tbody td')!.textContent).toBe('استضافة');
  });

  it('says when more is in scope', () => {
    const page = show('en', list(subscriptions(3), 'assigned', 60, true));
    expect(page.querySelector('.more')!.textContent).toBe('Showing the first 3 subscriptions; there are more within your scope.');
  });
});

describe('an API refusal', () => {
  it('is shown by its code, translated, or as unknown', () => {
    const shown = (lang: Lang, code: string | null) => {
      const view = render(
        <I18nProvider initial={lang}>
          <SubscriptionsView state={{ status: 'error', code }} />
        </I18nProvider>,
      );
      const text = view.getByRole('alert').textContent;
      view.unmount();
      return text;
    };
    expect(shown('ar', 'no_active_tenant')).toBe('اختر الجهة أولًا.');
    expect(shown('en', 'not_permitted')).toBe('You do not have permission to do this.');
    expect(shown('en', 'brand_new_code')).toBe('Something went wrong. Try again.');
  });
});

describe('the status badge', () => {
  it('is expired before today, due soon from today through DUE_SOON_DAYS, and active after', () => {
    const today = '2027-01-31';
    expect(subscriptionStatus('2027-01-30', today)).toBe('expired');
    expect(subscriptionStatus('2027-01-31', today)).toBe('dueSoon');
    expect(subscriptionStatus('2027-03-02', today)).toBe('dueSoon'); // 30 days, across February
    expect(subscriptionStatus('2027-03-03', today)).toBe('active');
    expect(DUE_SOON_DAYS).toBe(30);
  });

  it("takes today from the user's clock, not UTC's", () => {
    expect(localDay(new Date(2027, 0, 2, 23, 59))).toBe('2027-01-02');
    expect(localDay(new Date(2027, 11, 31, 0, 0))).toBe('2027-12-31');
  });

  it('pairs an icon with the word, in each language', () => {
    const en = show('en', list(subscriptions(1), 'all'));
    const badge = en.querySelector('tbody .badge')!;
    expect(badge.querySelector('svg[aria-hidden="true"]')).not.toBeNull();
    expect(['Active', 'Due soon', 'Expired']).toContain(badge.textContent);
    cleanup();
    const ar = show('ar', list(subscriptions(1), 'all'));
    expect(['نشط', 'قريب الانتهاء', 'منتهٍ']).toContain(ar.querySelector('tbody .badge')!.textContent);
  });
});

describe('the lifecycle on the list (Amendment 01 §2.9)', () => {
  const lifecycle = (status: SubscriptionItem['status'], ends_on = '2099-01-02'): SubscriptionItem[] => [
    { id: 's1', client_id: 'c1', service_name: 'VPS', ends_on, status },
  ];

  const actions: ListActions = { create: () => null, cancel: () => undefined };

  // With actions the rows link to the edit and renew screens, so the view needs a router.
  function withActions(items: SubscriptionItem[], lang: Lang = 'en', listActions: ListActions = actions) {
    return render(
      <I18nProvider initial={lang}>
        <MemoryRouter>
          <SubscriptionsView
            state={{ status: 'ready', data: { subscriptions: list(items, 'all'), clients: list(clients, 'all') } }}
            actions={listActions}
          />
        </MemoryRouter>
      </I18nProvider>,
    ).container;
  }

  const menuOf = (service: string) => {
    fireEvent.click(screen.getByRole('button', { name: `Actions for ${service}` }));
    return screen.getAllByRole('menuitem').map((i) => i.getAttribute('aria-label'));
  };

  // The lifecycle wins over the date: once a row has left 'active' its remaining days say nothing worth saying.
  it.each([
    ['cancelled' as const, 'Cancelled'],
    ['renewed' as const, 'Renewed'],
  ])('shows %s in place of the date-derived status', (status, label) => {
    const badge = show('en', list(lifecycle(status), 'all')).querySelector('tbody .badge')!;

    expect(badge.querySelector('[data-status]')!.getAttribute('data-status')).toBe(status);
    expect(badge.textContent).toBe(label);
  });

  it('keeps the date-derived status while the row is active', () => {
    const badge = show('en', list(lifecycle('active'), 'all')).querySelector('tbody .badge')!;

    expect(['Active', 'Due soon', 'Expired']).toContain(badge.textContent);
  });

  // The project owner's state table: an active row — edit, renew, cancel.
  it('offers edit, renew and cancel on an active row, behind one button, cancel last', () => {
    withActions(lifecycle('active'));

    expect(menuOf('VPS')).toEqual(['Edit VPS', 'Renew VPS', 'Cancel VPS']);
    expect(screen.getByRole('menuitem', { name: 'Edit VPS' }).getAttribute('href')).toBe('/app/subscriptions/s1/edit');
    expect(screen.getByRole('menuitem', { name: 'Renew VPS' }).getAttribute('href')).toBe('/app/subscriptions/s1/renew');
    expect(document.querySelector('[data-note="expired"]')).toBeNull();
  });

  // Active and past its end date: renewing is the point, so it comes first, and the row says why to open the menu.
  // "Expired" is the end date's, not the status's — the row stays 'active' after it ends.
  it('puts renew first on an active row past its end date, and says since when', () => {
    withActions(lifecycle('active', '2020-01-02'));

    expect(menuOf('VPS')).toEqual(['Renew VPS', 'Edit VPS', 'Cancel VPS']);
    const note = document.querySelector('[data-note="expired"]')!;
    expect(note.textContent).toBe('Expired since 2020-01-02');
    expect(note.querySelector('bdi')!.getAttribute('dir')).toBe('ltr');
  });

  it('says the same in Arabic, the date left-to-right in Western digits', () => {
    withActions(lifecycle('active', '2020-01-02'), 'ar');

    const note = document.querySelector('[data-note="expired"]')!;
    expect(note.textContent).toBe('منتهٍ منذ 2020-01-02');
    expect(note.querySelector('bdi')!.textContent).toBe('2020-01-02');
    expect(ARABIC_INDIC_DIGITS.test(note.textContent!)).toBe(false);
  });

  // A renewed or cancelled row: the database's own gate refuses any write, so there are no actions — and the cell
  // says so, because an empty one cannot be told from a fault.
  it.each([
    ['renewed' as const, 'Renewed, no actions'],
    ['cancelled' as const, 'Cancelled, no actions'],
  ])('offers nothing on a %s row, and says so', (status, note) => {
    const page = withActions(lifecycle(status));

    expect(screen.queryByRole('button', { name: 'Actions for VPS' })).toBeNull();
    expect(page.querySelector('td.actions')!.textContent).toBe(note);
  });

  it.each([
    ['renewed' as const, 'مجدَّد، لا إجراءات'],
    ['cancelled' as const, 'ملغى، لا إجراءات'],
  ])('says so in Arabic on a %s row', (status, note) => {
    const page = withActions(lifecycle(status), 'ar');

    expect(page.querySelector('td.actions')!.textContent).toBe(note);
  });

  it('asks before cancelling, and says the row is kept', () => {
    const confirm = vi.fn(() => false);
    vi.stubGlobal('confirm', confirm);
    const cancel = vi.fn();
    withActions(lifecycle('active'), 'en', { ...actions, cancel });

    menuOf('VPS');
    fireEvent.click(screen.getByRole('menuitem', { name: 'Cancel VPS' }));

    expect(confirm).toHaveBeenCalledWith(
      'Cancel VPS? It stays in the list with its history, and it cannot be edited or renewed afterwards.',
    );
    expect(cancel).not.toHaveBeenCalled();
    vi.unstubAllGlobals();
  });

  it('cancels once the question is answered', () => {
    vi.stubGlobal('confirm', vi.fn(() => true));
    const cancel = vi.fn();
    withActions(lifecycle('active'), 'en', { ...actions, cancel });

    menuOf('VPS');
    fireEvent.click(screen.getByRole('menuitem', { name: 'Cancel VPS' }));

    expect(cancel).toHaveBeenCalledTimes(1);
    expect(cancel.mock.calls[0]![0]).toMatchObject({ id: 's1' });
    vi.unstubAllGlobals();
  });
});

// The routed screen: the caller's permissions come from GET /me/scope, in the same load as the lists (OPEN_ITEMS 77).
describe('the actions follow the permission (GET /me/scope)', () => {
  const A = { tenant_id: 'tenant-a', name: 'Al-Amin' };
  const rows = { scope_mode: 'all', visible_count: 1, total_count: 1, has_more_in_scope: false,
    items: [{ id: 's1', client_id: 'c1', service_name: 'VPS', ends_on: '2099-01-02', status: 'active' }] };
  const clientRows = { scope_mode: 'all', visible_count: 1, total_count: 1, has_more_in_scope: false,
    items: [{ id: 'c1', name: 'Client A', is_active: true }] };

  function api(meScope: () => Response) {
    const paths: string[] = [];
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) => {
        const path = String(input).replace(/^\/api/, '').split('?')[0]!;
        paths.push(path);
        if (path === '/me') return Response.json({ username: 'layla', active_tenant: A });
        if (path === '/me/scope') return meScope();
        if (path === '/subscriptions/clients') return Response.json(clientRows);
        if (path === '/subscriptions') return Response.json(rows);
        return new Response(null, { status: 404 });
      }),
    );
    return paths;
  }

  const permitted = (...permissions: string[]) => () =>
    Response.json({ scope_mode: 'all', can_manage_scope: false, assignments: [], permissions });

  function renderScreen() {
    render(
      <I18nProvider initial="en">
        <MemoryRouter>
          <SessionProvider>
            <SubscriptionsScreen />
          </SessionProvider>
        </MemoryRouter>
      </I18nProvider>,
    );
  }

  afterEach(() => vi.unstubAllGlobals());

  it('a viewer: no "New subscription", no actions column, no menu', async () => {
    const paths = api(permitted('core.audit.read', 'subscriptions.read'));
    renderScreen();

    await screen.findByText('VPS');
    expect(paths).toContain('/me/scope');
    expect(screen.queryByRole('link', { name: 'New subscription' })).toBeNull();
    expect(screen.queryByRole('columnheader', { name: 'Actions' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Actions for VPS' })).toBeNull();
  });

  it('a member who may write: "New subscription", the column, and the menu', async () => {
    api(permitted('subscriptions.read', 'subscriptions.write'));
    renderScreen();

    await screen.findByText('VPS');
    expect(screen.getByRole('link', { name: 'New subscription' })).toBeDefined();
    expect(screen.getByRole('columnheader', { name: 'Actions' })).toBeDefined();
    expect(screen.getByRole('button', { name: 'Actions for VPS' })).toBeDefined();
  });

  // Nothing is shown until the permissions have answered: no action appears and then vanishes.
  it('shows no row and no action while the permissions are still loading', async () => {
    let answer: (r: Response) => void = () => undefined;
    api(() => new Promise<Response>((resolve) => (answer = resolve)) as unknown as Response);
    renderScreen();

    await screen.findByText('Loading…');
    expect(screen.queryByText('VPS')).toBeNull();
    expect(screen.queryByRole('link', { name: 'New subscription' })).toBeNull();
    answer(permitted('subscriptions.write')());
    await screen.findByText('VPS');
  });

  // A failed /me/scope is the screen's error, not a list that silently lost its actions.
  it('shows the error path when /me/scope fails, never the list without its actions', async () => {
    api(() => Response.json({ error: 'internal_error' }, { status: 500 }));
    renderScreen();

    await screen.findByRole('alert');
    expect(screen.queryByText('VPS')).toBeNull();
    expect(screen.queryByRole('table')).toBeNull();
  });
});
