import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { I18nProvider } from '../../i18n/I18nProvider';
import type { Lang } from '../../i18n/locale';
import type { ScopedList } from '../subscriptions/SubscriptionsScreen';
import { MembersView, readyState, type MemberActions } from './MembersScreen';
import type { MemberItem } from './types';

/**
 * The member list's username, and its sign-in help (the project owner's item 37, group ج). The first question a
 * manager asks when a member says "I cannot sign in" is which username they use; the answer is a message that
 * points the member at the forgot-password page, never a token the manager holds.
 */

function member(over: Partial<MemberItem> = {}): MemberItem {
  return {
    membership_id: 'm1',
    full_name: 'Ahmad',
    email: 'ahmad@x.test',
    username: 'ahmad.k',
    status: 'active',
    roles: [],
    scope_mode: 'assigned',
    is_self: false,
    ...over,
  };
}

const list = (items: MemberItem[]): ScopedList<MemberItem> => ({
  scope_mode: 'all',
  visible_count: items.length,
  total_count: items.length,
  has_more_in_scope: false,
  items,
});

const MEMBERS_ONLY: MemberActions = { members: true, scope: false };
const SCOPE_ONLY: MemberActions = { members: false, scope: true };

function show(members: MemberItem[], held: MemberActions, signInHelp = vi.fn(), lang: Lang = 'en') {
  const page = render(
    <I18nProvider initial={lang}>
      <MemoryRouter>
        <MembersView
          state={readyState(list(members), null)}
          held={held}
          setStatus={() => undefined}
          withdraw={() => undefined}
          signInHelp={signInHelp}
        />
      </MemoryRouter>
    </I18nProvider>,
  ).container;
  return { page, signInHelp };
}

const menuOf = (label: string) => {
  fireEvent.click(screen.getByRole('button', { name: label }));
  return screen.getAllByRole('menuitem').map((i) => i.getAttribute('aria-label'));
};

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('the username column', () => {
  it('shows what each member signs in with, left to right', () => {
    const { page } = show([member()], MEMBERS_ONLY);

    expect(screen.getByRole('columnheader', { name: 'Username' })).toBeTruthy();
    const cell = page.querySelector('[data-membership="m1"] [data-username]')!;
    expect(cell.textContent).toBe('ahmad.k');
    expect(cell.getAttribute('dir')).toBe('ltr');
  });

  it('is there in Arabic too, the username itself still left to right', () => {
    const { page } = show([member()], MEMBERS_ONLY, vi.fn(), 'ar');

    expect(screen.getByRole('columnheader', { name: 'اسم المستخدم' })).toBeTruthy();
    expect(page.querySelector('[data-username]')!.getAttribute('dir')).toBe('ltr');
  });
});

describe('sign-in help', () => {
  it('is offered for an active member, with member management', () => {
    show([member()], MEMBERS_ONLY);

    expect(menuOf('Actions for Ahmad')).toContain('Sign-in help for Ahmad');
  });

  it('is not offered for a disabled member, who cannot sign in here at all', () => {
    show([member({ status: 'disabled' })], MEMBERS_ONLY);

    expect(menuOf('Actions for Ahmad')).not.toContain('Sign-in help for Ahmad');
  });

  it('is not offered with scope management alone', () => {
    show([member()], SCOPE_ONLY);

    expect(menuOf('Actions for Ahmad')).not.toContain('Sign-in help for Ahmad');
  });

  it('asks first, saying what is sent and what is not, and sends on yes', () => {
    const confirm = vi.fn(() => true);
    vi.stubGlobal('confirm', confirm);
    const { signInHelp } = show([member()], MEMBERS_ONLY);
    fireEvent.click(screen.getByRole('button', { name: 'Actions for Ahmad' }));

    fireEvent.click(screen.getByRole('menuitem', { name: 'Sign-in help for Ahmad' }));

    expect(confirm).toHaveBeenCalledOnce();
    expect(String(confirm.mock.calls[0]![0])).toContain('forgot-password page');
    expect(signInHelp).toHaveBeenCalledWith(expect.objectContaining({ membership_id: 'm1' }));
  });

  it('sends nothing on no', () => {
    vi.stubGlobal('confirm', vi.fn(() => false));
    const { signInHelp } = show([member()], MEMBERS_ONLY);
    fireEvent.click(screen.getByRole('button', { name: 'Actions for Ahmad' }));

    fireEvent.click(screen.getByRole('menuitem', { name: 'Sign-in help for Ahmad' }));

    expect(signInHelp).not.toHaveBeenCalled();
  });
});
