import { useId, useState, type FormEvent } from 'react';
import { useIntl, type MessageDescriptor } from 'react-intl';
import { Navigate, useLocation, useNavigate, useSearchParams } from 'react-router';
import { ApiError } from '../../api/client';
import { ErrorAlert } from '../../components/ErrorAlert';
import { LanguageSwitcher } from '../../components/LanguageSwitcher';
import { apiErrors, errorMessage } from '../../i18n/apiErrors';
import { needsTenant, nextQuery, safeNext } from '../../session/next';
import { useSession } from '../../session/SessionProvider';
import { LoadingScreen } from '../status/StatusScreens';
import { messages } from './messages';

/** Codes that refuse the credentials: one message for all, never which part was wrong (T3.5). */
const REFUSED = new Set(['invalid_credentials', 'invalid_request']);

export function LoginScreen() {
  const intl = useIntl();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const { state, signIn } = useSession();
  const location = useLocation();
  const next = safeNext(params.get('next'));
  // Back to a path that needs no organization (accepting an invitation): straight there, nothing entered.
  const direct = next !== null && !needsTenant(next);
  // An account just created by accepting an invitation: to the selection, but nothing entered by itself.
  const accepted = (location.state as { accepted?: boolean } | null)?.accepted === true;
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
    try {
      await signIn(username, password);
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
        <LanguageSwitcher />
      </div>
      <h1>{intl.formatMessage(messages.title)}</h1>
      {state.status === 'anonymous' && state.reason === 'expired' && !error && (
        <p role="status" className="notice">
          {intl.formatMessage(messages.sessionEnded)}
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
    </main>
  );
}
