import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { I18nProvider } from '../../i18n/I18nProvider';
import { InviteMemberScreen } from './InviteMemberScreen';

/**
 * The invitation form's address (the project owner's item 35, group ج). The server refuses an undeliverable
 * address with `invalid_value` and someone already here with `already_member`; the screen tells the person before
 * they submit where it can, and says what the refusal means where it cannot.
 */

interface Call {
  method: string;
  path: string;
}

const json = (status: number, body: unknown) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });

function api(onInvite: () => Response) {
  const calls: Call[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const path = String(input).replace(/^\/api/, '');
      const method = init?.method ?? 'GET';
      calls.push({ method, path });
      if (path === '/roles')
        return json(200, { items: [{ id: 'r1', code: 'viewer', name_ar: 'مشاهد', name_en: 'Viewer', is_system: true, permissions: [] }] });
      if (path === '/me/scope')
        return json(200, { scope_mode: 'all', can_manage_scope: true, assignments: [], permissions: ['core.members.manage'] });
      return onInvite();
    }),
  );
  return calls;
}

async function fill(email: string) {
  render(
    <I18nProvider initial="en">
      <MemoryRouter>
        <InviteMemberScreen />
      </MemoryRouter>
    </I18nProvider>,
  );
  const input = (await screen.findByLabelText('E-mail address')) as HTMLInputElement;
  fireEvent.change(input, { target: { value: email } });
  fireEvent.change(screen.getByLabelText('Role'), { target: { value: 'r1' } });
  fireEvent.click(screen.getByRole('button', { name: 'Send the invitation' }));
  return input;
}

const invitations = (calls: Call[]) => calls.filter((c) => c.method === 'POST' && c.path === '/members/invitations');

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('InviteMemberScreen — the address', () => {
  it.each(['adnan', 'hasan@', 'a@x.com, b@y.com', 'a b@x.com', '"Adnan" <adnan@x.com>'])(
    'does not send %j, and says why at the field',
    async (email) => {
      const calls = api(() => json(201, {}));
      const input = await fill(email);

      expect(await screen.findByText('Enter one e-mail address, such as name@example.com.')).toBeTruthy();
      expect(input.getAttribute('aria-invalid')).toBe('true');
      expect(invitations(calls)).toHaveLength(0);
    },
  );

  it('shows the server’s invalid_value at the field when the screen let the address through', async () => {
    api(() => json(400, { error: 'invalid_value' }));
    const input = await fill('a,b@x.com');

    expect(await screen.findByText('Enter one e-mail address, such as name@example.com.')).toBeTruthy();
    expect(input.getAttribute('aria-invalid')).toBe('true');
  });

  // The acceptance's own message ("You are already a member…") speaks to the invitee; the inviter needs another.
  it('says what already_member means to the person inviting', async () => {
    const calls = api(() => json(409, { error: 'already_member' }));
    await fill('aya@x.com');

    await waitFor(() => expect(invitations(calls)).toHaveLength(1));
    expect(
      await screen.findByText(
        'This address already belongs to a member of this organization. A disabled member is enabled from the member list, not invited again.',
      ),
    ).toBeTruthy();
    expect(screen.queryByText('You are already a member of this organization.')).toBeNull();
  });
});
