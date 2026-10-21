import { useCallback, useEffect, useState, type ReactNode } from 'react';
import { useIntl } from 'react-intl';
import { Link } from 'react-router';
import { ApiError, getJson, isAbort, request } from '../../api/client';
import { Badge, type BadgeTone } from '../../components/Badge';
import { Day } from '../../components/Day';
import { ErrorAlert } from '../../components/ErrorAlert';
import { RowActions, type RowAction } from '../../components/RowActions';
import { errorMessage } from '../../i18n/apiErrors';
import { useLocale } from '../../i18n/I18nProvider';
import { getPermissions, PERMISSION } from '../../session/permissions';
import { useSession } from '../../session/SessionProvider';
import { ForbiddenScreen } from '../status/StatusScreens';
import { TENANT_LOST, type ScopedList } from '../subscriptions/SubscriptionsScreen';
import { messages } from './messages';
import { invitationExpired, roleName, type InvitationItem, type MemberItem } from './types';
import './MembersScreen.css';

export const LIST_PATH = '/app/members';
export const INVITE_PATH = '/app/members/invite';
export const PAGE_SIZE = 200; // the API's maximum page

/**
 * Whether the member row offers its way to the assignments screen. False until that screen and its route exist:
 * every change in this group must be mergeable on its own, and the platform is in use, so none of them may leave
 * a visible path into a page that answers 404.
 */
export const SCOPE_SCREEN_BUILT = false;

/** What the caller may do here, learnt from GET /me/scope with the rows (OPEN_ITEMS 77). */
export interface MemberActions {
  /** core.members.manage: invite, withdraw, disable and enable, and change roles. */
  members: boolean;
  /** core.scope.manage: the scope column, and the way to a member's assignments. */
  scope: boolean;
}

interface Data {
  members: ScopedList<MemberItem>;
  invitations: ScopedList<InvitationItem> | null;
}

type State = { status: 'loading' } | { status: 'error'; code: string | null } | { status: 'ready'; data: Data };

/**
 * The member list (PLATFORM_CORE §3.10, §4.5; OPEN_ITEMS 100): who is in the organization, and the invitations
 * waiting to be accepted.
 *
 * Two permissions, two different screens behind one address. `core.members.manage` brings the invitations, the
 * status actions and the role screen; `core.scope.manage` brings the scope column and the way to a member's
 * assignments. Holding one and not the other is a real combination, not a theoretical one — the platform's own
 * seed has a member with scope management and no member management.
 *
 * Whoever holds neither never arrives: `GET /members` refuses them, and the sidebar does not offer the link. That
 * refusal is the whole of PROOF_SPEC T8.2 for member data — the database would show them everyone — so the screen
 * must not soften it into an empty list or a count.
 */
export function MembersScreen() {
  const { revalidate } = useSession();
  const [state, setState] = useState<State>({ status: 'loading' });
  const [held, setHeld] = useState<MemberActions>({ members: false, scope: false });
  const [refusal, setRefusal] = useState<string | null>(null);
  const [reload, setReload] = useState(0);

  useEffect(() => {
    const abort = new AbortController();
    // The permissions first: whether the invitations may be asked for at all depends on them, and asking without
    // the permission would spend a 403 on a question the interface already knows the answer to.
    getPermissions(abort.signal)
      .then(async (permissions) => {
        const members = permissions.has(PERMISSION.membersManage);
        const scope = permissions.has(PERMISSION.scopeManage);
        const [list, invitations] = await Promise.all([
          getJson<ScopedList<MemberItem>>(`/members?limit=${PAGE_SIZE}`, abort.signal),
          members ? getJson<ScopedList<InvitationItem>>(`/members/invitations?limit=${PAGE_SIZE}`, abort.signal) : null,
        ]);
        return { permissions: { members, scope }, data: { members: list, invitations } };
      })
      .then(
        ({ permissions, data }) => {
          setHeld(permissions);
          setState({ status: 'ready', data });
        },
        (error: unknown) => {
          if (abort.signal.aborted || isAbort(error)) return;
          const code = error instanceof ApiError ? error.code : null;
          if (code && TENANT_LOST.has(code)) revalidate().catch(() => undefined);
          setState({ status: 'error', code });
        },
      );
    return () => abort.abort();
  }, [revalidate, reload]);

  const act = useCallback(
    async (method: 'PUT' | 'POST', path: string, body?: unknown) => {
      setRefusal(null);
      try {
        await request(method, path, body);
        setReload((n) => n + 1);
      } catch (e) {
        if (isAbort(e)) return;
        const code = e instanceof ApiError ? e.code : null;
        setRefusal(code);
        if (code && TENANT_LOST.has(code)) revalidate().catch(() => undefined);
      }
    },
    [revalidate],
  );

  if (state.status === 'error' && state.code === 'not_permitted') return <ForbiddenScreen />;
  return (
    <MembersView
      state={state}
      held={held}
      refusal={refusal}
      setStatus={(member, status) => act('PUT', `/members/${member.membership_id}/status`, { status })}
      withdraw={(invitation) => act('POST', `/members/invitations/${invitation.id}/revoke`)}
    />
  );
}

export interface ViewProps {
  state: State;
  held: MemberActions;
  refusal?: string | null;
  setStatus?: (member: MemberItem, status: 'active' | 'disabled') => void | Promise<void>;
  withdraw?: (invitation: InvitationItem) => void | Promise<void>;
}

export function MembersView({ state, held, refusal, setStatus, withdraw }: ViewProps) {
  const intl = useIntl();
  return (
    <main className="members">
      <div className="heading">
        <h1>{intl.formatMessage(messages.title)}</h1>
        {/* Hidden, never shown and then refused (the project owner's item 24): an operator who cannot invite is
            not offered the form, because POST /members/invitations would refuse it and the database beneath it. */}
        {state.status === 'ready' && held.members && (
          <Link to={INVITE_PATH} className="btn">
            {intl.formatMessage(messages.invite)}
          </Link>
        )}
      </div>
      <ErrorAlert message={refusal ? intl.formatMessage(errorMessage(refusal)) : null} />
      {state.status === 'loading' && <p>{intl.formatMessage(messages.loading)}</p>}
      {state.status === 'error' && <p role="alert">{intl.formatMessage(errorMessage(state.code))}</p>}
      {state.status === 'ready' && (
        <>
          <MembersTable members={state.data.members} held={held} setStatus={setStatus} />
          {held.members && state.data.invitations && (
            <InvitationsTable invitations={state.data.invitations} withdraw={withdraw} />
          )}
        </>
      )}
    </main>
  );
}

const STATUS = {
  active: { tone: 'success' as BadgeTone, message: messages.statusActive },
  disabled: { tone: 'neutral' as BadgeTone, message: messages.statusDisabled },
  left: { tone: 'neutral' as BadgeTone, message: messages.statusLeft },
} satisfies Record<MemberItem['status'], { tone: BadgeTone; message: typeof messages.statusActive }>;

function MembersTable({ members, held, setStatus }: { members: ScopedList<MemberItem>; held: MemberActions; setStatus?: ViewProps['setStatus'] }) {
  const intl = useIntl();
  const { lang } = useLocale();
  // Any action at all needs a permission; with none there is no actions column, as the client list does it.
  // SCOPE_SCREEN_BUILT is part of the condition and not an afterthought: while the assignments screen is unbuilt,
  // scope management alone grants no action here, and a column of buttons opening empty menus would be a worse
  // fault than the 404 it was meant to avoid.
  const actions = held.members || (held.scope && SCOPE_SCREEN_BUILT);
  return (
    <>
      <p className="count">{intl.formatMessage(messages.count, { members: members.visible_count })}</p>
      <div className="data-table">
        <table>
          <thead>
            <tr>
              <th scope="col">{intl.formatMessage(messages.columnName)}</th>
              {/* At phone width the roles fold under the name and the scope under the status (§15). */}
              <th scope="col" className="col-folded">
                {intl.formatMessage(messages.columnRoles)}
              </th>
              <th scope="col">{intl.formatMessage(messages.columnStatus)}</th>
              {/* The column does not exist without core.scope.manage — it is not rendered empty. The server
                  returns null for every colleague's mode there (membership_scope_read, §4.8/1.8), and a blank
                  column would read as "no scope" instead of "not yours to see". */}
              {held.scope && (
                <th scope="col" className="col-folded">
                  {intl.formatMessage(messages.columnScope)}
                </th>
              )}
              {actions && (
                <th scope="col" className="actions">
                  <span className="th-label">{intl.formatMessage(messages.columnActions)}</span>
                </th>
              )}
            </tr>
          </thead>
          <tbody>
            {members.items.map((member) => {
              const roles = <Roles member={member} lang={lang} />;
              const scope = <Scope member={member} />;
              return (
                <tr key={member.membership_id} data-membership={member.membership_id}>
                  <td>
                    <bdi dir="auto">{member.full_name}</bdi>
                    <span className="cell-fold">{roles}</span>
                  </td>
                  <td className="col-folded">{roles}</td>
                  <td>
                    <Badge tone={STATUS[member.status].tone}>
                      <span data-status={member.status}>{intl.formatMessage(STATUS[member.status].message)}</span>
                    </Badge>
                    {held.scope && <span className="cell-fold">{scope}</span>}
                  </td>
                  {held.scope && <td className="col-folded">{scope}</td>}
                  {actions && (
                    <td className="actions">
                      <div className="row-actions">
                        <MemberRowActions member={member} held={held} setStatus={setStatus} />
                      </div>
                    </td>
                  )}
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
      {members.has_more_in_scope && <p className="more">{intl.formatMessage(messages.more, { shown: members.items.length })}</p>}
    </>
  );
}

function Roles({ member, lang }: { member: MemberItem; lang: string }) {
  const intl = useIntl();
  if (member.roles.length === 0) return <span className="row-note">{intl.formatMessage(messages.noRoles)}</span>;
  return (
    <span data-roles={member.roles.map((r) => r.code).join(' ')}>
      {member.roles.map((role) => (
        <bdi key={role.id} dir="auto" className="role-chip">
          {roleName(role, lang)}
        </bdi>
      ))}
    </span>
  );
}

function Scope({ member }: { member: MemberItem }) {
  const intl = useIntl();
  if (member.scope_mode === null) return null;
  return (
    <span data-scope-mode={member.scope_mode}>
      {intl.formatMessage(member.scope_mode === 'all' ? messages.scopeAll : messages.scopeAssigned)}
    </span>
  );
}

/**
 * A member's actions. Two rows never have a menu, and say why rather than showing an empty cell:
 *
 * - **the caller's own** — every write policy on this surface carries "not the actor's own membership" (§4.8,
 *   §3.10), so the database refuses it. A member cannot widen their own scope or change their own roles, and the
 *   screen states that instead of offering actions that would come back 403;
 * - **a member who has left** — their membership is over; they return by accepting a new invitation (§4.5).
 */
function MemberRowActions({ member, held, setStatus }: { member: MemberItem; held: MemberActions; setStatus?: ViewProps['setStatus'] }) {
  const intl = useIntl();
  const [pending, setPending] = useState(false);
  if (pending) return <span className="row-note">{intl.formatMessage(messages.working)}</span>;
  if (member.is_self) return <span className="row-note">{intl.formatMessage(messages.ownMembership)}</span>;
  if (member.status === 'left') return <span className="row-note">{intl.formatMessage(messages.hasLeft)}</span>;

  const name = member.full_name;
  const change = async (status: 'active' | 'disabled') => {
    if (status === 'disabled' && !globalThis.confirm(intl.formatMessage(messages.confirmDisable, { member: name }))) return;
    setPending(true);
    await setStatus?.(member, status);
    setPending(false);
  };

  const items: RowAction[] = [];
  if (held.members)
    items.push({
      key: 'roles',
      label: intl.formatMessage(messages.roles),
      accessibleName: intl.formatMessage(messages.rolesLabel, { member: name }),
      icon: 'edit',
      to: `${LIST_PATH}/${member.membership_id}/roles`,
    });
  // The way to a member's assignments. Held back until the screen behind it exists: a visible path into an
  // unbuilt page is a 404 in a live platform, and no change in this group leaves one (the project owner's rule,
  // 2026-10-21). SCOPE_SCREEN_BUILT turns it on in the same change that adds the route.
  if (held.scope && SCOPE_SCREEN_BUILT)
    items.push({
      key: 'scope',
      label: intl.formatMessage(messages.manageScope),
      accessibleName: intl.formatMessage(messages.manageScopeLabel, { member: name }),
      icon: 'layers',
      to: `${LIST_PATH}/${member.membership_id}/scope`,
    });
  if (held.members && setStatus)
    items.push(
      member.status === 'active'
        ? {
            key: 'disable',
            label: intl.formatMessage(messages.disable),
            accessibleName: intl.formatMessage(messages.disableLabel, { member: name }),
            icon: 'pause',
            danger: true,
            onSelect: () => void change('disabled'),
          }
        : {
            key: 'enable',
            label: intl.formatMessage(messages.enable),
            accessibleName: intl.formatMessage(messages.enableLabel, { member: name }),
            icon: 'play',
            onSelect: () => void change('active'),
          },
    );
  // A button that opens nothing is a fault, not an empty state. The column above is gone in this case too; this
  // is the second guard, so a future permission combination cannot reintroduce it by itself.
  if (items.length === 0) return null;
  return <RowActions label={intl.formatMessage(messages.actionsFor, { member: name })} actions={items} />;
}

function InvitationsTable({ invitations, withdraw }: { invitations: ScopedList<InvitationItem>; withdraw?: ViewProps['withdraw'] }) {
  const intl = useIntl();
  // Only what is still waiting: an accepted invitation is a member in the table above, and a withdrawn one is
  // over. The server keeps all three — the screen shows the ones that can still be acted on.
  const waiting = invitations.items.filter((i) => i.status === 'pending');
  return (
    <section className="invitations">
      <h2>{intl.formatMessage(messages.invitationsTitle)}</h2>
      {waiting.length === 0 ? (
        <p>{intl.formatMessage(messages.invitationsNone)}</p>
      ) : (
        <div className="data-table">
          <table>
            <thead>
              <tr>
                <th scope="col">{intl.formatMessage(messages.columnEmail)}</th>
                <th scope="col" className="col-folded">
                  {intl.formatMessage(messages.columnIntendedScope)}
                </th>
                <th scope="col">{intl.formatMessage(messages.columnExpires)}</th>
                <th scope="col" className="actions">
                  <span className="th-label">{intl.formatMessage(messages.columnActions)}</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {waiting.map((invitation) => {
                const scope = (
                  <span data-intended-scope={invitation.intended_scope_mode}>
                    {intl.formatMessage(invitation.intended_scope_mode === 'all' ? messages.scopeAll : messages.scopeAssigned)}
                  </span>
                );
                return (
                  <tr key={invitation.id} data-invitation={invitation.id}>
                    <td>
                      <bdi dir="auto">{invitation.email}</bdi>
                      <span className="cell-fold">{scope}</span>
                    </td>
                    <td className="col-folded">{scope}</td>
                    <td>
                      <Day day={invitation.expires_at.slice(0, 10)} />
                    </td>
                    <td className="actions">
                      <div className="row-actions">
                        <InvitationRowActions invitation={invitation} withdraw={withdraw} />
                      </div>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}

/**
 * An invitation's one action, withdrawing it — **withdrawn, never deleted**: the row stays, its status becomes
 * `revoked`, and the same address can be invited again (§3.10 D8).
 *
 * An invitation whose date has passed has no action, because `POST …/revoke` writes `WHERE status = 'pending'` and
 * the acceptance path refuses it on its own date. "Expired" is derived from `expires_at` and never read from
 * `status`: nothing in `src/` writes `status = 'expired'`, so a screen trusting the column would show an expired
 * invitation as waiting forever.
 */
function InvitationRowActions({ invitation, withdraw }: { invitation: InvitationItem; withdraw?: ViewProps['withdraw'] }) {
  const intl = useIntl();
  const [pending, setPending] = useState(false);
  if (pending) return <span className="row-note">{intl.formatMessage(messages.working)}</span>;
  if (invitationExpired(invitation)) return <span className="row-note">{intl.formatMessage(messages.invitationExpiredNote)}</span>;

  const email = invitation.email;
  const act = async () => {
    if (!globalThis.confirm(intl.formatMessage(messages.confirmWithdraw, { email }))) return;
    setPending(true);
    await withdraw?.(invitation);
    setPending(false);
  };
  const items: RowAction[] = [
    {
      key: 'withdraw',
      label: intl.formatMessage(messages.withdraw),
      accessibleName: intl.formatMessage(messages.withdrawLabel, { email }),
      icon: 'cancel',
      danger: true,
      onSelect: () => void act(),
    },
  ];
  return <RowActions label={intl.formatMessage(messages.invitationActionsFor, { email })} actions={items} />;
}

export type { Data as MembersData, State as MembersState };
export function readyState(members: ScopedList<MemberItem>, invitations: ScopedList<InvitationItem> | null): State {
  return { status: 'ready', data: { members, invitations } };
}

export type MembersScreenNode = ReactNode;
