import { cleanup, render } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { I18nProvider } from '../../i18n/I18nProvider';
import type { Lang } from '../../i18n/locale';
import { SubscriptionsView, type ClientItem, type ScopedList, type SubscriptionItem } from './SubscriptionsScreen';

const ARABIC_INDIC_DIGITS = /[٠-٩۰-۹]/;

function list<T>(items: T[], scope: 'all' | 'assigned', visible = items.length, more = false): ScopedList<T> {
  return { scope_mode: scope, visible_count: visible, total_count: scope === 'all' ? visible : null, has_more_in_scope: more, items };
}

const clients: ClientItem[] = [
  { id: 'c1', name: 'Client A' },
  { id: 'c2', name: 'شركة النور' },
];
const subscriptions = (n: number): SubscriptionItem[] =>
  Array.from({ length: n }, (_, i) => ({
    id: `s${i}`,
    client_id: i % 2 ? 'c2' : 'c1',
    service_name: i === 0 ? 'استضافة' : `A service ${i}`,
    ends_on: '2027-01-02',
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
