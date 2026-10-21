import { useEffect, useState, type FormEvent } from 'react';
import { useIntl } from 'react-intl';
import { Link, useNavigate } from 'react-router';
import { ApiError, getJson, isAbort, request } from '../../api/client';
import { ErrorAlert } from '../../components/ErrorAlert';
import { errorMessage } from '../../i18n/apiErrors';
import { useLocale } from '../../i18n/I18nProvider';
import { useStayed } from '../../navigation/useStayed';
import { getPermissions, PERMISSION } from '../../session/permissions';
import { useAuthenticated } from '../../session/SessionProvider';
import { ForbiddenScreen } from '../status/StatusScreens';
import { LIST_PATH } from './MembersScreen';
import { formMessages } from './formMessages';
import { roleName, type RoleItem } from './types';

/** What POST /members/invitations answers: the invitation, and its token — once. */
interface Created {
  invitation_id: string;
  token: string;
  expires_at: string;
}

type Load = { status: 'loading' } | { status: 'error'; code: string | null } | { status: 'ready'; roles: RoleItem[]; canScope: boolean };

/**
 * Inviting a member (PLATFORM_CORE §4.5/1, §3.10 D6). The address, the role, and the scope the invitation
 * carries — `membership_scope` is written at acceptance with the mode the inviter approved, never a default
 * (§4.8).
 *
 * **An invitation is never a membership.** Nothing here creates one: the person accepts, and that acceptance is
 * their consent (§0). The screen says so, because "invite" reads like "add" to everyone who has used other
 * software.
 *
 * "All clients" is offered only to whoever holds `core.scope.manage`: `MemberAdministration.InviteAsync` refuses
 * it otherwise, and `invitations_insert` refuses it beneath that. Shown disabled with its reason rather than
 * hidden — unlike an action, a missing radio option would make the form look like it has only one answer.
 */
export function InviteMemberScreen() {
  const intl = useIntl();
  const { lang } = useLocale();
  const navigate = useNavigate();
  const begin = useStayed();
  const [load, setLoad] = useState<Load>({ status: 'loading' });
  const [email, setEmail] = useState('');
  const [roleId, setRoleId] = useState('');
  const [mode, setMode] = useState<'all' | 'assigned'>('assigned');
  const [sending, setSending] = useState(false);
  const [refusal, setRefusal] = useState<string | null>(null);
  const [created, setCreated] = useState<Created | null>(null);

  useEffect(() => {
    const abort = new AbortController();
    Promise.all([getJson<{ items: RoleItem[] }>('/roles', abort.signal), getPermissions(abort.signal)]).then(
      ([catalogue, permissions]) => {
        setLoad({ status: 'ready', roles: catalogue.items, canScope: permissions.has(PERMISSION.scopeManage) });
      },
      (error: unknown) => {
        if (abort.signal.aborted || isAbort(error)) return;
        setLoad({ status: 'error', code: error instanceof ApiError ? error.code : null });
      },
    );
    return () => abort.abort();
  }, []);

  if (load.status === 'error' && load.code === 'not_permitted') return <ForbiddenScreen />;

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (load.status !== 'ready') return;
    setRefusal(null);
    setSending(true);
    const stayed = begin();
    try {
      const body = await request<Created>('POST', '/members/invitations', {
        email: email.trim(),
        role_id: roleId,
        intended_scope_mode: mode,
      });
      if (stayed()) setCreated(body);
    } catch (e) {
      if (isAbort(e)) return;
      setRefusal(e instanceof ApiError ? e.code : null);
    } finally {
      setSending(false);
    }
  };

  if (created) return <Sent created={created} email={email} onDone={() => navigate(LIST_PATH)} />;

  return (
    <main className="member-form">
      <h1>{intl.formatMessage(formMessages.inviteTitle)}</h1>
      <p className="lede">{intl.formatMessage(formMessages.inviteLede)}</p>
      <ErrorAlert message={refusal ? intl.formatMessage(errorMessage(refusal)) : null} />
      {load.status === 'loading' && <p>{intl.formatMessage(formMessages.loading)}</p>}
      {load.status === 'error' && <p role="alert">{intl.formatMessage(errorMessage(load.code))}</p>}
      {load.status === 'ready' && (
        <form onSubmit={submit} noValidate>
          <label htmlFor="invite-email">{intl.formatMessage(formMessages.email)}</label>
          <input
            id="invite-email"
            type="email"
            inputMode="email"
            dir="ltr"
            required
            value={email}
            onChange={(e) => setEmail(e.target.value)}
          />

          <label htmlFor="invite-role">{intl.formatMessage(formMessages.role)}</label>
          <select id="invite-role" required value={roleId} onChange={(e) => setRoleId(e.target.value)}>
            <option value="" disabled>
              {intl.formatMessage(formMessages.chooseRole)}
            </option>
            {load.roles.map((role) => (
              <option key={role.id} value={role.id}>
                {roleName(role, lang)}
              </option>
            ))}
          </select>

          <fieldset className="scope-choice">
            <legend>{intl.formatMessage(formMessages.scope)}</legend>
            <label>
              <input type="radio" name="scope" value="assigned" checked={mode === 'assigned'} onChange={() => setMode('assigned')} />
              <span>{intl.formatMessage(formMessages.scopeAssigned)}</span>
            </label>
            <label className={load.canScope ? undefined : 'disabled'}>
              <input
                type="radio"
                name="scope"
                value="all"
                checked={mode === 'all'}
                disabled={!load.canScope}
                aria-describedby={load.canScope ? undefined : 'invite-scope-all-reason'}
                onChange={() => setMode('all')}
              />
              <span>{intl.formatMessage(formMessages.scopeAll)}</span>
            </label>
            {!load.canScope && (
              <p id="invite-scope-all-reason" className="field-note">
                {intl.formatMessage(formMessages.scopeAllNeedsPermission)}
              </p>
            )}
            <p className="field-note">{intl.formatMessage(formMessages.scopeNote)}</p>
          </fieldset>

          <div className="form-actions">
            <button type="submit" className="btn" disabled={sending || email.trim() === '' || roleId === ''}>
              {intl.formatMessage(sending ? formMessages.sending : formMessages.send)}
            </button>
            <Link to={LIST_PATH} className="btn secondary">
              {intl.formatMessage(formMessages.cancel)}
            </Link>
          </div>
        </form>
      )}
    </main>
  );
}

/**
 * What the invitation was, once it exists — and the link, **shown exactly once and kept nowhere**: not in a
 * store, not in a cache, not in the address. It is here only because there is no way to re-issue an invitation
 * yet (OPEN_ITEMS 101) and the e-mail is handed to a dispatcher that drops a failed delivery with a log line. The
 * day re-issuing exists, this panel loses the link and the item says so.
 */
function Sent({ created, email, onDone }: { created: Created; email: string; onDone: () => void }) {
  const intl = useIntl();
  const { activeTenant } = useAuthenticated();
  const [copied, setCopied] = useState(false);
  // The acceptance screen's own shape (DEVELOPER_GUIDE §9): the tenant and the token both in the FRAGMENT, which
  // no server log and no Referer header ever sees.
  const link = `${globalThis.location.origin}/app/invitations/accept#tenant=${encodeURIComponent(
    activeTenant?.tenant_id ?? '',
  )}&token=${encodeURIComponent(created.token)}`;
  return (
    <main className="member-form">
      <h1>{intl.formatMessage(formMessages.sentTitle)}</h1>
      <p>{intl.formatMessage(formMessages.sentBody, { email })}</p>
      <p className="field-note">{intl.formatMessage(formMessages.sentLinkNote)}</p>
      <div className="form-actions">
        <button
          type="button"
          className="btn secondary"
          onClick={() => {
            void navigator.clipboard?.writeText(link).then(
              () => setCopied(true),
              () => undefined,
            );
          }}
        >
          {intl.formatMessage(copied ? formMessages.copied : formMessages.copyLink)}
        </button>
        <button type="button" className="btn" onClick={onDone}>
          {intl.formatMessage(formMessages.backToMembers)}
        </button>
      </div>
    </main>
  );
}
