import { useId, useState, useSyncExternalStore, type FormEvent } from 'react';
import { useIntl } from 'react-intl';
import { Link, useNavigate } from 'react-router';
import { ApiError, isAbort, request } from '../../api/client';
import { ErrorAlert } from '../../components/ErrorAlert';
import { messages as common } from '../../components/messages';
import { LanguageSwitcher } from '../../components/LanguageSwitcher';
import { apiErrors, errorMessage } from '../../i18n/apiErrors';
import {
  ACCEPT_PATH,
  clearPendingInvitation,
  pendingInvitation,
  subscribePendingInvitation,
  type PendingInvitation,
} from '../../invitations/pendingInvitation';
import { nextQuery } from '../../session/next';
import { useSession } from '../../session/SessionProvider';
import { LoadingScreen } from '../status/StatusScreens';
import { messages } from './messages';

/** A refusal the screen ends on: the form is gone, its message stays. */
type Ended = 'invalid' | 'email_mismatch' | 'already_member';

/** The codes of an invitation that cannot be accepted: one message for the three (unknown, expired, used). */
const INVALID = new Set(['invalid_invitation', 'invitation_expired']);

/**
 * Accepting an invitation (4.5, 3.10), the token from memory only (pendingInvitation). POST /invitations/accept:
 * - signed in: the existing account accepts — one button;
 * - not signed in: a new account is created with it — and an address that already has an account is refused
 *   (account_exists) with the way to sign in and come back here, the token still in memory.
 * Accepting enters no organization: afterwards, the selection — nothing chosen by itself.
 */
export function AcceptInvitationScreen() {
  // Another link opened here brings another invitation: a screen of its own, nothing kept from the previous one.
  const invitation = useSyncExternalStore(subscribePendingInvitation, pendingInvitation);
  return <Acceptance key={invitation?.token ?? ''} invitation={invitation} />;
}

function Acceptance({ invitation }: { invitation: PendingInvitation | null }) {
  const intl = useIntl();
  const navigate = useNavigate();
  const { state } = useSession();
  const [ended, setEnded] = useState<Ended | null>(invitation ? null : 'invalid');
  const [accountExists, setAccountExists] = useState(false);
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [form, setForm] = useState({ fullName: '', email: '', username: '', password: '' });
  const ids = { fullName: useId(), email: useId(), username: useId(), password: useId(), error: useId() };

  if (state.status === 'loading') return <LoadingScreen />;

  const signInHere = `/login${nextQuery(ACCEPT_PATH)}`;

  const accept = async (account?: typeof form) => {
    if (!invitation) return;
    setPending(true);
    setError(null);
    try {
      await request<void>('POST', '/invitations/accept', {
        tenant_id: invitation.tenantId,
        token: invitation.token,
        ...(account && { email: account.email, full_name: account.fullName, username: account.username, password: account.password }),
      });
      clearPendingInvitation();
      // To the selection, with nothing entered: signed in, straight there; a new account signs in first.
      if (account) navigate('/login', { replace: true, state: { accepted: true } });
      else navigate('/organizations', { replace: true });
    } catch (e) {
      if (isAbort(e)) return;
      const code = e instanceof ApiError ? e.code : null;
      setForm((f) => ({ ...f, password: '' }));
      setPending(false);
      if (code && INVALID.has(code)) {
        clearPendingInvitation();
        setEnded('invalid');
      } else if (code === 'already_member') {
        clearPendingInvitation();
        setEnded('already_member');
      } else if (code === 'email_mismatch') {
        setEnded('email_mismatch');
      } else if (code === 'account_exists') {
        setAccountExists(true);
      } else {
        setError(intl.formatMessage(errorMessage(code)));
      }
    }
  };

  const onSubmit = (event: FormEvent) => {
    event.preventDefault();
    void accept(form);
  };

  const field = (name: keyof typeof form, type: string, autoComplete: string) => (
    <p>
      <label htmlFor={ids[name]}>{intl.formatMessage(messages[name])}</label>
      <input
        id={ids[name]}
        name={name}
        type={type}
        autoComplete={autoComplete}
        required
        dir={type === 'password' ? undefined : 'auto'}
        value={form[name]}
        onChange={(e) => setForm((f) => ({ ...f, [name]: e.target.value }))}
        aria-describedby={error ? ids.error : undefined}
      />
    </p>
  );

  let body;
  if (ended)
    body = (
      <>
        <ErrorAlert
          message={intl.formatMessage(ended === 'invalid' ? messages.invalid : apiErrors[ended])}
        />
        {ended === 'already_member' && (
          <p>
            <Link to="/organizations">{intl.formatMessage(messages.toOrganizations)}</Link>
          </p>
        )}
      </>
    );
  else if (state.status === 'authenticated')
    body = (
      <>
        <p>
          {intl.formatMessage(common.signedInAs)} <bdi dir="auto">{state.username}</bdi>
        </p>
        <p>{intl.formatMessage(messages.signedInIntro)}</p>
        <ErrorAlert message={error} id={ids.error} />
        <button type="button" onClick={() => void accept()} disabled={pending}>
          {intl.formatMessage(pending ? messages.accepting : messages.accept)}
        </button>
      </>
    );
  else if (accountExists)
    body = (
      <>
        <ErrorAlert message={intl.formatMessage(apiErrors.account_exists)} />
        <p>
          <Link to={signInHere}>{intl.formatMessage(messages.signInToAccept)}</Link>
        </p>
      </>
    );
  else
    body = (
      <>
        <p>{intl.formatMessage(messages.newAccountIntro)}</p>
        <ErrorAlert message={error} id={ids.error} />
        <form onSubmit={onSubmit}>
          {field('fullName', 'text', 'name')}
          {field('email', 'email', 'email')}
          {field('username', 'text', 'username')}
          {field('password', 'password', 'new-password')}
          <button type="submit" disabled={pending}>
            {intl.formatMessage(pending ? messages.accepting : messages.createAndAccept)}
          </button>
        </form>
        <p>
          {intl.formatMessage(messages.haveAccount)} <Link to={signInHere}>{intl.formatMessage(messages.signInToAccept)}</Link>
        </p>
      </>
    );

  return (
    <main className="login invitation">
      <div className="toolbar">
        <LanguageSwitcher />
      </div>
      <h1>{intl.formatMessage(messages.title)}</h1>
      {body}
    </main>
  );
}
