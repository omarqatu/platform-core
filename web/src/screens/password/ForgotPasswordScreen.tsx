import { useId, useState, type FormEvent } from 'react';
import { useIntl, type MessageDescriptor } from 'react-intl';
import { Link } from 'react-router';
import { ApiError, isAbort, request } from '../../api/client';
import { Brand } from '../../components/Brand';
import { ErrorAlert } from '../../components/ErrorAlert';
import { LanguageSwitcher } from '../../components/LanguageSwitcher';
import { apiErrors, errorMessage } from '../../i18n/apiErrors';
import { messages } from './messages';

export const FORGOT_PATH = '/app/password/forgot';

/**
 * Asking for a recovery link (Amendment 01 §5.1): POST /auth/password/forgot, open, no session.
 *
 * **The answer is the same for every address.** The API answers 204 for an address that has an account, one that
 * does not, and one nobody could hold, and holds every answer to the same floor
 * (`Identity:ForgotPassword:MinimumResponseTime`) so the timing says nothing either. The screen must not undo
 * that: one panel, one wording, no redirect that depends on the answer, and **nothing timed here** — no delay, no
 * spinner held open, no retry counter. The only other answer is the ip ceiling's 429 (§5.2), which depends on the
 * caller's own address and so says nothing about anybody else's.
 */
export function ForgotPasswordScreen() {
  const intl = useIntl();
  const [email, setEmail] = useState('');
  const [pending, setPending] = useState(false);
  const [sent, setSent] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const ids = { email: useId(), error: useId() };

  const onSubmit = async (event: FormEvent) => {
    event.preventDefault();
    setPending(true);
    setError(null);
    try {
      await request<void>('POST', '/auth/password/forgot', { email });
      // The same panel whatever the address was, and the address is dropped with the form.
      setSent(true);
    } catch (e) {
      if (isAbort(e)) return;
      const code = e instanceof ApiError ? e.code : null;
      const message: MessageDescriptor = code === 'too_many_attempts' ? apiErrors.too_many_attempts : errorMessage(code);
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
      <h1>{intl.formatMessage(messages.forgotTitle)}</h1>
      {sent ? (
        <>
          <p role="status" className="notice">
            {intl.formatMessage(messages.forgotSent)}
          </p>
          <p>{intl.formatMessage(messages.forgotSentHint)}</p>
          <p>
            <Link to="/login">{intl.formatMessage(messages.backToSignIn)}</Link>
          </p>
        </>
      ) : (
        <>
          <p>{intl.formatMessage(messages.forgotIntro)}</p>
          <ErrorAlert message={error} id={ids.error} />
          <form onSubmit={onSubmit}>
            <p>
              <label htmlFor={ids.email}>{intl.formatMessage(messages.email)}</label>
              <input
                id={ids.email}
                name="email"
                type="email"
                autoComplete="email"
                required
                dir="auto"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                aria-describedby={error ? ids.error : undefined}
              />
            </p>
            <button type="submit" disabled={pending}>
              {intl.formatMessage(pending ? messages.forgotSubmitting : messages.forgotSubmit)}
            </button>
          </form>
          <p>
            <Link to="/login">{intl.formatMessage(messages.backToSignIn)}</Link>
          </p>
        </>
      )}
    </main>
  );
}
