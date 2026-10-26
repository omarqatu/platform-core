import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { I18nProvider } from '../../i18n/I18nProvider';
import { InviteMemberScreen } from './InviteMemberScreen';

/**
 * The invite form's warning (the project owner's item 36, group ج): a role holding core.members.manage or
 * core.scope.manage, combined with scope `assigned`, reads as a mistake — someone who manages members or scope yet
 * sees only some clients. It is one line at selection and never a block: the person may well mean it. Read from
 * the roles' permissions, as GET /roles returns them, never from a role's name.
 */

const WARNING = 'This role manages members or scope, but they will see only their assigned clients. If that is what you intend, continue.';

const json = (status: number, body: unknown) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });

const ROLES = [
  {
    id: 'admin',
    code: 'admin',
    name_ar: 'مدير',
    name_en: 'Admin',
    is_system: true,
    permissions: ['core.audit.read', 'core.members.manage', 'core.scope.manage', 'subscriptions.read'],
  },
  { id: 'viewer', code: 'viewer', name_ar: 'مشاهد', name_en: 'Viewer', is_system: true, permissions: ['core.audit.read', 'subscriptions.read'] },
  // A role of the organization's own, named nothing the code could know: the rule is its permission.
  { id: 'custom', code: 'custom-x', name_ar: 'مسند', name_en: 'Assigner', is_system: false, permissions: ['core.scope.manage'] },
];

function api() {
  const posts: unknown[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const path = String(input).replace(/^\/api/, '');
      if (path === '/roles') return json(200, { items: ROLES });
      if (path === '/me/scope')
        return json(200, { scope_mode: 'all', can_manage_scope: true, assignments: [], permissions: ['core.members.manage', 'core.scope.manage'] });
      posts.push(JSON.parse(String(init?.body)));
      return json(201, { invitation_id: 'i1', token: 't', expires_at: '2026-11-02T00:00:00Z' });
    }),
  );
  return posts;
}

async function open() {
  render(
    <I18nProvider initial="en">
      <MemoryRouter>
        <InviteMemberScreen />
      </MemoryRouter>
    </I18nProvider>,
  );
  await screen.findByLabelText('E-mail address');
}

const choose = (role: string, mode: 'all' | 'assigned') => {
  fireEvent.change(screen.getByLabelText('Role'), { target: { value: role } });
  fireEvent.click(screen.getByRole('radio', { name: mode === 'all' ? 'All clients' : 'Assigned clients' }));
};

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('InviteMemberScreen — a manager with assigned clients', () => {
  it.each([
    ['admin', 'assigned', true],
    ['custom', 'assigned', true],
    ['viewer', 'assigned', false],
    ['admin', 'all', false],
    ['viewer', 'all', false],
  ] as const)('%s with %s: warning shown = %s', async (role, mode, shown) => {
    api();
    await open();

    choose(role, mode);

    expect(screen.queryByText(WARNING) !== null).toBe(shown);
  });

  it('is a status line, and goes when the combination does', async () => {
    api();
    await open();
    choose('admin', 'assigned');
    expect(screen.getByText(WARNING).closest('[role="status"]')).not.toBeNull();

    choose('admin', 'all');

    expect(screen.queryByText(WARNING)).toBeNull();
  });

  it('never blocks: the invitation is sent with the warning showing', async () => {
    const posts = api();
    await open();
    fireEvent.change(screen.getByLabelText('E-mail address'), { target: { value: 'aya@x.test' } });
    choose('admin', 'assigned');
    expect(screen.getByText(WARNING)).toBeTruthy();

    fireEvent.click(screen.getByRole('button', { name: 'Send the invitation' }));

    await waitFor(() => expect(posts).toEqual([{ email: 'aya@x.test', role_id: 'admin', intended_scope_mode: 'assigned' }]));
  });
});
