import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { I18nProvider } from '../../i18n/I18nProvider';
import type { Lang } from '../../i18n/locale';
import type { ClientItem } from '../clients/clientForm';
import { ScopeForm, type ScopeFormProps } from './MemberScopeScreen';
import type { MemberItem } from './types';

const ahmad: MemberItem = {
  membership_id: 'm1',
  full_name: 'Ahmad',
  email: 'ahmad@x.test',
  username: 'ahmad',
  status: 'active',
  roles: [],
  scope_mode: 'assigned',
  is_self: false,
};

const clients: ClientItem[] = [
  { id: 'c1', name: 'Client One', is_active: true },
  { id: 'c2', name: 'Client Two', is_active: true },
  { id: 'c3', name: 'Client Three', is_active: true },
  { id: 'c4', name: 'Disabled Co', is_active: false },
];

type Assignment = { scope_ref_id: string; assignment_role: string; active: boolean; reason: string | null };
const assignment = (id: string, active: boolean): Assignment => ({ scope_ref_id: id, assignment_role: 'contributor', active, reason: null });

function form(
  lang: Lang,
  over: { mode?: 'all' | 'assigned'; assignments?: Assignment[]; ownAssigned?: boolean; clients?: ClientItem[] } = {},
  handlers: Partial<Pick<ScopeFormProps, 'setMode' | 'setAssigned'>> = {},
) {
  return render(
    <I18nProvider initial={lang}>
      <MemoryRouter>
        <ScopeForm
          state={{
            status: 'ready',
            member: ahmad,
            scope: { scope_mode: over.mode ?? 'assigned', assignments: over.assignments ?? [] },
            clients: over.clients ?? clients,
            ownScopeIsAssigned: over.ownAssigned ?? false,
          }}
          saving={null}
          setMode={handlers.setMode ?? (() => undefined)}
          setAssigned={handlers.setAssigned ?? (() => undefined)}
        />
      </MemoryRouter>
    </I18nProvider>,
  ).container;
}

const toggle = (client: string) => screen.getByRole('switch', { name: `Assign ${client}` }) as HTMLInputElement;

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  document.cookie = 'lang=; Path=/; Max-Age=0';
});

describe('a member’s scope', () => {
  it('shows the mode, and the clients as switches', () => {
    form('en', { assignments: [assignment('c1', true)] });

    expect(toggle('Client One').checked).toBe(true);
    expect(toggle('Client Two').checked).toBe(false);
  });

  it('assigns a client that is off, and withdraws one that is on', () => {
    const setAssigned = vi.fn();
    form('en', { assignments: [assignment('c1', true)] }, { setAssigned });

    fireEvent.click(toggle('Client Two'));
    expect(setAssigned.mock.calls[0]![1]).toBe(true);

    fireEvent.click(toggle('Client One'));
    expect(setAssigned.mock.calls[1]![1]).toBe(false);
  });

  /**
   * §4.8: one permanent row per (membership, client), disabled and reactivated, never deleted. A withdrawn row is
   * data — it comes back into force when the mode returns to 'assigned' — so the screen marks it, and does not
   * let it look like a client that was never assigned.
   */
  it('marks a withdrawn assignment, rather than showing it as never assigned', () => {
    const page = form('en', { assignments: [assignment('c1', true), assignment('c2', false)] });

    expect(page.querySelector('[data-client="c2"] [data-note="withdrawn"]')).toBeTruthy();
    expect(page.querySelector('[data-client="c3"] [data-note="withdrawn"]')).toBeNull();
    expect(toggle('Client Two').checked).toBe(false);
  });

  /**
   * Under 'all' the assignments have no effect — and are kept, and apply again the moment the mode goes back.
   * Hiding them would restore visibility nobody on this screen had seen.
   */
  it('shows the clients under all, disabled, with the reason', () => {
    const page = form('en', { mode: 'all', assignments: [assignment('c1', true)] });

    expect(page.querySelector('[data-note="under-all"]')!.textContent).toContain('apply again');
    expect(toggle('Client One').disabled).toBe(true);
    expect(toggle('Client Two').disabled).toBe(true);
    expect(page.querySelectorAll('.assignment-list li').length).toBe(4);
  });

  it('enables the switches again under assigned', () => {
    form('en', { mode: 'assigned' });

    expect(toggle('Client One').disabled).toBe(false);
  });

  it('changes the mode', () => {
    const setMode = vi.fn();
    form('en', { mode: 'assigned' }, { setMode });

    fireEvent.click(screen.getByRole('radio', { name: 'All clients' }));

    expect(setMode).toHaveBeenCalledWith('all');
  });

  it('says what each mode means, and the assigned note promises no total', () => {
    const page = form('en', { mode: 'assigned' });

    expect(page.querySelector('.field-note')!.textContent).toContain('not even how many there are');
  });

  /**
   * GET /subscriptions/clients is itself under the second axis, so a manager whose own mode is 'assigned' is
   * offered only their own clients. Correct — one cannot assign what one cannot see — and said, so the short
   * list does not read as a fault.
   */
  it('tells an assigned manager that the list is their own scope', () => {
    const page = form('en', { ownAssigned: true });

    expect(page.querySelector('[data-note="own-scope"]')!.textContent).toContain('within your own scope');
  });

  it('says nothing of the sort to a manager who sees everything', () => {
    const page = form('en', { ownAssigned: false });

    expect(page.querySelector('[data-note="own-scope"]')).toBeNull();
  });

  /**
   * The project owner's item 32-c, decided explicitly rather than by silence: a disabled client stays assignable
   * and stays visible to the member assigned to it. It carries the same badge the client list gives it.
   */
  it('keeps a disabled client assignable, with its badge', () => {
    const page = form('en');

    expect(page.querySelector('[data-client="c4"] [data-client-status="disabled"]')).toBeTruthy();
    expect(toggle('Disabled Co').disabled).toBe(false);
  });

  it('says so when the organization has no client', () => {
    const page = form('en', { clients: [] });

    expect(page.textContent).toContain('no client yet');
    expect(page.querySelector('.assignment-list')).toBeNull();
  });

  it('reads in Arabic', () => {
    const page = form('ar', { mode: 'all' });

    expect(page.textContent).toContain('كل العملاء');
    expect(page.querySelector('[data-note="under-all"]')).toBeTruthy();
  });
});
