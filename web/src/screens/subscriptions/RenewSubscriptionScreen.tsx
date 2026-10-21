import { useEffect, useId, useState, type FormEvent } from 'react';
import { useIntl } from 'react-intl';
import { Link, useNavigate, useParams } from 'react-router';
import { ApiError, getJson, isAbort, request } from '../../api/client';
import { Day } from '../../components/Day';
import { ErrorAlert } from '../../components/ErrorAlert';
import { errorMessage } from '../../i18n/apiErrors';
import { useSession } from '../../session/SessionProvider';
import { ForbiddenScreen, NotFoundScreen } from '../status/StatusScreens';
import { messages } from './formMessages';
import { isDay, westernDigits, type SubscriptionDetail } from './subscriptionForm';
import { LIST_PATH, TENANT_LOST } from './SubscriptionsScreen';
import './SubscriptionFormScreen.css';

export const RENEW_PATH = '/app/subscriptions/:id/renew';

type State = { status: 'loading' } | { status: 'error'; code: string | null } | { status: 'ready'; subscription: SubscriptionDetail };

/**
 * Renewing a subscription (Amendment 01 §2.9) — /app/subscriptions/:id/renew. One field: the new end date. The
 * client is not among them and is not sent: a renewal keeps the client of the subscription it renews, and the
 * server refuses a request that names one at all.
 */
export function RenewSubscriptionScreen() {
  const { id } = useParams();
  const { revalidate } = useSession();
  const [state, setState] = useState<State>({ status: 'loading' });

  useEffect(() => {
    const abort = new AbortController();
    getJson<SubscriptionDetail>(`/subscriptions/${id}`, abort.signal).then(
      (subscription) => setState({ status: 'ready', subscription }),
      (error: unknown) => {
        if (abort.signal.aborted || isAbort(error)) return;
        const code = error instanceof ApiError ? error.code : null;
        if (code && TENANT_LOST.has(code)) revalidate().catch(() => undefined);
        setState({ status: 'error', code });
      },
    );
    return () => abort.abort();
  }, [id, revalidate]);

  if (state.status === 'error' && state.code === 'not_permitted') return <ForbiddenScreen />;
  if (!id) return <NotFoundScreen />;
  const onRefusal = (code: string | null) => {
    if (code && TENANT_LOST.has(code)) revalidate().catch(() => undefined);
  };
  return <RenewView state={state} onRefusal={onRefusal} />;
}

export function RenewView({ state, onRefusal }: { state: State; onRefusal?: (code: string | null) => void }) {
  const intl = useIntl();
  return (
    <main className="subscription-form">
      <h1>{intl.formatMessage(messages.renewTitle)}</h1>
      {state.status === 'loading' && <p>{intl.formatMessage(messages.loading)}</p>}
      {state.status === 'error' && <p role="alert">{intl.formatMessage(errorMessage(state.code))}</p>}
      {state.status === 'ready' && <RenewForm subscription={state.subscription} onRefusal={onRefusal} />}
    </main>
  );
}

function RenewForm({ subscription, onRefusal }: { subscription: SubscriptionDetail; onRefusal?: (code: string | null) => void }) {
  const intl = useIntl();
  const navigate = useNavigate();
  const [endsOn, setEndsOn] = useState('');
  const [error, setError] = useState<'required' | 'date' | 'after' | null>(null);
  const [refusal, setRefusal] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  const base = useId();
  const idOf = (name: string) => `${base}-${name}`;

  const onSubmit = async (event: FormEvent) => {
    event.preventDefault();
    const value = endsOn.trim();
    // The same three rules the server holds: a date, a real one, and later than the term it renews.
    const found = !value ? 'required' : !isDay(value) ? 'date' : value <= subscription.ends_on ? 'after' : null;
    setError(found);
    if (found) {
      setRefusal(intl.formatMessage(messages.fix));
      return;
    }
    setPending(true);
    setRefusal(null);
    try {
      await request('POST', `/subscriptions/${subscription.id}/renew`, { ends_on: value });
      navigate(LIST_PATH);
    } catch (e) {
      if (isAbort(e)) return;
      const code = e instanceof ApiError ? e.code : null;
      setPending(false);
      setRefusal(intl.formatMessage(errorMessage(code)));
      onRefusal?.(code);
    }
  };

  const message = error === 'required' ? messages.required : error === 'date' ? messages.date : messages.renewAfter;
  return (
    <>
      <ErrorAlert message={refusal} id={idOf('summary')} />
      <p className="field-hint">{intl.formatMessage(messages.renewNote)}</p>
      <form onSubmit={onSubmit} noValidate>
        <div className="field" data-field="serviceName">
          <label htmlFor={idOf('serviceName')}>{intl.formatMessage(messages.renewService)}</label>
          <output id={idOf('serviceName')} className="readonly">
            <bdi dir="auto">{subscription.service_name}</bdi>
          </output>
        </div>
        <div className="field" data-field="currentEnd">
          <label htmlFor={idOf('currentEnd')}>{intl.formatMessage(messages.renewCurrentEnd)}</label>
          <output id={idOf('currentEnd')} className="readonly">
            <Day day={subscription.ends_on} />
          </output>
        </div>
        <div className="field" data-field="endsOn">
          <label htmlFor={idOf('endsOn')}>{intl.formatMessage(messages.renewNewEnd)}</label>
          <input
            id={idOf('endsOn')}
            name="endsOn"
            type="text"
            inputMode="numeric"
            autoComplete="off"
            dir="ltr"
            value={endsOn}
            aria-invalid={error ? true : undefined}
            aria-describedby={[`${idOf('endsOn')}-hint`, error ? `${idOf('endsOn')}-error` : null].filter(Boolean).join(' ')}
            onChange={(e) => {
              setEndsOn(westernDigits(e.target.value));
              setError(null);
            }}
          />
          <span id={`${idOf('endsOn')}-hint`} className="field-hint">
            {intl.formatMessage(messages.renewNewEndHint)}
          </span>
          {error && (
            <span id={`${idOf('endsOn')}-error`} className="field-error">
              {intl.formatMessage(message)}
            </span>
          )}
        </div>
        <div className="actions">
          <button type="submit" className="btn" disabled={pending}>
            {intl.formatMessage(pending ? messages.saving : messages.renewSubmit)}
          </button>
          <Link to={LIST_PATH} className="btn secondary">
            {intl.formatMessage(messages.cancel)}
          </Link>
        </div>
      </form>
    </>
  );
}
