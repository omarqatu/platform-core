import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { I18nProvider } from '../../i18n/I18nProvider';
import type { Lang } from '../../i18n/locale';
import type { ScopedList } from '../subscriptions/SubscriptionsScreen';
import { ClientsView, type ListActions } from './ClientsScreen';
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

const actions = (setStatus: ListActions['setStatus']): ListActions => ({
  create: () => null,
  edit: () => null,
  setStatus,
});

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

    fireEvent.click(screen.getByRole('button', { name: 'Disable Client A' }));

    expect(confirm).toHaveBeenCalledWith(
      'Stop the reminders for Client A? Its subscriptions are not changed, and you can enable it again.',
    );
    expect(setStatus).not.toHaveBeenCalled();
  });

  it('disables once the question is answered', () => {
    vi.stubGlobal('confirm', vi.fn(() => true));
    const setStatus = vi.fn();
    show('en', list([two[0]], 'all'), actions(setStatus));

    fireEvent.click(screen.getByRole('button', { name: 'Disable Client A' }));

    expect(setStatus).toHaveBeenCalledWith(two[0], false);
  });

  // Enabling resumes the reminders and catches nothing up, so there is nothing to warn about.
  it('enables without asking', () => {
    const confirm = vi.fn(() => true);
    vi.stubGlobal('confirm', confirm);
    const setStatus = vi.fn();
    show('en', list([two[1]], 'all'), actions(setStatus));

    fireEvent.click(screen.getByRole('button', { name: 'Enable شركة النور' }));

    expect(confirm).not.toHaveBeenCalled();
    expect(setStatus).toHaveBeenCalledWith(two[1], true);
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
