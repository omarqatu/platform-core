import { useId, useState, type FormEvent } from 'react';
import { useIntl, type MessageDescriptor } from 'react-intl';
import { Link, Navigate, useLocation, useNavigate, useSearchParams } from 'react-router';
import { ApiError } from '../../api/client';
import { ErrorAlert } from '../../components/ErrorAlert';
import { Brand } from '../../components/Brand';
import { LanguageSwitcher } from '../../components/LanguageSwitcher';
import { apiErrors, errorMessage } from '../../i18n/apiErrors';
import { FORGOT_PATH } from '../password/ForgotPasswordScreen';
import { useStayed } from '../../navigation/useStayed';
import { RESET_PATH } from '../../password/pendingResetToken';
import { needsTenant, nextQuery, safeNext } from '../../session/next';
import { useSession } from '../../session/SessionProvider';
import { LoadingScreen } from '../status/StatusScreens';
import { messages } from './messages';

/** Codes that refuse the credentials: one message for all, never which part was wrong (T3.5). */
const REFUSED = new Set(['invalid_credentials', 'invalid_request']);
/** The pages of password recovery: signing in makes them moot, so a sign-in that succeeds leaves them. */
const RECOVERY = new Set([FORGOT_PATH, RESET_PATH]);

export function LoginScreen() {
  const intl = useIntl();
  const navigate = useNavigate();
  const begin = useStayed();
  const [params] = useSearchParams();
  const { state, signIn } = useSession();
  const location = useLocation();
  const next = safeNext(params.get('next'));
  // Back to a path that needs no organization (accepting an invitation): straight there, nothing entered.
  const direct = next !== null && !needsTenant(next);
  // An account just created by accepting an invitation: to the selection, but nothing entered by itself.
  const accepted = (location.state as { accepted?: boolean } | null)?.accepted === true;
  // A password just set from a recovery link: that path issues no cookie, so it ends here (Amendment 01 §5.1).
  const passwordSet = (location.state as { passwordSet?: boolean } | null)?.passwordSet === true;
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const ids = { username: useId(), password: useId(), error: useId() };

  if (state.status === 'loading') return <LoadingScreen />;
  if (state.status === 'authenticated' && !pending)
    return <Navigate to={direct && next ? next : state.activeTenant ? (next ?? '/') : `/organizations${nextQuery(next)}`} replace />;

  const onSubmit = async (event: FormEvent) => {
    event.preventDefault();
    setPending(true);
    setError(null);
    const stayed = begin();
    try {
      await signIn(username, password);
      // Where signing in leads, unless the person has gone somewhere else meanwhile (navigation/useStayed) — a Back
      // there is theirs. Not a recovery page, though: "Forgot your password?" tapped while the sign-in waits was a
      // belief the sign-in has just disproved, and a signed-in person left on it would be asked to reset a password
      // that works (DEVELOPER_GUIDE §15).
      if (!stayed() && !RECOVERY.has(window.location.pathname)) return;
      if (direct && next) navigate(next, { replace: true });
      // The selection enters by itself when there is exactly one organization to enter — not after an acceptance.
      else navigate(`/organizations${nextQuery(next)}`, { replace: true, state: { autoEnter: !accepted } });
    } catch (e) {
      const code = e instanceof ApiError ? e.code : null;
      // The password stays in the field only: never in a message or a log.
      setPassword('');
      const message: MessageDescriptor = code && REFUSED.has(code) ? apiErrors.invalid_credentials : errorMessage(code);
      setError(intl.formatMessage(message));
      setPending(false);
    }
  };

  return (
    <main className="login">
      <div className="toolbar">
        <Brand />
        <LanguageSwitcher />
      </div>
      <h1>{intl.formatMessage(messages.title)}</h1>
      {state.status === 'anonymous' && state.reason === 'expired' && !error && (
        <p role="status" className="notice">
          {intl.formatMessage(messages.sessionEnded)}
        </p>
      )}
      {passwordSet && !error && (
        <p role="status" className="notice">
          {intl.formatMessage(messages.passwordSet)}
        </p>
      )}
      {accepted && !error && (
        <p role="status" className="notice">
          {intl.formatMessage(messages.accountCreated)}
        </p>
      )}
      {direct && !accepted && !error && (
        <p role="status" className="notice">
          {intl.formatMessage(messages.toAcceptInvitation)}
        </p>
      )}
      <ErrorAlert message={error} id={ids.error} />
      <form onSubmit={onSubmit} noValidate={false}>
        <p>
          <label htmlFor={ids.username}>{intl.formatMessage(messages.username)}</label>
          <input
            id={ids.username}
            name="username"
            autoComplete="username"
            required
            dir="auto"
            value={username}
            onChange={(e) => setUsername(e.target.value)}
            aria-describedby={error ? ids.error : undefined}
          />
        </p>
        <p>
          <label htmlFor={ids.password}>{intl.formatMessage(messages.password)}</label>
          <input
            id={ids.password}
            name="password"
            type="password"
            autoComplete="current-password"
            required
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            aria-describedby={error ? ids.error : undefined}
          />
        </p>
        <button type="submit" disabled={pending}>
          {intl.formatMessage(pending ? messages.submitting : messages.submit)}
        </button>
      </form>
      <p className="recover">
        <Link to={FORGOT_PATH}>{intl.formatMessage(messages.forgotPassword)}</Link>
      </p>
    </main>
  );
}
