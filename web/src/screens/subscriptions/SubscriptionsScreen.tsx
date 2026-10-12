import { useEffect, useState } from 'react';
import { useIntl } from 'react-intl';
import { ApiError, getJson, isAbort } from '../../api/client';
import { errorMessage } from '../../i18n/apiErrors';
import { useSession } from '../../session/SessionProvider';
import { ForbiddenScreen } from '../status/StatusScreens';
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

/** The tenant can no longer be entered: the session asks /me again, and the interface returns to the selection. */
const TENANT_LOST = new Set(['not_a_member', 'no_active_tenant', 'tenant_not_active']);

/**
 * The subscription list (PROOF_SPEC T8), from GET /subscriptions and GET /subscriptions/clients: the scope declaration,
 * then the rows. The chrome is translated; what users entered — services and clients — is shown as is, in its own
 * direction (dir="auto").
 */
export function SubscriptionsScreen() {
  const [state, setState] = useState<State>({ status: 'loading' });
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
  }, [revalidate]);

  if (state.status === 'error' && state.code === 'not_permitted') return <ForbiddenScreen />;
  return <SubscriptionsView state={state} />;
}

export function SubscriptionsView({ state }: { state: State }) {
  const intl = useIntl();
  return (
    <main className="subscriptions">
      <header className="page-header">
        <span className="page-icon" aria-hidden="true">
          <svg viewBox="0 0 24 24" focusable="false">
            <path d="M4 6.5A2.5 2.5 0 0 1 6.5 4h11A2.5 2.5 0 0 1 20 6.5v11a2.5 2.5 0 0 1-2.5 2.5h-11A2.5 2.5 0 0 1 4 17.5z" />
            <path d="M8 9h8M8 12.5h8M8 16h5" />
          </svg>
        </span>
        <h1>{intl.formatMessage(messages.title)}</h1>
      </header>
      {state.status === 'loading' && (
        <div className="loading">
          <span className="spinner-border spinner-border-sm" aria-hidden="true" />
          <p>{intl.formatMessage(messages.loading)}</p>
        </div>
      )}
      {state.status === 'error' && (
        <p role="alert" className="alert alert-danger">
          {intl.formatMessage(errorMessage(state.code))}
        </p>
      )}
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
      <div className="card table-card">
        <div className="table-responsive">
          <table className="table table-hover align-middle mb-0">
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
                    <td className="service">
                      <bdi dir="auto">{s.service_name}</bdi>
                    </td>
                    <td>
                      {client === undefined ? (
                        <span className="client unavailable">{intl.formatMessage(messages.clientUnavailable)}</span>
                      ) : (
                        <bdi dir="auto" className="client">
                          {client}
                        </bdi>
                      )}
                    </td>
                    <td className="date">
                      <time dateTime={s.ends_on}>{formatDay(intl.formatDate, s.ends_on)}</time>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
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

/**
 * A calendar day (yyyy-MM-dd, no time, no zone) through Intl.DateTimeFormat in the current locale. Read and written in
 * UTC, so no time zone can move it to the day before or after.
 */
export function formatDay(formatDate: ReturnType<typeof useIntl>['formatDate'], day: string): string {
  return formatDate(new Date(`${day}T00:00:00Z`), { year: 'numeric', month: 'long', day: 'numeric', timeZone: 'UTC' });
}
