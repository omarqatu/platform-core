import { useCallback, useEffect, useState, type ReactNode } from 'react';
import { useIntl } from 'react-intl';
import { Link } from 'react-router';
import { ApiError, getJson, isAbort, request } from '../../api/client';
import { Badge } from '../../components/Badge';
import { ErrorAlert } from '../../components/ErrorAlert';
import { RowActions, type RowAction } from '../../components/RowActions';
import { errorMessage } from '../../i18n/apiErrors';
import { getPermissions, PERMISSION } from '../../session/permissions';
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
 * hides what the server shows. The caller's permissions (GET /me/scope) come in the same load, so nothing is shown
 * until both have answered and a failure of either is the screen's one error path.
 */
export function ClientsScreen() {
  const { revalidate } = useSession();
  const [state, setState] = useState<State>({ status: 'loading' });
  const [held, setHeld] = useState<{ write: boolean; manage: boolean }>({ write: false, manage: false });
  const [refusal, setRefusal] = useState<string | null>(null);
  const [reload, setReload] = useState(0);

  useEffect(() => {
    const abort = new AbortController();
    Promise.all([
      getJson<ScopedList<ClientItem>>(`/subscriptions/clients?limit=${PAGE_SIZE}`, abort.signal),
      getPermissions(abort.signal),
    ]).then(
      ([data, permissions]) => {
        setHeld({ write: permissions.has(PERMISSION.clientsWrite), manage: permissions.has(PERMISSION.clientsManage) });
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

  const onRefusal = useCallback(
    (code: string | null) => {
      if (code && TENANT_LOST.has(code)) revalidate().catch(() => undefined);
    },
    [revalidate],
  );

  const setStatus = async (client: ClientItem, isActive: boolean) => {
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
  };

  if (state.status === 'error' && state.code === 'not_permitted') return <ForbiddenScreen />;
  return (
    <ClientsView
      state={state}
      refusal={refusal}
      // Each way to act is shown only with its own permission (Amendment 01 §4.2, OPEN_ITEMS 77): creating and
      // editing with subscriptions.clients.write (owner, admin, operator), disabling and enabling with
      // subscriptions.clients.manage (owner and admin alone). Neither: no actions column. The server stays the guard
      // — a role changed elsewhere shows on the next load.
      actions={
        held.write || held.manage
          ? { create: held.write ? () => <NewLink /> : undefined, edit: held.write, setStatus: held.manage ? setStatus : undefined }
          : undefined
      }
    />
  );
}

/**
 * The list's ways to act, each present only when the caller holds its permission: creating and editing
 * (subscriptions.clients.write), disabling and enabling (subscriptions.clients.manage). Given at all only when the
 * caller holds one of the two.
 */
export interface ListActions {
  create?: () => ReactNode;
  edit: boolean;
  setStatus?: (client: ClientItem, isActive: boolean) => void | Promise<void>;
}

function NewLink() {
  const intl = useIntl();
  return (
    <Link to={`${LIST_PATH}/new`} className="btn">
      {intl.formatMessage(messages.newClient)}
    </Link>
  );
}

/**
 * A client's actions, as one RowActions menu: edit, and disable or enable it again. Disabling is the destructive one —
 * it stops the reminders — so it comes last, apart, and asks first, saying what it does not change; enabling resumes
 * them with no catch-up for what was missed, so it asks nothing. While the request runs the row says it is working.
 */
function ClientRowActions({ client, actions }: { client: ClientItem; actions: ListActions }) {
  const intl = useIntl();
  const [pending, setPending] = useState(false);
  if (pending) return <span className="row-note">{intl.formatMessage(messages.working)}</span>;

  const name = client.name;
  const change = async (isActive: boolean) => {
    if (!isActive && !globalThis.confirm(intl.formatMessage(messages.confirmDisable, { client: name }))) return;
    setPending(true);
    await actions.setStatus?.(client, isActive);
    setPending(false);
  };
  const items: RowAction[] = [];
  if (actions.edit)
    items.push({
      key: 'edit',
      label: intl.formatMessage(messages.edit),
      accessibleName: intl.formatMessage(messages.editLabel, { client: name }),
      icon: 'edit',
      to: `${LIST_PATH}/${client.id}/edit`,
    });
  if (actions.setStatus)
    items.push(
      client.is_active
        ? {
            key: 'disable',
            label: intl.formatMessage(messages.disable),
            accessibleName: intl.formatMessage(messages.disableLabel, { client: name }),
            icon: 'pause',
            danger: true,
            onSelect: () => void change(false),
          }
        : {
            key: 'enable',
            label: intl.formatMessage(messages.enable),
            accessibleName: intl.formatMessage(messages.enableLabel, { client: name }),
            icon: 'play',
            onSelect: () => void change(true),
          },
    );
  return <RowActions label={intl.formatMessage(messages.actionsFor, { client: name })} actions={items} />;
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
        {state.status === 'ready' && actions?.create?.()}
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
                      <ClientRowActions client={client} actions={actions} />
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
