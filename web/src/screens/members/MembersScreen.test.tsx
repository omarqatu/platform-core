import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { I18nProvider } from '../../i18n/I18nProvider';
import type { Lang } from '../../i18n/locale';
import type { ScopedList } from '../subscriptions/SubscriptionsScreen';
import { MembersView, readyState, SCOPE_SCREEN_BUILT, type MemberActions } from './MembersScreen';
import { invitationExpired, type InvitationItem, type MemberItem, type RoleItem } from './types';

const owner: RoleItem = { id: 'r1', code: 'owner', name_ar: 'مالك', name_en: 'Owner', is_system: true };
const operator: RoleItem = { id: 'r2', code: 'operator', name_ar: 'مشغّل', name_en: 'Operator', is_system: true };

function member(over: Partial<MemberItem> = {}): MemberItem {
  return {
    membership_id: 'm1',
    full_name: 'Ahmad',
    email: 'ahmad@x.test',
    username: 'ahmad',
    status: 'active',
    roles: [operator],
    scope_mode: 'assigned',
    is_self: false,
    ...over,
  };
}

function list<T>(items: T[]): ScopedList<T> {
  return { scope_mode: 'all', visible_count: items.length, total_count: items.length, has_more_in_scope: false, items };
}

const DAY = 24 * 60 * 60 * 1000;
function invitation(over: Partial<InvitationItem> = {}): InvitationItem {
  return {
    id: 'i1',
    email: 'new@x.test',
    role_id: 'r2',
    intended_scope_mode: 'assigned',
    status: 'pending',
    expires_at: new Date(Date.now() + 7 * DAY).toISOString(),
    created_at: new Date().toISOString(),
    ...over,
  };
}

const BOTH: MemberActions = { members: true, scope: true };
const MEMBERS_ONLY: MemberActions = { members: true, scope: false };
const SCOPE_ONLY: MemberActions = { members: false, scope: true };

function show(lang: Lang, members: MemberItem[], held: MemberActions, invitations: InvitationItem[] | null = []) {
  return render(
    <I18nProvider initial={lang}>
      <MemoryRouter>
        <MembersView
          state={readyState(list(members), invitations === null ? null : list(invitations))}
          held={held}
          setStatus={() => undefined}
          withdraw={() => undefined}
        />
      </MemoryRouter>
    </I18nProvider>,
  ).container;
}

const menuOf = (label: string) => {
  fireEvent.click(screen.getByRole('button', { name: label }));
  return screen.getAllByRole('menuitem').map((i) => i.getAttribute('aria-label'));
};

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  document.cookie = 'lang=; Path=/; Max-Age=0';
});

describe('the member list', () => {
  it('shows each member with their roles and status', () => {
    const page = show('en', [member({ roles: [owner, operator] })], BOTH);

    expect(page.querySelector('[data-membership="m1"] bdi')!.textContent).toBe('Ahmad');
    expect(page.querySelector('[data-roles]')!.getAttribute('data-roles')).toBe('owner operator');
    expect(page.querySelector('[data-status]')!.getAttribute('data-status')).toBe('active');
  });

  // PLATFORM_CORE §4.8/1.8: "someone who sees every file has no corresponding need to see who was assigned to
  // what". The server answers null for a colleague's mode without core.scope.manage, so the column must not be
  // there at all — an empty column reads as "no scope", which is a different and false statement.
  it('has no scope column at all without scope management, rather than an empty one', () => {
    const page = show('en', [member()], MEMBERS_ONLY);

    expect(page.textContent).not.toContain('Scope');
    expect(page.querySelector('[data-scope-mode]')).toBeNull();
  });

  it('shows the scope column with scope management', () => {
    const page = show('en', [member()], BOTH);

    expect(page.querySelector('[data-scope-mode]')!.getAttribute('data-scope-mode')).toBe('assigned');
    expect(page.textContent).toContain('Assigned clients');
  });

  // Every write policy on this surface carries "not the actor's own membership" (§4.8, §3.10), so the database
  // refuses it. Offering the actions and letting the server say no is the shape the rest of the interface avoids.
  it('offers no actions on the caller to themselves, and says why', () => {
    const page = show('en', [member({ is_self: true })], BOTH);

    expect(screen.queryByRole('button', { name: 'Actions for Ahmad' })).toBeNull();
    expect(page.querySelector('.row-note')!.textContent).toBe('Your own membership');
  });

  it('offers no actions on a member who has left', () => {
    const page = show('en', [member({ status: 'left' })], BOTH);

    expect(screen.queryByRole('button', { name: 'Actions for Ahmad' })).toBeNull();
    expect(page.querySelector('.row-note')!.textContent).toBe('Left, no actions');
  });

  // The two permissions are orthogonal (§4.8): each brings its own action and neither implies the other.
  it('offers roles and disabling with member management alone', () => {
    show('en', [member()], MEMBERS_ONLY);

    expect(menuOf('Actions for Ahmad')).toEqual(['Roles for Ahmad', 'Disable Ahmad']);
  });

  // Held back until the assignments screen exists: no change in this group leaves a visible path into a page that
  // answers 404, and the platform is in use. When that screen lands, SCOPE_SCREEN_BUILT turns true in the same
  // change, and this expectation becomes ['Scope for Ahmad'].
  it('offers nothing yet with scope management alone, while its screen is unbuilt', () => {
    show('en', [member()], SCOPE_ONLY);

    expect(SCOPE_SCREEN_BUILT).toBe(false);
    expect(screen.queryByRole('button', { name: 'Actions for Ahmad' })).toBeNull();
    // And no actions column standing empty above the rows: a button that opens nothing is worse than the 404
    // holding the action back was meant to avoid.
    expect(document.querySelector('th.actions')).toBeNull();
    expect(document.querySelector('td.actions')).toBeNull();
  });

  it('offers enabling, not disabling, for a disabled member', () => {
    show('en', [member({ status: 'disabled' })], MEMBERS_ONLY);

    expect(menuOf('Actions for Ahmad')).toEqual(['Roles for Ahmad', 'Enable Ahmad']);
  });

  it('asks before disabling, and says what it does not change', () => {
    const confirm = vi.fn((message?: string) => Boolean(message) && false);
    vi.stubGlobal('confirm', confirm);
    show('en', [member()], MEMBERS_ONLY);
    fireEvent.click(screen.getByRole('button', { name: 'Actions for Ahmad' }));

    fireEvent.click(screen.getByRole('menuitem', { name: 'Disable Ahmad' }));

    expect(confirm).toHaveBeenCalledOnce();
    expect(confirm.mock.calls[0]![0]).toContain('are not changed');
  });

  it('has no actions column at all when the caller manages neither', () => {
    const page = show('en', [member()], { members: false, scope: false });

    expect(page.querySelector('td.actions')).toBeNull();
    expect(page.querySelector('th.actions')).toBeNull();
  });
});

describe('the invitations', () => {
  it('is not offered at all without member management', () => {
    const page = show('en', [member()], SCOPE_ONLY, null);

    expect(page.textContent).not.toContain('Invitations');
    expect(screen.queryByRole('link', { name: 'Invite a member' })).toBeNull();
  });

  it('offers the way to invite with member management', () => {
    show('en', [member()], MEMBERS_ONLY, []);

    expect(screen.getByRole('link', { name: 'Invite a member' })).toBeTruthy();
  });

  it('lists a waiting invitation with its day and its scope', () => {
    const page = show('en', [member()], MEMBERS_ONLY, [invitation()]);

    expect(page.querySelector('[data-invitation="i1"] bdi')!.textContent).toBe('new@x.test');
    expect(page.querySelector('[data-intended-scope]')!.getAttribute('data-intended-scope')).toBe('assigned');
    expect(page.querySelector('time')).toBeTruthy();
  });

  // Nothing in src/ writes status = 'expired', so the column would say "pending" forever. The day decides, the
  // way subscriptionStatus derives "expired" from ends_on while the row stays active.
  it('derives expiry from the date, never from the status column', () => {
    const past = invitation({ expires_at: new Date(Date.now() - DAY).toISOString() });

    expect(past.status).toBe('pending');
    expect(invitationExpired(past)).toBe(true);
    expect(invitationExpired(invitation())).toBe(false);
  });

  it('offers no action on an invitation whose day has passed', () => {
    const page = show('en', [member()], MEMBERS_ONLY, [invitation({ expires_at: new Date(Date.now() - DAY).toISOString() })]);

    expect(screen.queryByRole('button', { name: /Actions for the invitation/ })).toBeNull();
    expect(page.querySelector('.invitations .row-note')!.textContent).toBe('Expired, no actions');
  });

  // §3.10 D8: pending → revoked. The row stays and the address can be invited again, so the word is "withdraw".
  it('withdraws an invitation, never deletes it, and asks first', () => {
    const confirm = vi.fn((message?: string) => Boolean(message) && false);
    vi.stubGlobal('confirm', confirm);
    show('en', [member()], MEMBERS_ONLY, [invitation()]);

    expect(menuOf('Actions for the invitation to new@x.test')).toEqual(['Withdraw the invitation to new@x.test']);
    fireEvent.click(screen.getByRole('menuitem', { name: 'Withdraw the invitation to new@x.test' }));
    expect(confirm.mock.calls[0]![0]).toContain('invite the same address again');
  });

  it('shows only what is still waiting', () => {
    const page = show('en', [member()], MEMBERS_ONLY, [invitation({ id: 'i2', status: 'revoked' }), invitation()]);

    expect(page.querySelector('[data-invitation="i2"]')).toBeNull();
    expect(page.querySelector('[data-invitation="i1"]')).toBeTruthy();
  });
});

describe('in Arabic', () => {
  it('names a role from the row, in Arabic', () => {
    const page = show('ar', [member({ roles: [owner] })], BOTH);

    expect(page.textContent).toContain('مالك');
    // Arabic's `one` form carries no digit at all — "عضو واحد", a word. The count is checked below, where the
    // plural form actually has one.
    expect(page.querySelector('.count')!.textContent).toBe('عضو واحد في هذه الجهة.');
  });

  it('writes a count in Western digits, as every number in the interface is', () => {
    const three = [member({ membership_id: 'm1' }), member({ membership_id: 'm2' }), member({ membership_id: 'm3' })];

    const count = show('ar', three, BOTH).querySelector('.count')!.textContent!;

    expect(count).toMatch(/3/);
    expect(count).not.toMatch(/[٠-٩۰-۹]/);
  });
});
