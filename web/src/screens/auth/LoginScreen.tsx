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
    <main className="login-page">
      <div className="login-shell">
        <aside className="login-brand">
          <BrandMark />
          <p className="login-brand-name">{intl.formatMessage(messages.brandName)}</p>
          <p className="login-brand-tagline">{intl.formatMessage(messages.brandTagline)}</p>
        </aside>
        <section className="login-panel">
          <div className="login-toolbar">
            <LanguageSwitcher />
          </div>
          <h1 className="login-title">{intl.formatMessage(messages.title)}</h1>
          <p className="login-subtitle">{intl.formatMessage(messages.subtitle)}</p>
          {state.status === 'anonymous' && state.reason === 'expired' && !error && (
            <p role="status" className="alert alert-warning notice">
              {intl.formatMessage(messages.sessionEnded)}
            </p>
          )}
          {accepted && !error && (
            <p role="status" className="alert alert-success notice">
              {intl.formatMessage(messages.accountCreated)}
            </p>
          )}
          {direct && !accepted && !error && (
            <p role="status" className="alert alert-info notice">
              {intl.formatMessage(messages.toAcceptInvitation)}
            </p>
          )}
          <ErrorAlert message={error} id={ids.error} />
          <form onSubmit={onSubmit} noValidate={false}>
            <div className="form-floating mb-3">
              <input
                id={ids.username}
                className="form-control"
                name="username"
                autoComplete="username"
                required
                dir="auto"
                placeholder={intl.formatMessage(messages.username)}
                value={username}
                onChange={(e) => setUsername(e.target.value)}
                aria-describedby={error ? ids.error : undefined}
              />
              <label htmlFor={ids.username}>{intl.formatMessage(messages.username)}</label>
            </div>
            <div className="form-floating mb-4">
              <input
                id={ids.password}
                className="form-control"
                name="password"
                type="password"
                autoComplete="current-password"
                required
                placeholder={intl.formatMessage(messages.password)}
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                aria-describedby={error ? ids.error : undefined}
              />
              <label htmlFor={ids.password}>{intl.formatMessage(messages.password)}</label>
            </div>
            <button type="submit" className="btn btn-primary btn-lg w-100 login-submit" disabled={pending}>
              {pending && <span className="spinner-border spinner-border-sm me-2" aria-hidden="true" />}
              {intl.formatMessage(pending ? messages.submitting : messages.submit)}
            </button>
          </form>
        </section>
      </div>
    </main>
  );
}

/** The product's mark: decoration only, the name beside it is the text. */
function BrandMark() {
  return (
    <svg className="login-brand-mark" viewBox="0 0 48 48" aria-hidden="true" focusable="false">
      <rect x="4" y="4" width="18" height="18" rx="5" fill="currentColor" opacity="0.95" />
      <rect x="26" y="4" width="18" height="18" rx="9" fill="currentColor" opacity="0.6" />
      <rect x="4" y="26" width="18" height="18" rx="9" fill="currentColor" opacity="0.6" />
      <rect x="26" y="26" width="18" height="18" rx="5" fill="currentColor" opacity="0.95" />
    </svg>
  );
}
