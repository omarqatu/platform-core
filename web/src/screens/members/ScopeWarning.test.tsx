import { cleanup, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { afterEach, describe, expect, it } from 'vitest';
import { I18nProvider } from '../../i18n/I18nProvider';
import type { Lang } from '../../i18n/locale';
import { ScopeForm } from './MemberScopeScreen';
import type { MemberItem } from './types';

/**
 * The scope screen's line (the project owner's item 36, group ج): shown whenever the member holds a role that
 * manages members or scope **and** their scope is `assigned` — on load too, because a week later someone asks why
 * an admin sees two clients, and this page should answer it. One status line on a page opened on purpose, never a
 * prompt and never a block.
 */

const aya: MemberItem = {
  membership_id: 'm1',
  full_name: 'Aya',
  email: 'aya@x.test',
  username: 'aya',
  status: 'active',
  roles: [],
  scope_mode: 'assigned',
  is_self: false,
};

const LINE = 'Aya manages members or scope, but sees only their assigned clients. If that is what you intend, nothing more is needed.';

function form(mode: 'all' | 'assigned', managesMembersOrScope: boolean, lang: Lang = 'en') {
  return (
    <I18nProvider initial={lang}>
      <MemoryRouter>
        <ScopeForm
          state={{
            status: 'ready',
            member: aya,
            scope: { scope_mode: mode, assignments: [] },
            clients: [],
            ownScopeIsAssigned: false,
            managesMembersOrScope,
          }}
          saving={null}
          setMode={() => undefined}
          setAssigned={() => undefined}
        />
      </MemoryRouter>
    </I18nProvider>
  );
}

afterEach(cleanup);

describe('ScopeForm — a manager with assigned clients', () => {
  it('shows the line on load when the state holds, as a status', () => {
    render(form('assigned', true));

    expect(screen.getByText(LINE).closest('[role="status"]')).not.toBeNull();
  });

  it('hides it once the member is moved to all clients', () => {
    const { rerender } = render(form('assigned', true));

    rerender(form('all', true));

    expect(screen.queryByText(LINE)).toBeNull();
  });

  it('never shows it for a member who manages neither', () => {
    render(form('assigned', false));

    expect(screen.queryByText(LINE)).toBeNull();
  });

  it('says it in Arabic, in the present', () => {
    render(form('assigned', true, 'ar'));

    expect(screen.getByText('Aya يدير الأعضاء أو النطاق، لكنه يرى العملاء المحددين له فقط. إن كان هذا ما تقصده، فلا حاجة لأي إجراء.')).toBeTruthy();
  });
});
