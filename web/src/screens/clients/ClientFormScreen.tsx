import { useEffect, useId, useState, type FormEvent, type ReactNode } from 'react';
import { useIntl, type MessageDescriptor } from 'react-intl';
import { Link, useNavigate, useParams } from 'react-router';
import { ApiError, getJson, isAbort, request } from '../../api/client';
import { ErrorAlert } from '../../components/ErrorAlert';
import { errorMessage } from '../../i18n/apiErrors';
import { useSession } from '../../session/SessionProvider';
import { ForbiddenScreen, NotFoundScreen } from '../status/StatusScreens';
import { TENANT_LOST } from '../subscriptions/SubscriptionsScreen';
import {
  EMPTY,
  fromDetail,
  toBody,
  validate,
  westernDigits,
  type ClientDetail,
  type FieldError,
  type FieldErrors,
  type FormValues,
} from './clientForm';
import { LIST_PATH } from './ClientsScreen';
import { messages } from './formMessages';
import './ClientFormScreen.css';

export const NEW_PATH = '/app/clients/new';
export const EDIT_PATH = '/app/clients/:id/edit';

export type Mode = 'create' | 'edit';

export interface FormData {
  values: FormValues;
  id?: string;
}

type State = { status: 'loading' } | { status: 'error'; code: string | null } | { status: 'ready'; data: FormData };

/**
 * Creating and editing a client (Amendment 01 §2.8) — /app/clients/new and /app/clients/:id/edit. A client the
 * caller cannot see answers not_permitted, exactly as one that does not exist: the screen says the same for both.
 * Disabling is not here: it is another permission, on the list.
 */
export function ClientFormScreen({ mode }: { mode: Mode }) {
  const { id } = useParams();
  const { revalidate } = useSession();
  // Creating has nothing to load, so it starts ready rather than loading and then setting state from an effect.
  const [state, setState] = useState<State>(() =>
    mode === 'create' ? { status: 'ready', data: { values: EMPTY } } : { status: 'loading' },
  );

  useEffect(() => {
    if (mode === 'create') return;
    const abort = new AbortController();
    getJson<ClientDetail>(`/subscriptions/clients/${id}`, abort.signal).then(
      (detail) => setState({ status: 'ready', data: { values: fromDetail(detail), id } }),
      (error: unknown) => {
        if (abort.signal.aborted || isAbort(error)) return;
        const code = error instanceof ApiError ? error.code : null;
        if (code && TENANT_LOST.has(code)) revalidate().catch(() => undefined);
        setState({ status: 'error', code });
      },
    );
    return () => abort.abort();
  }, [mode, id, revalidate]);

  if (state.status === 'error' && state.code === 'not_permitted') return <ForbiddenScreen />;
  if (mode === 'edit' && !id) return <NotFoundScreen />;
  const onRefusal = (code: string | null) => {
    if (code && TENANT_LOST.has(code)) revalidate().catch(() => undefined);
  };
  return <ClientFormView mode={mode} state={state} onRefusal={onRefusal} />;
}

export function ClientFormView({
  mode,
  state,
  onRefusal,
}: {
  mode: Mode;
  state: State;
  onRefusal?: (code: string | null) => void;
}) {
  const intl = useIntl();
  return (
    <main className="client-form">
      <h1>{intl.formatMessage(mode === 'create' ? messages.titleNew : messages.titleEdit)}</h1>
      {state.status === 'loading' && <p>{intl.formatMessage(messages.loading)}</p>}
      {state.status === 'error' && <p role="alert">{intl.formatMessage(errorMessage(state.code))}</p>}
      {state.status === 'ready' && <ClientForm mode={mode} data={state.data} onRefusal={onRefusal} />}
    </main>
  );
}

const FIELD_ERROR: Record<FieldError, MessageDescriptor> = {
  required: messages.required,
  tooLong: messages.tooLong,
  email: messages.emailInvalid,
  phone: messages.phoneInvalid,
  telegram: messages.telegramInvalid,
};

function ClientForm({ mode, data, onRefusal }: { mode: Mode; data: FormData; onRefusal?: (code: string | null) => void }) {
  const intl = useIntl();
  const navigate = useNavigate();
  const [values, setValues] = useState<FormValues>(data.values);
  const [errors, setErrors] = useState<FieldErrors>({});
  const [refusal, setRefusal] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  const base = useId();
  const idOf = (name: keyof FormValues | 'summary' | 'channels') => `${base}-${name}`;

  const set = <K extends keyof FormValues>(name: K, value: FormValues[K]) => {
    setValues((v) => ({ ...v, [name]: value }));
    setErrors((e) => ({ ...e, [name]: undefined }));
  };

  const onSubmit = async (event: FormEvent) => {
    event.preventDefault();
    const found = validate(values);
    setErrors(found);
    if (Object.values(found).some(Boolean)) {
      setRefusal(intl.formatMessage(messages.fix));
      return;
    }
    setPending(true);
    setRefusal(null);
    try {
      if (mode === 'create') await request('POST', '/subscriptions/clients', toBody(values));
      else await request('PUT', `/subscriptions/clients/${data.id}`, toBody(values));
      navigate(LIST_PATH);
    } catch (e) {
      if (isAbort(e)) return;
      const code = e instanceof ApiError ? e.code : null;
      setPending(false);
      setRefusal(intl.formatMessage(errorMessage(code)));
      onRefusal?.(code);
    }
  };

  /** A labelled field: its hint and its error under it, both named by aria-describedby. */
  const field = (name: keyof FormValues, label: MessageDescriptor, control: ReactNode, options: { hint?: MessageDescriptor; optional?: boolean } = {}) => {
    const error = errors[name];
    return (
      <div className="field" data-field={name}>
        <label htmlFor={idOf(name)}>
          {intl.formatMessage(label)}
          {options.optional && <span className="optional"> {intl.formatMessage(messages.optional)}</span>}
        </label>
        {control}
        {options.hint && (
          <span id={`${idOf(name)}-hint`} className="field-hint">
            {intl.formatMessage(options.hint)}
          </span>
        )}
        {error && (
          <span id={`${idOf(name)}-error`} className="field-error">
            {intl.formatMessage(FIELD_ERROR[error])}
          </span>
        )}
      </div>
    );
  };

  const described = (name: keyof FormValues, hint = false) => ({
    id: idOf(name),
    name,
    'aria-invalid': errors[name] ? true : undefined,
    'aria-describedby': [hint ? `${idOf(name)}-hint` : null, errors[name] ? `${idOf(name)}-error` : null].filter(Boolean).join(' ') || undefined,
  });

  return (
    <>
      <ErrorAlert message={refusal} id={idOf('summary')} />
      <form onSubmit={onSubmit} noValidate>
        {field(
          'name',
          messages.name,
          <input {...described('name')} type="text" maxLength={256} value={values.name} onChange={(e) => set('name', e.target.value)} dir="auto" />,
        )}
        <p id={idOf('channels')} className="channels-note">
          {intl.formatMessage(messages.channelsNote)}
        </p>
        {field(
          'email',
          messages.email,
          <input
            {...described('email')}
            type="email"
            maxLength={320}
            autoComplete="off"
            dir="ltr"
            value={values.email}
            onChange={(e) => set('email', e.target.value)}
          />,
          { optional: true },
        )}
        {field(
          'phoneE164',
          messages.phoneE164,
          <input
            {...described('phoneE164', true)}
            type="tel"
            inputMode="tel"
            autoComplete="off"
            dir="ltr"
            value={values.phoneE164}
            onChange={(e) => set('phoneE164', westernDigits(e.target.value))}
          />,
          { hint: messages.phoneHint, optional: true },
        )}
        {field(
          'telegramChatId',
          messages.telegramChatId,
          <input
            {...described('telegramChatId', true)}
            type="text"
            inputMode="numeric"
            autoComplete="off"
            dir="ltr"
            value={values.telegramChatId}
            onChange={(e) => set('telegramChatId', westernDigits(e.target.value))}
          />,
          { hint: messages.telegramHint, optional: true },
        )}
        <div className="actions">
          <button type="submit" className="btn" disabled={pending}>
            {intl.formatMessage(pending ? messages.saving : mode === 'create' ? messages.create : messages.save)}
          </button>
          <Link to={LIST_PATH} className="btn secondary">
            {intl.formatMessage(messages.cancel)}
          </Link>
        </div>
      </form>
    </>
  );
}
