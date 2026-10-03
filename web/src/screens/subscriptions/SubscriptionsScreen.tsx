import { useEffect, useState } from 'react';
import { useIntl } from 'react-intl';
import { ApiError, getJson } from '../../api/client';
import { errorMessage } from '../../i18n/apiErrors';
import { messages } from './messages';
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
}

export interface ClientItem {
  id: string;
  name: string;
}

export interface SubscriptionsData {
  subscriptions: ScopedList<SubscriptionItem>;
  clients: ScopedList<ClientItem>;
}

export const PAGE_SIZE = 50;
const CLIENTS_LIMIT = 200; // the API's maximum page

type State = { status: 'loading' } | { status: 'error'; code: string | null } | { status: 'ready'; data: SubscriptionsData };

/**
 * The subscription list (PROOF_SPEC T8), from GET /subscriptions and GET /subscriptions/clients: the scope declaration,
 * then the rows. The chrome is translated; what users entered — services and clients — is shown as is, in its own
 * direction (dir="auto").
 */
export function SubscriptionsScreen() {
  const [state, setState] = useState<State>({ status: 'loading' });

  useEffect(() => {
    const abort = new AbortController();
    Promise.all([
      getJson<ScopedList<SubscriptionItem>>(`/subscriptions?limit=${PAGE_SIZE}`, abort.signal),
      getJson<ScopedList<ClientItem>>(`/subscriptions/clients?limit=${CLIENTS_LIMIT}`, abort.signal),
    ]).then(
      ([subscriptions, clients]) => setState({ status: 'ready', data: { subscriptions, clients } }),
      (error: unknown) => {
        if (!abort.signal.aborted) setState({ status: 'error', code: error instanceof ApiError ? error.code : null });
      },
    );
    return () => abort.abort();
  }, []);

  return <SubscriptionsView state={state} />;
}

export function SubscriptionsView({ state }: { state: State }) {
  const intl = useIntl();
  return (
    <main className="subscriptions">
      <h1>{intl.formatMessage(messages.title)}</h1>
      {state.status === 'loading' && <p>{intl.formatMessage(messages.loading)}</p>}
      {state.status === 'error' && <p role="alert">{intl.formatMessage(errorMessage(state.code))}</p>}
      {state.status === 'ready' && <SubscriptionsTable data={state.data} />}
    </main>
  );
}

function SubscriptionsTable({ data: { subscriptions, clients } }: { data: SubscriptionsData }) {
  const intl = useIntl();
  const names = new Map(clients.items.map((c) => [c.id, c.name]));
  return (
    <>
      <Declaration subscriptions={subscriptions} clients={clients.visible_count} />
      <table>
        <thead>
          <tr>
            <th scope="col">{intl.formatMessage(messages.columnService)}</th>
            <th scope="col">{intl.formatMessage(messages.columnClient)}</th>
            <th scope="col">{intl.formatMessage(messages.columnEndsOn)}</th>
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
              </tr>
            );
          })}
        </tbody>
      </table>
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

/**
 * A calendar day (yyyy-MM-dd, no time, no zone) through Intl.DateTimeFormat in the current locale. Read and written in
 * UTC, so no time zone can move it to the day before or after.
 */
export function formatDay(formatDate: ReturnType<typeof useIntl>['formatDate'], day: string): string {
  return formatDate(new Date(`${day}T00:00:00Z`), { year: 'numeric', month: 'long', day: 'numeric', timeZone: 'UTC' });
}
