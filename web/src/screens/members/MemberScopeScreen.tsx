import { useEffect, useState } from 'react';
import { useIntl } from 'react-intl';
import { Link, useParams } from 'react-router';
import { ApiError, getJson, isAbort, request } from '../../api/client';
import { Badge } from '../../components/Badge';
import { ErrorAlert } from '../../components/ErrorAlert';
import { errorMessage } from '../../i18n/apiErrors';
import { ForbiddenScreen } from '../status/StatusScreens';
import type { ClientItem } from '../clients/clientForm';
import type { ScopedList } from '../subscriptions/SubscriptionsScreen';
import { LIST_PATH } from './MembersScreen';
import { scopeMessages } from './formMessages';
import { managesMembersOrScope } from './scopeWarning';
import type { CatalogueRole, MemberItem } from './types';
import './MembersScreen.css';

/** GET /scope/memberships/{id}: the mode, and every assignment row — withdrawn ones included. */
interface MemberScope {
  scope_mode: 'all' | 'assigned';
  assignments: { scope_ref_id: string; assignment_role: string; active: boolean; reason: string | null }[];
}

type State =
  | { status: 'loading' }
  | { status: 'error'; code: string | null }
  | { status: 'self' }
  | {
      status: 'ready';
      member: MemberItem;
      scope: MemberScope;
      clients: ClientItem[];
      ownScopeIsAssigned: boolean;
      /** Item 36: whether one of the member's roles manages members or scope, from the role catalogue. */
      managesMembersOrScope?: boolean;
    };

/**
 * A member's scope, and the clients assigned to them (PLATFORM_CORE §4.8) — the second axis, which until now was
 * proven in the database and provable nowhere else.
 *
 * Four things here are the architecture, not the layout:
 *
 * - **An assignment is toggled, never deleted.** `UNIQUE (tenant_id, membership_id, scope_ref_id)` with no
 *   `DELETE` grant for anyone gives one permanent row per pair, disabled and reactivated. The control is a
 *   switch, the word is "withdraw", and reactivation restores visibility without rewriting the past.
 * - **Withdrawn rows stay on screen**, marked inactive. They are not absence: they come back into force the
 *   moment the mode returns to `assigned`, and a screen that hid them would restore visibility nobody had seen.
 * - **Under `all` the list is shown and disabled**, with one line saying why, for the same reason.
 * - **No one sets their own scope.** `membership_scope_admin_update` carries `membership_id <> app.membership_id`
 *   — without it, a member holding `core.scope.manage` in mode `assigned` could raise themselves to `all` with
 *   one command. The page says that, rather than letting the database answer 403 into an empty form.
 *
 * The client list is itself under the second axis (`GET /subscriptions/clients`), so a manager whose own mode is
 * `assigned` is offered only the clients they can see. That is correct — one cannot assign what one cannot see —
 * and it is stated, so a short list does not read as a fault.
 */
export function MemberScopeScreen() {
  const intl = useIntl();
  const { membershipId = '' } = useParams();
  const [state, setState] = useState<State>({ status: 'loading' });
  const [refusal, setRefusal] = useState<string | null>(null);
  const [saving, setSaving] = useState<string | null>(null);
  const [reload, setReload] = useState(0);

  useEffect(() => {
    const abort = new AbortController();
    Promise.all([
      getJson<ScopedList<MemberItem>>('/members?limit=200', abort.signal),
      getJson<MemberScope>(`/scope/memberships/${membershipId}`, abort.signal),
      getJson<ScopedList<ClientItem>>('/subscriptions/clients?limit=200', abort.signal),
      getJson<{ scope_mode: 'all' | 'assigned' }>('/me/scope', abort.signal),
      // The catalogue: which of the member's roles manage members or scope (item 36). Readable with
      // core.scope.manage alone, which is all this screen requires.
      getJson<{ items: CatalogueRole[] }>('/roles', abort.signal),
    ]).then(
      ([members, scope, clients, own, catalogue]) => {
        const member = members.items.find((m) => m.membership_id === membershipId);
        if (!member) {
          setState({ status: 'error', code: 'not_permitted' });
          return;
        }
        // Reached by typing the address: the row's action is not offered for one's own membership.
        if (member.is_self) {
          setState({ status: 'self' });
          return;
        }
        const held = new Set(member.roles.map((role) => role.id));
        setState({
          status: 'ready',
          member,
          scope,
          clients: clients.items,
          ownScopeIsAssigned: own.scope_mode === 'assigned',
          managesMembersOrScope: managesMembersOrScope(catalogue.items.filter((role) => held.has(role.id))),
        });
      },
      (error: unknown) => {
        if (abort.signal.aborted || isAbort(error)) return;
        setState({ status: 'error', code: error instanceof ApiError ? error.code : null });
      },
    );
    return () => abort.abort();
  }, [membershipId, reload]);

  if (state.status === 'error' && state.code === 'not_permitted') return <ForbiddenScreen />;

  if (state.status === 'self')
    return (
      <main className="member-form">
        <h1>{intl.formatMessage(scopeMessages.ownScope)}</h1>
        <p className="lede">{intl.formatMessage(scopeMessages.ownScopeBody)}</p>
        <div className="form-actions">
          <Link to={LIST_PATH} className="btn">
            {intl.formatMessage(scopeMessages.back)}
          </Link>
        </div>
      </main>
    );

  const write = async (key: string, run: () => Promise<unknown>) => {
    setRefusal(null);
    setSaving(key);
    try {
      await run();
      setReload((n) => n + 1);
    } catch (e) {
      if (isAbort(e)) return;
      setRefusal(e instanceof ApiError ? e.code : null);
    } finally {
      setSaving(null);
    }
  };

  const setMode = (mode: 'all' | 'assigned') =>
    write('mode', () => request('PUT', `/scope/memberships/${membershipId}/mode`, { scope_mode: mode }));

  const setAssigned = (client: ClientItem, active: boolean) =>
    write(client.id, () =>
      request('PUT', `/scope/memberships/${membershipId}/assignments/${client.id}`, { active, reason: null, assignment_role: null }),
    );

  return (
    <main className="member-form member-scope">
      {state.status === 'ready' && <h1>{intl.formatMessage(scopeMessages.title, { member: state.member.full_name })}</h1>}
      <ErrorAlert message={refusal ? intl.formatMessage(errorMessage(refusal)) : null} />
      {state.status === 'loading' && <p>{intl.formatMessage(scopeMessages.loading)}</p>}
      {state.status === 'error' && <p role="alert">{intl.formatMessage(errorMessage(state.code))}</p>}
      {state.status === 'ready' && (
        <ScopeForm
          state={state}
          saving={saving}
          setMode={(mode) => void setMode(mode)}
          setAssigned={(client, active) => void setAssigned(client, active)}
        />
      )}
      <div className="form-actions">
        <Link to={LIST_PATH} className="btn">
          {intl.formatMessage(scopeMessages.back)}
        </Link>
      </div>
    </main>
  );
}

export interface ScopeFormProps {
  state: Extract<State, { status: 'ready' }>;
  saving: string | null;
  setMode: (mode: 'all' | 'assigned') => void;
  setAssigned: (client: ClientItem, active: boolean) => void;
}

export function ScopeForm({ state, saving, setMode, setAssigned }: ScopeFormProps) {
  const intl = useIntl();
  const { scope, clients, ownScopeIsAssigned } = state;
  const underAll = scope.scope_mode === 'all';
  const activeFor = (id: string) => scope.assignments.find((a) => a.scope_ref_id === id)?.active ?? false;
  const known = (id: string) => scope.assignments.some((a) => a.scope_ref_id === id);

  return (
    <>
      <fieldset className="scope-choice" data-scope-mode={scope.scope_mode}>
        <legend>{intl.formatMessage(scopeMessages.modeLegend)}</legend>
        <label>
          <input
            type="radio"
            name="mode"
            value="assigned"
            checked={!underAll}
            disabled={saving !== null}
            onChange={() => setMode('assigned')}
          />
          <span>{intl.formatMessage(scopeMessages.modeAssigned)}</span>
        </label>
        <label>
          <input type="radio" name="mode" value="all" checked={underAll} disabled={saving !== null} onChange={() => setMode('all')} />
          <span>{intl.formatMessage(scopeMessages.modeAll)}</span>
        </label>
        <p className="field-note">{intl.formatMessage(underAll ? scopeMessages.modeAllNote : scopeMessages.modeAssignedNote)}</p>
        {/* Item 36: whenever the state holds — on load too, so this page answers why an admin sees some clients
            only. One status line on a page opened on purpose: never a prompt, never a block. */}
        {!underAll && state.managesMembersOrScope && (
          <p role="status" className="field-note field-warning" data-note="assigned-manager">
            {intl.formatMessage(scopeMessages.assignedManager, { member: state.member.full_name })}
          </p>
        )}
      </fieldset>

      <h2>{intl.formatMessage(scopeMessages.assignmentsTitle)}</h2>
      {/* Shown and disabled under 'all', never hidden: the rows are permanent and apply again the moment the
          mode returns to 'assigned', so hiding them would open visibility nobody had seen. */}
      {underAll && <p className="field-note" data-note="under-all">{intl.formatMessage(scopeMessages.assignmentsUnderAll)}</p>}
      {ownScopeIsAssigned && <p className="field-note" data-note="own-scope">{intl.formatMessage(scopeMessages.yourOwnClients)}</p>}
      {clients.length === 0 ? (
        <p className="field-note">{intl.formatMessage(scopeMessages.assignmentsNone)}</p>
      ) : (
        <ul className="assignment-list">
          {clients.map((client) => {
            const on = activeFor(client.id);
            return (
              <li key={client.id} data-client={client.id} data-assigned={on ? 'true' : 'false'}>
                <label>
                  <input
                    type="checkbox"
                    role="switch"
                    checked={on}
                    disabled={underAll || saving !== null}
                    aria-label={intl.formatMessage(scopeMessages.assignLabel, { client: client.name })}
                    onChange={() => setAssigned(client, !on)}
                  />
                  <bdi dir="auto">{client.name}</bdi>
                  {/* The project owner's item 32-c, decided explicitly rather than by silence: a disabled client
                      is still assignable and still seen by the member assigned to it. It carries its badge here
                      exactly as it does in the client list. */}
                  {!client.is_active && (
                    <Badge tone="neutral">
                      <span data-client-status="disabled">{intl.formatMessage(scopeMessages.disabledClient)}</span>
                    </Badge>
                  )}
                  {/* A row that exists and is off: withdrawn, not absent. §4.8 keeps it for ever. */}
                  {!on && known(client.id) && <span className="row-note" data-note="withdrawn">{intl.formatMessage(scopeMessages.withdrawnNote)}</span>}
                </label>
              </li>
            );
          })}
        </ul>
      )}
      {saving !== null && <p className="field-note">{intl.formatMessage(scopeMessages.saving)}</p>}
    </>
  );
}
