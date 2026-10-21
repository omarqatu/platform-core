import { useCallback, useEffect, useState, type ReactNode } from 'react';
import { useIntl } from 'react-intl';
import { Link } from 'react-router';
import { ApiError, getJson, isAbort, request } from '../../api/client';
import { Badge } from '../../components/Badge';
import { ErrorAlert } from '../../components/ErrorAlert';
import { errorMessage } from '../../i18n/apiErrors';
import { useSession } from '../../session/SessionProvider';
import { ForbiddenScreen } from '../status/StatusScreens';
import { TENANT_LOST, type ScopedList } from '../subscriptions/SubscriptionsScreen';
import type { ClientItem } from './clientForm';
import { messages } from './messages';
import './ClientsScreen.css';

export const LIST_PATH = '/app/clients';
export const PAGE_SIZE = 200; // the API's maximum page

type State = { status: 'loading' } | { status: 'error'; code: string | null } | { status: 'ready'; data: ScopedList<ClientItem> };

/**
 * The client list (Amendment 01 §2.8, §5.1), from GET /subscriptions/clients: the scope declaration, then the rows.
 * A disabled client is listed with its badge and its way back — disabling is not deletion, and the interface never
 * hides what the server shows.
 */
export function ClientsScreen() {
  const { revalidate } = useSession();
  const [state, setState] = useState<State>({ status: 'loading' });
  const [refusal, setRefusal] = useState<string | null>(null);
  const [reload, setReload] = useState(0);

  useEffect(() => {
    const abort = new AbortController();
    getJson<ScopedList<ClientItem>>(`/subscriptions/clients?limit=${PAGE_SIZE}`, abort.signal).then(
      (data) => setState({ status: 'ready', data }),
      (error: unknown) => {
        if (abort.signal.aborted || isAbort(error)) return;
        const code = error instanceof ApiError ? error.code : null;
        if (code && TENANT_LOST.has(code)) revalidate().catch(() => undefined);
        setState({ status: 'error', code });
      },
    );
    return () => abort.abort();
  }, [revalidate, reload]);

  const onRefusal = useCallback(
    (code: string | null) => {
      if (code && TENANT_LOST.has(code)) revalidate().catch(() => undefined);
    },
    [revalidate],
  );

  if (state.status === 'error' && state.code === 'not_permitted') return <ForbiddenScreen />;
  return (
    <ClientsView
      state={state}
      refusal={refusal}
      actions={{
        create: () => <NewLink />,
        edit: (client) => <EditLink client={client} />,
        setStatus: async (client, isActive) => {
          setRefusal(null);
          try {
            await request('PUT', `/subscriptions/clients/${client.id}/status`, { is_active: isActive });
            setReload((n) => n + 1);
          } catch (e) {
            if (isAbort(e)) return;
            const code = e instanceof ApiError ? e.code : null;
            setRefusal(code);
            onRefusal(code);
          }
        },
      }}
    />
  );
}

/**
 * The list's ways to act. Shown to every member who can list, because the interface does not know the caller's
 * permissions (OPEN_ITEMS 77): an operator sees "Disable" and the server refuses it with not_permitted. The server
 * is the guard; this is a courtesy gap, not a security one.
 */
export interface ListActions {
  create: () => ReactNode;
  edit: (client: ClientItem) => ReactNode;
  setStatus: (client: ClientItem, isActive: boolean) => void | Promise<void>;
}

function NewLink() {
  const intl = useIntl();
  return (
    <Link to={`${LIST_PATH}/new`} className="btn">
      {intl.formatMessage(messages.newClient)}
    </Link>
  );
}

function EditLink({ client }: { client: ClientItem }) {
  const intl = useIntl();
  return (
    <Link to={`${LIST_PATH}/${client.id}/edit`} aria-label={intl.formatMessage(messages.editLabel, { client: client.name })}>
      {intl.formatMessage(messages.edit)}
    </Link>
  );
}

export function ClientsView({
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
    <main className="clients">
      <div className="heading">
        <h1>{intl.formatMessage(messages.title)}</h1>
        {state.status === 'ready' && actions?.create()}
      </div>
      <ErrorAlert message={refusal ? intl.formatMessage(errorMessage(refusal)) : null} />
      {state.status === 'loading' && <p>{intl.formatMessage(messages.loading)}</p>}
      {state.status === 'error' && <p role="alert">{intl.formatMessage(errorMessage(state.code))}</p>}
      {state.status === 'ready' && <ClientsTable clients={state.data} actions={actions} />}
    </main>
  );
}

function ClientsTable({ clients, actions }: { clients: ScopedList<ClientItem>; actions?: ListActions }) {
  const intl = useIntl();
  return (
    <>
      <Declaration clients={clients} />
      <div className="data-table">
        <table>
          <thead>
            <tr>
              <th scope="col">{intl.formatMessage(messages.columnName)}</th>
              <th scope="col">{intl.formatMessage(messages.columnStatus)}</th>
              {actions && (
                <th scope="col" className="actions">
                  {intl.formatMessage(messages.columnActions)}
                </th>
              )}
            </tr>
          </thead>
          <tbody>
            {clients.items.map((client) => (
              <tr key={client.id} data-client={client.id}>
                <td>
                  <bdi dir="auto">{client.name}</bdi>
                </td>
                <td>
                  <Badge tone={client.is_active ? 'success' : 'neutral'}>
                    <span data-status={client.is_active ? 'active' : 'disabled'}>
                      {intl.formatMessage(client.is_active ? messages.statusActive : messages.statusDisabled)}
                    </span>
                  </Badge>
                </td>
                {actions && (
                  <td className="actions">
                    <div className="row-actions">
                      {actions.edit(client)}
                      <StatusButton client={client} actions={actions} />
                    </div>
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {clients.has_more_in_scope && <p className="more">{intl.formatMessage(messages.more, { shown: clients.items.length })}</p>}
    </>
  );
}

/**
 * Disabling a client, and enabling it again. Disabling asks first and says what it does — it stops the reminders
 * and changes nothing about the subscriptions; enabling resumes them, with no catch-up for what was missed, so it
 * asks nothing.
 */
function StatusButton({ client, actions }: { client: ClientItem; actions: ListActions }) {
  const intl = useIntl();
  const [pending, setPending] = useState(false);
  const act = async () => {
    if (client.is_active && !globalThis.confirm(intl.formatMessage(messages.confirmDisable, { client: client.name }))) return;
    setPending(true);
    await actions.setStatus(client, !client.is_active);
    setPending(false);
  };
  return (
    <button
      type="button"
      className="link"
      disabled={pending}
      aria-label={intl.formatMessage(client.is_active ? messages.disableLabel : messages.enableLabel, { client: client.name })}
      onClick={act}
    >
      {intl.formatMessage(pending ? messages.working : client.is_active ? messages.disable : messages.enable)}
    </button>
  );
}

function Declaration({ clients }: { clients: ScopedList<ClientItem> }) {
  const intl = useIntl();
  if (clients.scope_mode === 'all')
    return (
      <p className="scope" data-scope-mode="all" data-total={clients.total_count ?? undefined}>
        {intl.formatMessage(messages.scopeAll, { clients: clients.visible_count })}
      </p>
    );
  // 'assigned': no total figure, in the text or in the markup.
  return (
    <p className="scope" data-scope-mode="assigned">
      {clients.visible_count === 0
        ? intl.formatMessage(messages.scopeAssignedEmpty)
        : intl.formatMessage(messages.scopeAssigned, { clients: clients.visible_count })}
    </p>
  );
}
