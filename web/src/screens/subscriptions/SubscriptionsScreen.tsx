import { useEffect, useState, type ReactNode } from 'react';
import { useIntl } from 'react-intl';
import { Link } from 'react-router';
import { ApiError, getJson, isAbort, request } from '../../api/client';
import { Badge, type BadgeTone } from '../../components/Badge';
import { ErrorAlert } from '../../components/ErrorAlert';
import { errorMessage } from '../../i18n/apiErrors';
import { useSession } from '../../session/SessionProvider';
import { ForbiddenScreen } from '../status/StatusScreens';
import { messages as form } from './formMessages';
import type { ClientItem } from '../clients/clientForm';
import { messages } from './messages';
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

/** The tenant can no longer be entered: the session asks /me again, and the interface returns to the selection. */
export const TENANT_LOST = new Set(['not_a_member', 'no_active_tenant', 'tenant_not_active']);

/**
 * The subscription list (PROOF_SPEC T8), from GET /subscriptions and GET /subscriptions/clients: the scope declaration,
 * then the rows. The chrome is translated; what users entered — services and clients — is shown as is, in its own
 * direction (dir="auto").
 */
export function SubscriptionsScreen() {
  const [state, setState] = useState<State>({ status: 'loading' });
  const [refusal, setRefusal] = useState<string | null>(null);
  const [reload, setReload] = useState(0);
  const { revalidate } = useSession();

  useEffect(() => {
    const abort = new AbortController();
    Promise.all([
      getJson<ScopedList<SubscriptionItem>>(`/subscriptions?limit=${PAGE_SIZE}`, abort.signal),
      getJson<ScopedList<ClientItem>>(`/subscriptions/clients?limit=${CLIENTS_LIMIT}`, abort.signal),
    ]).then(
      ([subscriptions, clients]) => setState({ status: 'ready', data: { subscriptions, clients } }),
      (error: unknown) => {
        if (abort.signal.aborted || isAbort(error)) return;
        const code = error instanceof ApiError ? error.code : null;
        if (code && TENANT_LOST.has(code)) revalidate().catch(() => undefined);
        setState({ status: 'error', code });
      },
    );
    return () => abort.abort();
  }, [revalidate, reload]);

  if (state.status === 'error' && state.code === 'not_permitted') return <ForbiddenScreen />;
  return (
    <SubscriptionsView
      state={state}
      refusal={refusal}
      actions={{
        create: () => <NewLink />,
        edit: (subscription) => <EditLink subscription={subscription} />,
        renew: (subscription) => <RenewLink subscription={subscription} />,
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
      }}
    />
  );
}

/**
 * The list's ways to the form (Amendment 02 §3.2): creating, and editing a row. The routed screen supplies them; the
 * view alone renders none, so it stands without a router. Shown to every member who lists: the interface does not
 * know the caller's permissions, and a viewer's attempt is refused by the server with not_permitted.
 */
export interface ListActions {
  create: () => ReactNode;
  edit: (subscription: SubscriptionItem) => ReactNode;
  renew: (subscription: SubscriptionItem) => ReactNode;
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

function RenewLink({ subscription }: { subscription: SubscriptionItem }) {
  const intl = useIntl();
  return (
    <Link
      to={`${LIST_PATH}/${subscription.id}/renew`}
      aria-label={intl.formatMessage(form.renewLabel, { service: subscription.service_name })}
    >
      {intl.formatMessage(form.renew)}
    </Link>
  );
}

/**
 * Cancelling from the list: a status change, never a delete (Amendment 01 §2.9). It asks first, because the row
 * stops being editable afterwards — the database's own gate sees to that.
 */
function CancelButton({ subscription, cancel }: { subscription: SubscriptionItem; cancel: ListActions['cancel'] }) {
  const intl = useIntl();
  const [pending, setPending] = useState(false);
  const act = async () => {
    if (!globalThis.confirm(intl.formatMessage(form.confirmCancel, { service: subscription.service_name }))) return;
    setPending(true);
    await cancel(subscription);
    setPending(false);
  };
  return (
    <button
      type="button"
      className="link"
      disabled={pending}
      aria-label={intl.formatMessage(form.cancelLabel, { service: subscription.service_name })}
      onClick={act}
    >
      {intl.formatMessage(pending ? form.working : form.cancelSubscription)}
    </button>
  );
}

function EditLink({ subscription }: { subscription: SubscriptionItem }) {
  const intl = useIntl();
  return (
    <Link
      to={`${LIST_PATH}/${subscription.id}/edit`}
      aria-label={intl.formatMessage(form.editLabel, { service: subscription.service_name })}
    >
      {intl.formatMessage(form.edit)}
    </Link>
  );
}



export function SubscriptionsView({
  state,
  actions,
  refusal,
}: {
  state: State;
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
      <ErrorAlert message={refusal ? intl.formatMessage(errorMessage(refusal)) : null} />
      {state.status === 'loading' && <p>{intl.formatMessage(messages.loading)}</p>}
      {state.status === 'error' && <p role="alert">{intl.formatMessage(errorMessage(state.code))}</p>}
      {state.status === 'ready' && <SubscriptionsTable data={state.data} actions={actions} />}
    </main>
  );
}

function SubscriptionsTable({ data: { subscriptions, clients }, actions }: { data: SubscriptionsData; actions?: ListActions }) {
  const intl = useIntl();
  const names = new Map(clients.items.map((c) => [c.id, c.name]));
  const today = localDay(new Date());
  return (
    <>
      <Declaration subscriptions={subscriptions} clients={clients.visible_count} />
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
                        {/* A cancelled or renewed subscription is not edited, renewed or cancelled: the database's own
                            gate refuses the write, so the screen offers nothing it knows will be refused. */}
                        {s.status === 'active' && (
                          <>
                            {actions.edit(s)}
                            {actions.renew(s)}
                            <CancelButton subscription={s} cancel={actions.cancel} />
                          </>
                        )}
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

function Declaration({ subscriptions, clients }: { subscriptions: ScopedList<SubscriptionItem>; clients: number }) {
  const intl = useIntl();
  if (subscriptions.scope_mode === 'all')
    return (
      <p className="scope" data-scope-mode="all" data-total={subscriptions.total_count ?? undefined}>
        {intl.formatMessage(messages.scopeAll, { subscriptions: subscriptions.visible_count })}
      </p>
    );
  // 'assigned': no total figure, in the text or in the markup.
  return (
    <p className="scope" data-scope-mode="assigned">
      {subscriptions.visible_count === 0
        ? intl.formatMessage(messages.scopeAssignedEmpty)
        : intl.formatMessage(messages.scopeAssigned, { clients, subscriptions: subscriptions.visible_count })}
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
