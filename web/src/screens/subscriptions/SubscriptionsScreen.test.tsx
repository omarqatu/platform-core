import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { I18nProvider } from '../../i18n/I18nProvider';
import type { Lang } from '../../i18n/locale';
import {
  DUE_SOON_DAYS,
  localDay,
  subscriptionStatus,
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
  const lifecycle = (status: SubscriptionItem['status']): SubscriptionItem[] => [
    { id: 's1', client_id: 'c1', service_name: 'VPS', ends_on: '2027-01-02', status },
  ];

  const actions: ListActions = {
    create: () => null,
    edit: () => <span data-testid="edit" />,
    renew: () => <span data-testid="renew" />,
    cancel: () => undefined,
  };

  function withActions(items: SubscriptionItem[]) {
    return render(
      <I18nProvider initial="en">
        <SubscriptionsView
          state={{ status: 'ready', data: { subscriptions: list(items, 'all'), clients: list(clients, 'all') } }}
          actions={actions}
        />
      </I18nProvider>,
    ).container;
  }

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

  // The database refuses an edit, a renewal and a cancellation on a row that has left 'active', so the screen
  // offers none of the three rather than offering what it knows will be refused.
  it('offers edit, renew and cancel on an active row and none of them otherwise', () => {
    expect(withActions(lifecycle('active')).querySelectorAll('[data-testid]')).toHaveLength(2);
    expect(screen.getByRole('button', { name: 'Cancel VPS' })).toBeDefined();
    cleanup();
    for (const status of ['cancelled', 'renewed'] as const) {
      const page = withActions(lifecycle(status));
      expect(page.querySelectorAll('[data-testid]')).toHaveLength(0);
      expect(page.querySelectorAll('.row-actions button')).toHaveLength(0);
      cleanup();
    }
  });

  it('asks before cancelling, and says the row is kept', () => {
    const confirm = vi.fn(() => false);
    vi.stubGlobal('confirm', confirm);
    const cancel = vi.fn();
    render(
      <I18nProvider initial="en">
        <SubscriptionsView
          state={{ status: 'ready', data: { subscriptions: list(lifecycle('active'), 'all'), clients: list(clients, 'all') } }}
          actions={{ ...actions, cancel }}
        />
      </I18nProvider>,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Cancel VPS' }));

    expect(confirm).toHaveBeenCalledWith(
      'Cancel VPS? It stays in the list with its history, and it cannot be edited or renewed afterwards.',
    );
    expect(cancel).not.toHaveBeenCalled();
    vi.unstubAllGlobals();
  });
});
