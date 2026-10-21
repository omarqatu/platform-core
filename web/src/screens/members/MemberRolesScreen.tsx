import { useEffect, useState } from 'react';
import { useIntl } from 'react-intl';
import { Link, useParams } from 'react-router';
import { ApiError, getJson, isAbort, request } from '../../api/client';
import { ErrorAlert } from '../../components/ErrorAlert';
import { errorMessage } from '../../i18n/apiErrors';
import { useLocale } from '../../i18n/I18nProvider';
import { ForbiddenScreen } from '../status/StatusScreens';
import type { ScopedList } from '../subscriptions/SubscriptionsScreen';
import { LIST_PATH } from './MembersScreen';
import { formMessages } from './formMessages';
import { roleName, type MemberItem, type RoleItem } from './types';
import './MembersScreen.css';

type State =
  | { status: 'loading' }
  | { status: 'error'; code: string | null }
  | { status: 'ready'; member: MemberItem; roles: RoleItem[] };

/**
 * A member's roles (PLATFORM_CORE §3.10 D5): one checkbox per role of the organization, each change its own
 * request — `POST /members/{id}/roles` or `DELETE …/{roleId}` — because that is what the API offers and a
 * "save everything" button would have to invent an order and a rollback the server does not have.
 *
 * Two refusals the server owns and this screen only reports: the last active owner cannot lose the owner role
 * (`last_owner`, item a), and no one changes their own roles (the write policies carry "not the actor's own
 * membership"). The list screen does not offer this page for the caller's own row at all.
 */
export function MemberRolesScreen() {
  const intl = useIntl();
  const { lang } = useLocale();
  const { membershipId = '' } = useParams();
  const [state, setState] = useState<State>({ status: 'loading' });
  const [refusal, setRefusal] = useState<string | null>(null);
  const [saving, setSaving] = useState<string | null>(null);
  const [reload, setReload] = useState(0);

  useEffect(() => {
    const abort = new AbortController();
    Promise.all([
      getJson<ScopedList<MemberItem>>('/members?limit=200', abort.signal),
      getJson<{ items: RoleItem[] }>('/roles', abort.signal),
    ]).then(
      ([members, catalogue]) => {
        const member = members.items.find((m) => m.membership_id === membershipId);
        if (!member) {
          setState({ status: 'error', code: 'not_permitted' });
          return;
        }
        setState({ status: 'ready', member, roles: catalogue.items });
      },
      (error: unknown) => {
        if (abort.signal.aborted || isAbort(error)) return;
        setState({ status: 'error', code: error instanceof ApiError ? error.code : null });
      },
    );
    return () => abort.abort();
  }, [membershipId, reload]);

  if (state.status === 'error' && state.code === 'not_permitted') return <ForbiddenScreen />;

  const toggle = async (role: RoleItem, held: boolean) => {
    setRefusal(null);
    setSaving(role.id);
    try {
      if (held) await request('DELETE', `/members/${membershipId}/roles/${role.id}`);
      else await request('POST', `/members/${membershipId}/roles`, { role_id: role.id });
      setReload((n) => n + 1);
    } catch (e) {
      if (isAbort(e)) return;
      setRefusal(e instanceof ApiError ? e.code : null);
    } finally {
      setSaving(null);
    }
  };

  return (
    <main className="member-form">
      {state.status === 'ready' && <h1>{intl.formatMessage(formMessages.rolesTitle, { member: state.member.full_name })}</h1>}
      <p className="lede">{intl.formatMessage(formMessages.rolesLede)}</p>
      <ErrorAlert message={refusal ? intl.formatMessage(errorMessage(refusal)) : null} />
      {state.status === 'loading' && <p>{intl.formatMessage(formMessages.loading)}</p>}
      {state.status === 'error' && <p role="alert">{intl.formatMessage(errorMessage(state.code))}</p>}
      {state.status === 'ready' && (
        <>
          {state.member.roles.length === 0 && <p className="field-note">{intl.formatMessage(formMessages.rolesNone)}</p>}
          <ul className="role-list">
            {state.roles.map((role) => {
              const held = state.member.roles.some((r) => r.id === role.id);
              return (
                <li key={role.id}>
                  <label>
                    <input
                      type="checkbox"
                      checked={held}
                      disabled={saving !== null}
                      data-role={role.code}
                      onChange={() => void toggle(role, held)}
                    />
                    <bdi dir="auto">{roleName(role, lang)}</bdi>
                  </label>
                </li>
              );
            })}
          </ul>
          {saving !== null && <p className="field-note">{intl.formatMessage(formMessages.saving)}</p>}
          <div className="form-actions">
            <Link to={LIST_PATH} className="btn">
              {intl.formatMessage(formMessages.done)}
            </Link>
          </div>
        </>
      )}
    </main>
  );
}
