import { useEffect, useId, useState, type ReactNode } from 'react';
import { useIntl } from 'react-intl';
import { Link, useSearchParams } from 'react-router';
import { ApiError, getJson, isAbort, request } from '../../api/client';
import { Badge, type BadgeTone } from '../../components/Badge';
import { ErrorAlert } from '../../components/ErrorAlert';
import { RowActions, type RowAction } from '../../components/RowActions';
import { errorMessage } from '../../i18n/apiErrors';
import { getPermissions, PERMISSION } from '../../session/permissions';
import { useSession } from '../../session/SessionProvider';
import { ForbiddenScreen } from '../status/StatusScreens';
import { messages as form } from './formMessages';
import type { ClientItem } from '../clients/clientForm';
import { messages, type StatusFilter } from './messages';
import type { LifecycleStatus } from './subscriptionForm';
import './SubscriptionsScreen.css';

/** A page of an aggregate list with its scope declaration (PLATFORM_CORE 3.5) — ScopedList in Core. */
export interface ScopedList<T> {
  scope_mode: 'all' | 'assigned';
  visible_count: number;
  total_count: number | null;
  has_more_in_scope: boolean;
  items: T[];
}

export interface SubscriptionItem {
  id: string;
  client_id: string;
  service_name: string;
  ends_on: string; // yyyy-MM-dd
  status: LifecycleStatus;
}

export interface SubscriptionsData {
  subscriptions: ScopedList<SubscriptionItem>;
  clients: ScopedList<ClientItem>;
}

export type { ClientItem };

export const LIST_PATH = '/app/subscriptions';
export const PAGE_SIZE = 50;
const CLIENTS_LIMIT = 200; // the API's maximum page

type State = { status: 'loading' } | { status: 'error'; code: string | null } | { status: 'ready'; data: SubscriptionsData };

export type { StatusFilter };
const FILTERS: readonly StatusFilter[] = ['active', 'renewed', 'cancelled', 'all'];

/**
 * The filter in the address (?status=renewed|cancelled|all), so Back and a reload keep it. "active" is the default
 * and is never written; anything the interface does not know reads as the default.
 */
export function filterOf(params: URLSearchParams): StatusFilter {
  const value = params.get('status');
  return value !== 'active' && FILTERS.includes(value as StatusFilter) ? (value as StatusFilter) : 'active';
}

/** The list request for a filter: "all" sends no status (every status), any other sends its own. */
export function listPath(filter: StatusFilter): string {
  return `/subscriptions?limit=${PAGE_SIZE}${filter === 'all' ? '' : `&status=${filter}`}`;
}

/** The tenant can no longer be entered: the session asks /me again, and the interface returns to the selection. */
export const TENANT_LOST = new Set(['not_a_member', 'no_active_tenant', 'tenant_not_active']);

/**
 * The subscription list (PROOF_SPEC T8), from GET /subscriptions and GET /subscriptions/clients: the scope declaration,
 * then the rows. The chrome is translated; what users entered — services and clients — is shown as is, in its own
 * direction (dir="auto"). The caller's permissions (GET /me/scope) come with the lists, in the same load: nothing is
 * shown until all three have answered, so no action appears and then vanishes, and a failure of any of them is the
 * screen's one error path — never a list that silently lost its actions.
 */
export function SubscriptionsScreen() {
  const [params, setParams] = useSearchParams();
  const filter = filterOf(params);
  // The answer, with the filter it answers: until the one for the filter in force arrives, the screen is loading — a
  // filter changed by Back shows no list of the filter before it.
  const [loaded, setLoaded] = useState<{ filter: StatusFilter; state: State }>({ filter, state: { status: 'loading' } });
  const state: State = loaded.filter === filter ? loaded.state : { status: 'loading' };
  const [canWrite, setCanWrite] = useState(false);
  const [refusal, setRefusal] = useState<string | null>(null);
  const [reload, setReload] = useState(0);
  const { revalidate } = useSession();

  useEffect(() => {
    const abort = new AbortController();
    Promise.all([
      getJson<ScopedList<SubscriptionItem>>(listPath(filter), abort.signal),
      getJson<ScopedList<ClientItem>>(`/subscriptions/clients?limit=${CLIENTS_LIMIT}`, abort.signal),
      getPermissions(abort.signal),
    ]).then(
      ([subscriptions, clients, permissions]) => {
        setCanWrite(permissions.has(PERMISSION.subscriptionsWrite));
        setLoaded({ filter, state: { status: 'ready', data: { subscriptions, clients } } });
      },
      (error: unknown) => {
        if (abort.signal.aborted || isAbort(error)) return;
        const code = error instanceof ApiError ? error.code : null;
        if (code && TENANT_LOST.has(code)) revalidate().catch(() => undefined);
        setLoaded({ filter, state: { status: 'error', code } });
      },
    );
    return () => abort.abort();
  }, [revalidate, reload, filter]);

  if (state.status === 'error' && state.code === 'not_permitted') return <ForbiddenScreen />;
  return (
    <SubscriptionsView
      state={state}
      filter={filter}
      onFilter={(next) => {
        setRefusal(null);
        setParams(next === 'active' ? {} : { status: next });
      }}
      refusal={refusal}
      // Shown only to a member who may write (OPEN_ITEMS 77): a viewer has no "New subscription" and no actions
      // column. The server is still the guard — a role changed elsewhere shows on the next load, and until then a
      // refused action says so.
      actions={
        canWrite
          ? {
              create: () => <NewLink />,
              cancel: async (subscription) => {
                setRefusal(null);
                try {
                  await request('POST', `/subscriptions/${subscription.id}/cancel`);
                  setReload((n) => n + 1);
                } catch (e) {
                  if (isAbort(e)) return;
                  const code = e instanceof ApiError ? e.code : null;
                  setRefusal(code);
                  if (code && TENANT_LOST.has(code)) revalidate().catch(() => undefined);
                }
              },
            }
          : undefined
      }
    />
  );
}

/**
 * The list's ways to act (Amendment 02 §3.2, Amendment 01 §2.9), given only to a member who holds
 * subscriptions.write: creating, and cancelling a row. Editing and renewing are links to their screens, which the
 * view builds itself; it needs a router for them only when it is given actions.
 */
export interface ListActions {
  create: () => ReactNode;
  cancel: (subscription: SubscriptionItem) => void | Promise<void>;
}

function NewLink() {
  const intl = useIntl();
  return (
    <Link to={`${LIST_PATH}/new`} className="btn">
      {intl.formatMessage(form.newSubscription)}
    </Link>
  );
}

/**
 * A row's actions, by its state (the project owner's table): an active row has edit, renew and cancel — renew first
 * once its end date has passed, with a note saying since when; a renewed or a cancelled row has none, and says so,
 * because the database's own gate refuses the write and an empty cell reads as a fault. "Expired" comes from the end
 * date (subscriptionStatus), never from the status, which stays 'active' past the end. Cancelling asks first, because
 * the row stops being editable afterwards; while the request runs the row says it is working.
 */
function RowActionsCell({
  subscription,
  today,
  cancel,
}: {
  subscription: SubscriptionItem;
  today: string;
  cancel: ListActions['cancel'];
}) {
  const intl = useIntl();
  const [pending, setPending] = useState(false);
  if (subscription.status === 'renewed') return <span className="row-note">{intl.formatMessage(form.renewedNoActions)}</span>;
  if (subscription.status === 'cancelled') return <span className="row-note">{intl.formatMessage(form.cancelledNoActions)}</span>;
  // A status the interface does not know offers nothing it cannot vouch for.
  if (subscription.status !== 'active') return null;
  if (pending) return <span className="row-note">{intl.formatMessage(form.working)}</span>;

  const service = subscription.service_name;
  const ask = async () => {
    if (!globalThis.confirm(intl.formatMessage(form.confirmCancel, { service }))) return;
    setPending(true);
    await cancel(subscription);
    setPending(false);
  };
  const edit: RowAction = {
    key: 'edit',
    label: intl.formatMessage(form.edit),
    accessibleName: intl.formatMessage(form.editLabel, { service }),
    icon: 'edit',
    to: `${LIST_PATH}/${subscription.id}/edit`,
  };
  const renew: RowAction = {
    key: 'renew',
    label: intl.formatMessage(form.renew),
    accessibleName: intl.formatMessage(form.renewLabel, { service }),
    icon: 'renew',
    to: `${LIST_PATH}/${subscription.id}/renew`,
  };
  const cancelAction: RowAction = {
    key: 'cancel',
    label: intl.formatMessage(form.cancelSubscription),
    accessibleName: intl.formatMessage(form.cancelLabel, { service }),
    icon: 'cancel',
    danger: true,
    onSelect: () => void ask(),
  };
  const expired = subscriptionStatus(subscription.ends_on, today) === 'expired';
  return (
    <>
      {expired && (
        <span className="row-note" data-note="expired">
          {intl.formatMessage(form.expiredSince, { date: subscription.ends_on, ltr: (chunks) => <bdi dir="ltr">{chunks}</bdi> })}
        </span>
      )}
      <RowActions
        label={intl.formatMessage(form.actionsFor, { service })}
        actions={expired ? [renew, edit, cancelAction] : [edit, renew, cancelAction]}
      />
    </>
  );
}

/**
 * The list for one filter. `state` is the answer for `filter`; `onFilter` asks for another one (the screen writes it
 * to the address). Without `onFilter` the filter is shown and cannot be changed.
 */
export function SubscriptionsView({
  state,
  filter,
  onFilter,
  actions,
  refusal,
}: {
  state: State;
  filter: StatusFilter;
  onFilter?: (filter: StatusFilter) => void;
  actions?: ListActions;
  refusal?: string | null;
}) {
  const intl = useIntl();
  return (
    <main className="subscriptions">
      <div className="heading">
        <h1>{intl.formatMessage(messages.title)}</h1>
        {state.status === 'ready' && actions?.create()}
      </div>
      <FilterControl filter={filter} onFilter={onFilter} />
      <ErrorAlert message={refusal ? intl.formatMessage(errorMessage(refusal)) : null} />
      {state.status === 'loading' && <p>{intl.formatMessage(messages.loading)}</p>}
      {state.status === 'error' && <p role="alert">{intl.formatMessage(errorMessage(state.code))}</p>}
      {state.status === 'ready' && <SubscriptionsTable data={state.data} filter={filter} actions={actions} />}
    </main>
  );
}

const FILTER_LABEL: Record<StatusFilter, typeof messages.filterActive> = {
  active: messages.filterActive,
  renewed: messages.filterRenewed,
  cancelled: messages.filterCancelled,
  all: messages.filterAll,
};

/** One choice of four, as radio buttons (the arrow keys move between them), drawn as a segmented control. */
function FilterControl({ filter, onFilter }: { filter: StatusFilter; onFilter?: (filter: StatusFilter) => void }) {
  const intl = useIntl();
  const name = useId();
  return (
    <fieldset className="status-filter">
      <legend>{intl.formatMessage(messages.filterLabel)}</legend>
      <div className="status-filter-options">
        {FILTERS.map((value) => (
          <label key={value}>
            <input
              type="radio"
              name={name}
              value={value}
              checked={filter === value}
              disabled={!onFilter}
              onChange={() => onFilter?.(value)}
            />
            <span>{intl.formatMessage(FILTER_LABEL[value])}</span>
          </label>
        ))}
      </div>
    </fieldset>
  );
}

function SubscriptionsTable({
  data: { subscriptions, clients },
  filter,
  actions,
}: {
  data: SubscriptionsData;
  filter: StatusFilter;
  actions?: ListActions;
}) {
  const intl = useIntl();
  const names = new Map(clients.items.map((c) => [c.id, c.name]));
  const today = localDay(new Date());
  return (
    <>
      <Declaration subscriptions={subscriptions} clients={clients.visible_count} filter={filter} />
      <div className="data-table">
        <table>
          <thead>
            <tr>
              <th scope="col">{intl.formatMessage(messages.columnService)}</th>
              <th scope="col">{intl.formatMessage(messages.columnClient)}</th>
              <th scope="col">{intl.formatMessage(messages.columnEndsOn)}</th>
              <th scope="col">{intl.formatMessage(messages.columnStatus)}</th>
              {actions && (
                <th scope="col" className="actions">
                  {intl.formatMessage(form.columnActions)}
                </th>
              )}
            </tr>
          </thead>
          <tbody>
            {subscriptions.items.map((s) => {
              const client = names.get(s.client_id);
              return (
                <tr key={s.id}>
                  <td>
                    <bdi dir="auto">{s.service_name}</bdi>
                  </td>
                  <td>
                    {client === undefined ? intl.formatMessage(messages.clientUnavailable) : <bdi dir="auto">{client}</bdi>}
                  </td>
                  <td className="date">
                    <time dateTime={s.ends_on}>{formatDay(intl.formatDate, s.ends_on)}</time>
                  </td>
                  <td>
                    <RowStatus subscription={s} today={today} />
                  </td>
                  {actions && (
                    <td className="actions">
                      <div className="row-actions">
                        <RowActionsCell subscription={s} today={today} cancel={actions.cancel} />
                      </div>
                    </td>
                  )}
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
      {subscriptions.has_more_in_scope && (
        <p className="more">{intl.formatMessage(messages.more, { shown: subscriptions.items.length })}</p>
      )}
    </>
  );
}

/** What is shown, for the filter in force — counted after it, as the API counts (spec item m). */
function Declaration({
  subscriptions,
  clients,
  filter,
}: {
  subscriptions: ScopedList<SubscriptionItem>;
  clients: number;
  filter: StatusFilter;
}) {
  const intl = useIntl();
  if (subscriptions.scope_mode === 'all')
    return (
      <p className="scope" data-scope-mode="all" data-total={subscriptions.total_count ?? undefined}>
        {intl.formatMessage(messages.scopeAll, { status: filter, subscriptions: subscriptions.visible_count })}
      </p>
    );
  // 'assigned': no total figure, in the text or in the markup.
  return (
    <p className="scope" data-scope-mode="assigned">
      {subscriptions.visible_count === 0
        ? intl.formatMessage(messages.scopeAssignedEmpty, { status: filter })
        : intl.formatMessage(messages.scopeAssigned, { status: filter, clients, subscriptions: subscriptions.visible_count })}
    </p>
  );
}

/** How close an end date counts as "due soon", in days. TODO: the product's own threshold (a module setting). */
export const DUE_SOON_DAYS = 30;

export type SubscriptionStatus = 'expired' | 'dueSoon' | 'active';

/**
 * A subscription's status on `today` (both yyyy-MM-dd, compared as calendar days): ended before today → expired; ending
 * within DUE_SOON_DAYS, today included → due soon; otherwise active.
 */
export function subscriptionStatus(endsOn: string, today: string): SubscriptionStatus {
  const days = (Date.parse(`${endsOn}T00:00:00Z`) - Date.parse(`${today}T00:00:00Z`)) / 86_400_000;
  if (days < 0) return 'expired';
  if (days <= DUE_SOON_DAYS) return 'dueSoon';
  return 'active';
}

/** The user's calendar day now, yyyy-MM-dd (the day on their clock, not UTC's). */
export function localDay(now: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

const STATUS: Record<SubscriptionStatus, { tone: BadgeTone; label: typeof messages.statusActive }> = {
  expired: { tone: 'danger', label: messages.statusExpired },
  dueSoon: { tone: 'warning', label: messages.statusDueSoon },
  active: { tone: 'success', label: messages.statusActive },
};

function StatusBadge({ status }: { status: SubscriptionStatus }) {
  const intl = useIntl();
  const { tone, label } = STATUS[status];
  return (
    <Badge tone={tone}>
      <span data-status={status}>{intl.formatMessage(label)}</span>
    </Badge>
  );
}

const LIFECYCLE: Record<'cancelled' | 'renewed', { tone: BadgeTone; label: typeof messages.statusCancelled }> = {
  cancelled: { tone: 'neutral', label: messages.statusCancelled },
  renewed: { tone: 'neutral', label: messages.statusRenewed },
};

/**
 * One status column for two meanings. While a subscription is active its end date decides what the row says —
 * expired, due soon, active. Once it is cancelled or renewed the date says nothing worth saying: the lifecycle
 * does, and it wins.
 */
function RowStatus({ subscription, today }: { subscription: SubscriptionItem; today: string }) {
  const intl = useIntl();
  // Anything but the two the interface knows falls back to the date: a value the server gains later shows a row
  // that is merely less informative, never a screen that fails to render.
  const lifecycle = LIFECYCLE[subscription.status as 'cancelled' | 'renewed'] as (typeof LIFECYCLE)['cancelled'] | undefined;
  if (!lifecycle) return <StatusBadge status={subscriptionStatus(subscription.ends_on, today)} />;
  const { tone, label } = lifecycle;
  return (
    <Badge tone={tone}>
      <span data-status={subscription.status}>{intl.formatMessage(label)}</span>
    </Badge>
  );
}

/**
 * A calendar day (yyyy-MM-dd, no time, no zone) through Intl.DateTimeFormat in the current locale. Read and written in
 * UTC, so no time zone can move it to the day before or after.
 */
export function formatDay(formatDate: ReturnType<typeof useIntl>['formatDate'], day: string): string {
  return formatDate(new Date(`${day}T00:00:00Z`), { year: 'numeric', month: 'long', day: 'numeric', timeZone: 'UTC' });
}
