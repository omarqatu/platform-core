import { useId, useState, useSyncExternalStore, type FormEvent } from 'react';
import { useIntl, type MessageDescriptor } from 'react-intl';
import { Link, useNavigate } from 'react-router';
import { ApiError, isAbort, request } from '../../api/client';
import { Brand } from '../../components/Brand';
import { ErrorAlert } from '../../components/ErrorAlert';
import { LanguageSwitcher } from '../../components/LanguageSwitcher';
import { errorMessage } from '../../i18n/apiErrors';
import {
  clearPendingResetToken,
  pendingResetToken,
  subscribePendingResetToken,
} from '../../password/pendingResetToken';
import { FORGOT_PATH } from './ForgotPasswordScreen';
import { messages } from './messages';

/**
 * Setting a password from a recovery link (Amendment 01 §5.1), the token from memory only
 * ({@link ../../password/pendingResetToken}) — it arrived in the fragment and left the address before this
 * rendered. POST /auth/password/reset, open, no session.
 *
 * The API answers `invalid_token` for a token that is unknown, already used, or expired, and the screen keeps
 * them one message: which of the three it was is not the user's business and is not ours to guess. No cookie is
 * issued on this path, so the end of it is the sign-in screen — and every session the user had, in this browser
 * or any other, has ended with the new security stamp (§2.1).
 *
 * The only rule checked here is that the two fields match. The platform has no password policy at all — the API
 * accepts 1 to 1024 bytes and the invitation-acceptance screen checks no more than `required` — and inventing one
 * in the interface is not this task's to invent (OPEN_ITEMS 62).
 */
export function ResetPasswordScreen() {
  // Another link opened here brings another token: a screen of its own, nothing kept from the previous one.
  const token = useSyncExternalStore(subscribePendingResetToken, pendingResetToken);
  return <Reset key={token ?? ''} token={token} />;
}

function Reset({ token }: { token: string | null }) {
  const intl = useIntl();
  const navigate = useNavigate();
  // No token in memory: the link was opened again after a reload, or was never a link of ours.
  const [invalid, setInvalid] = useState(token === null);
  const [form, setForm] = useState({ newPassword: '', confirmPassword: '' });
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const ids = { newPassword: useId(), confirmPassword: useId(), error: useId() };

  const onSubmit = async (event: FormEvent) => {
    event.preventDefault();
    if (!token) return;
    if (form.newPassword !== form.confirmPassword) {
      setForm((f) => ({ ...f, confirmPassword: '' }));
      setError(intl.formatMessage(messages.mismatch));
      return;
    }
    setPending(true);
    setError(null);
    try {
      await request<void>('POST', '/auth/password/reset', { token, new_password: form.newPassword });
      clearPendingResetToken();
      navigate('/login', { replace: true, state: { passwordSet: true } });
    } catch (e) {
      if (isAbort(e)) return;
      const code = e instanceof ApiError ? e.code : null;
      // The passwords stay in the fields only: never in a message or a log.
      setForm({ newPassword: '', confirmPassword: '' });
      setPending(false);
      if (code === 'invalid_token') {
        clearPendingResetToken();
        setInvalid(true);
      } else {
        setError(intl.formatMessage(errorMessage(code)));
      }
    }
  };

  const field = (name: keyof typeof form, label: MessageDescriptor) => (
    <p>
      <label htmlFor={ids[name]}>{intl.formatMessage(label)}</label>
      <input
        id={ids[name]}
        name={name}
        type="password"
        autoComplete="new-password"
        required
        value={form[name]}
        onChange={(e) => setForm((f) => ({ ...f, [name]: e.target.value }))}
        aria-describedby={error ? ids.error : undefined}
      />
    </p>
  );

  return (
    <main className="login">
      <div className="toolbar">
        <Brand />
        <LanguageSwitcher />
      </div>
      <h1>{intl.formatMessage(messages.resetTitle)}</h1>
      {invalid ? (
        <>
          <ErrorAlert message={intl.formatMessage(messages.resetInvalid)} />
          <p>
            <Link to={FORGOT_PATH}>{intl.formatMessage(messages.askAgain)}</Link>
          </p>
        </>
      ) : (
        <>
          <p>{intl.formatMessage(messages.resetIntro)}</p>
          <ErrorAlert message={error} id={ids.error} />
          <form onSubmit={onSubmit}>
            {field('newPassword', messages.newPassword)}
            {field('confirmPassword', messages.confirmPassword)}
            <button type="submit" disabled={pending}>
              {intl.formatMessage(pending ? messages.resetSubmitting : messages.resetSubmit)}
            </button>
          </form>
        </>
      )}
    </main>
  );
}
