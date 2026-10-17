import { useId, useState, type FormEvent } from 'react';
import { useIntl, type MessageDescriptor } from 'react-intl';
import { ApiError, isAbort, request } from '../../api/client';
import { ErrorAlert } from '../../components/ErrorAlert';
import { messages as common } from '../../components/messages';
import { apiErrors, errorMessage } from '../../i18n/apiErrors';
import { isLongEnough } from '../../password/policy';
import { messages } from './messages';
import './PasswordScreens.css';

export const CHANGE_PATH = '/app/password/change';

/**
 * Changing one's own password while signed in (Amendment 01 §5.1): PUT /me/password, the current password and the
 * new one. The screen needs a session but no organization — the endpoint runs its own units of work with
 * `app.user_id` alone — so it lives under RequireAuth and outside RequireTenant.
 *
 * On success the server writes a new security stamp and re-issues **this** session's cookie with it, which ends
 * every other session of the same user (§2.1). People expect to be signed out here and worry when they are not,
 * so the screen says both halves of that in one line (`changeOtherSessions`).
 *
 * A wrong current password comes back as 401 `invalid_credentials`. That is not an ended session, and
 * `api/client.ts` keeps it away from the session's 401 handler for exactly this screen's sake; a 401
 * `session_expired` on the same request still ends the session and lands on the sign-in screen. The status code
 * is the backend's to correct (OPEN_ITEMS 64).
 */
export function ChangePasswordScreen() {
  const intl = useIntl();
  const [form, setForm] = useState({ currentPassword: '', newPassword: '', confirmPassword: '' });
  const [pending, setPending] = useState(false);
  const [done, setDone] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const ids = { currentPassword: useId(), newPassword: useId(), confirmPassword: useId(), rule: useId(), error: useId() };

  const onSubmit = async (event: FormEvent) => {
    event.preventDefault();
    // The rule, before the round trip (OPEN_ITEMS 62). The server checks it too and decides; catching it here
    // also means a password it would refuse costs no attempt against the limiter of §5.2.
    if (!isLongEnough(form.newPassword)) {
      setError(intl.formatMessage(apiErrors.password_too_short));
      return;
    }
    if (form.newPassword !== form.confirmPassword) {
      setForm((f) => ({ ...f, confirmPassword: '' }));
      setError(intl.formatMessage(messages.mismatch));
      return;
    }
    setPending(true);
    setError(null);
    try {
      await request<void>('PUT', '/me/password', {
        current_password: form.currentPassword,
        new_password: form.newPassword,
      });
      setForm({ currentPassword: '', newPassword: '', confirmPassword: '' });
      setPending(false);
      setDone(true);
    } catch (e) {
      if (isAbort(e)) return;
      const code = e instanceof ApiError ? e.code : null;
      // The passwords stay in the fields only: never in a message or a log.
      setForm({ currentPassword: '', newPassword: '', confirmPassword: '' });
      setPending(false);
      const message: MessageDescriptor = code === 'invalid_credentials' ? messages.changeWrongCurrent : errorMessage(code);
      setError(intl.formatMessage(message));
    }
  };

  // `rule` puts the password rule under the field that must satisfy it, and names it in aria-describedby so a
  // screen reader hears it with the field rather than only after a refusal.
  const field = (name: keyof typeof form, label: MessageDescriptor, autoComplete: string, rule = false) => (
    <p>
      <label htmlFor={ids[name]}>{intl.formatMessage(label)}</label>
      <input
        id={ids[name]}
        name={name}
        type="password"
        autoComplete={autoComplete}
        required
        value={form[name]}
        onChange={(e) => setForm((f) => ({ ...f, [name]: e.target.value }))}
        aria-describedby={[rule ? ids.rule : null, error ? ids.error : null].filter(Boolean).join(' ') || undefined}
      />
      {rule && (
        <span id={ids.rule} className="field-hint">
          {intl.formatMessage(common.passwordRule)}
        </span>
      )}
    </p>
  );

  return (
    <main className="password-change">
      <h1>{intl.formatMessage(messages.changeTitle)}</h1>
      {done ? (
        <p role="status" className="notice">
          {intl.formatMessage(messages.changeDone)} {intl.formatMessage(messages.changeOtherSessions)}
        </p>
      ) : (
        <>
          <ErrorAlert message={error} id={ids.error} />
          <form onSubmit={onSubmit}>
            {field('currentPassword', messages.currentPassword, 'current-password')}
            {field('newPassword', messages.newPassword, 'new-password', true)}
            {field('confirmPassword', messages.confirmPassword, 'new-password')}
            <button type="submit" className="btn" disabled={pending}>
              {intl.formatMessage(pending ? messages.changeSubmitting : messages.changeSubmit)}
            </button>
          </form>
        </>
      )}
    </main>
  );
}
