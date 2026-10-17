import { useEffect, useId, useState, type FormEvent, type ReactNode } from 'react';
import { useIntl, type MessageDescriptor } from 'react-intl';
import { Link, useNavigate, useParams } from 'react-router';
import { ApiError, getJson, isAbort, request } from '../../api/client';
import { ErrorAlert } from '../../components/ErrorAlert';
import { errorMessage } from '../../i18n/apiErrors';
import { useLocale } from '../../i18n/I18nProvider';
import { useSession } from '../../session/SessionProvider';
import { ForbiddenScreen, NotFoundScreen } from '../status/StatusScreens';
import { messages } from './formMessages';
import {
  BILLING_CYCLES,
  catalogName,
  EMPTY,
  fromDetail,
  toBody,
  validate,
  westernDigits,
  type CategoriesResponse,
  type CategoryItem,
  type FieldError,
  type FieldErrors,
  type FormValues,
  type SubscriptionDetail,
} from './subscriptionForm';
import { LIST_PATH, TENANT_LOST, type ClientItem, type ScopedList } from './SubscriptionsScreen';
import './SubscriptionFormScreen.css';

export const NEW_PATH = '/app/subscriptions/new';
export const EDIT_PATH = '/app/subscriptions/:id/edit';

const CLIENTS_LIMIT = 200; // the API's maximum page

export type Mode = 'create' | 'edit';

/** What the form needs before it can show: the catalog, the clients the caller sees, and — to edit — the row. */
export interface FormData {
  categories: CategoryItem[];
  clients: ClientItem[];
  values: FormValues;
  id?: string;
}

type State = { status: 'loading' } | { status: 'error'; code: string | null } | { status: 'ready'; data: FormData };

/**
 * Creating and editing a subscription (Amendment 02 §3.2) — /app/subscriptions/new and /app/subscriptions/:id/edit.
 * Loads the catalog (GET /subscriptions/categories), the clients the caller sees (GET /subscriptions/clients), and,
 * to edit, the subscription (GET /subscriptions/{id}). A subscription the caller cannot see answers not_permitted,
 * exactly as one that does not exist: the screen says the same for both.
 */
export function SubscriptionFormScreen({ mode }: { mode: Mode }) {
  const { id } = useParams();
  const { revalidate } = useSession();
  const [state, setState] = useState<State>({ status: 'loading' });

  useEffect(() => {
    const abort = new AbortController();
    Promise.all([
      getJson<CategoriesResponse>('/subscriptions/categories', abort.signal),
      getJson<ScopedList<ClientItem>>(`/subscriptions/clients?limit=${CLIENTS_LIMIT}`, abort.signal),
      mode === 'edit' ? getJson<SubscriptionDetail>(`/subscriptions/${id}`, abort.signal) : Promise.resolve(null),
    ]).then(
      ([catalog, clients, detail]) =>
        setState({
          status: 'ready',
          data: { categories: catalog.categories, clients: clients.items, values: detail ? fromDetail(detail) : EMPTY, id },
        }),
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
  return <SubscriptionFormView mode={mode} state={state} onRefusal={onRefusal} />;
}

export function SubscriptionFormView({
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
    <main className="subscription-form">
      <h1>{intl.formatMessage(mode === 'create' ? messages.titleNew : messages.titleEdit)}</h1>
      {state.status === 'loading' && <p>{intl.formatMessage(messages.loading)}</p>}
      {state.status === 'error' && <p role="alert">{intl.formatMessage(errorMessage(state.code))}</p>}
      {state.status === 'ready' && <SubscriptionForm mode={mode} data={state.data} onRefusal={onRefusal} />}
    </main>
  );
}

const FIELD_ERROR: Record<FieldError, MessageDescriptor> = {
  required: messages.required,
  tooLong: messages.tooLong,
  date: messages.date,
  cost: messages.costInvalid,
  currency: messages.currencyInvalid,
  costNeedsCurrency: messages.costNeedsCurrency,
  url: messages.url,
};

function SubscriptionForm({ mode, data, onRefusal }: { mode: Mode; data: FormData; onRefusal?: (code: string | null) => void }) {
  const intl = useIntl();
  const { lang } = useLocale();
  const navigate = useNavigate();
  const [values, setValues] = useState<FormValues>(data.values);
  const [errors, setErrors] = useState<FieldErrors>({});
  const [refusal, setRefusal] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  const base = useId();
  const idOf = (name: keyof FormValues | 'summary') => `${base}-${name}`;

  const category = data.categories.find((c) => c.id === values.categoryId);
  const subcategories = category?.subcategories ?? [];
  const client = data.clients.find((c) => c.id === values.clientId);

  const set = <K extends keyof FormValues>(name: K, value: FormValues[K]) => {
    setValues((v) => ({
      ...v,
      [name]: value,
      // A subcategory belongs to its category: choosing another category clears it.
      ...(name === 'categoryId' ? { subcategoryId: '' } : {}),
    }));
    setErrors((e) => ({ ...e, [name]: undefined }));
  };

  const onSubmit = async (event: FormEvent) => {
    event.preventDefault();
    const found = validate(values, mode);
    setErrors(found);
    if (Object.values(found).some(Boolean)) {
      setRefusal(intl.formatMessage(messages.fix));
      return;
    }
    setPending(true);
    setRefusal(null);
    try {
      if (mode === 'create') await request('POST', '/subscriptions', toBody(values, mode));
      else await request('PUT', `/subscriptions/${data.id}`, toBody(values, mode));
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

  const text = (name: 'serviceName' | 'provider' | 'loginUrl', extra: { type?: string; maxLength?: number; hint?: boolean } = {}) => (
    <input
      {...described(name, extra.hint)}
      type={extra.type ?? 'text'}
      maxLength={extra.maxLength}
      value={values[name]}
      onChange={(e) => set(name, e.target.value)}
      dir={extra.type === 'url' ? 'ltr' : 'auto'}
    />
  );

  return (
    <>
      <ErrorAlert message={refusal} id={idOf('summary')} />
      <form onSubmit={onSubmit} noValidate>
        {mode === 'create'
          ? field(
              'clientId',
              messages.client,
              <select {...described('clientId')} value={values.clientId} onChange={(e) => set('clientId', e.target.value)}>
                <option value="">{intl.formatMessage(messages.chooseClient)}</option>
                {data.clients.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name}
                  </option>
                ))}
              </select>,
            )
          : field(
              'clientId',
              messages.client,
              <output id={idOf('clientId')} className="readonly">
                <bdi dir="auto">{client?.name ?? ''}</bdi>
              </output>,
            )}
        {field('serviceName', messages.serviceName, text('serviceName', { maxLength: 256 }))}
        {field(
          'endsOn',
          messages.endsOn,
          <input
            {...described('endsOn', true)}
            type="text"
            inputMode="numeric"
            autoComplete="off"
            dir="ltr"
            value={values.endsOn}
            onChange={(e) => set('endsOn', westernDigits(e.target.value))}
          />,
          { hint: messages.endsOnHint },
        )}
        {field(
          'categoryId',
          messages.category,
          <select {...described('categoryId')} value={values.categoryId} onChange={(e) => set('categoryId', e.target.value)}>
            <option value="">{intl.formatMessage(messages.chooseCategory)}</option>
            {data.categories.map((c) => (
              <option key={c.id} value={c.id} data-code={c.code}>
                {catalogName(c, lang)}
              </option>
            ))}
          </select>,
        )}
        {subcategories.length > 0 &&
          field(
            'subcategoryId',
            messages.subcategory,
            <select {...described('subcategoryId')} value={values.subcategoryId} onChange={(e) => set('subcategoryId', e.target.value)}>
              <option value="">{intl.formatMessage(messages.noSubcategory)}</option>
              {subcategories.map((s) => (
                <option key={s.id} value={s.id}>
                  {catalogName(s, lang)}
                </option>
              ))}
            </select>,
            { optional: true },
          )}
        {field('provider', messages.provider, text('provider', { maxLength: 256, hint: true }), { hint: messages.providerHint, optional: true })}
        <div className="pair">
          {field(
            'cost',
            messages.cost,
            <input
              {...described('cost', true)}
              type="text"
              inputMode="decimal"
              autoComplete="off"
              dir="ltr"
              value={values.cost}
              onChange={(e) => set('cost', westernDigits(e.target.value))}
            />,
            { hint: messages.costHint, optional: true },
          )}
          {field(
            'currency',
            messages.currency,
            <input
              {...described('currency', true)}
              type="text"
              autoComplete="off"
              autoCapitalize="characters"
              maxLength={3}
              dir="ltr"
              value={values.currency}
              onChange={(e) => set('currency', e.target.value.toUpperCase())}
            />,
            { hint: messages.currencyHint, optional: true },
          )}
        </div>
        {field(
          'billingCycle',
          messages.billingCycle,
          <select
            {...described('billingCycle')}
            value={values.billingCycle}
            onChange={(e) => set('billingCycle', e.target.value as FormValues['billingCycle'])}
          >
            <option value="">{intl.formatMessage(messages.chooseBillingCycle)}</option>
            {BILLING_CYCLES.map((cycle) => (
              <option key={cycle} value={cycle}>
                {intl.formatMessage(cycle === 'monthly' ? messages.monthly : messages.yearly)}
              </option>
            ))}
          </select>,
        )}
        <div className="field check" data-field="autoRenew">
          <input
            id={idOf('autoRenew')}
            name="autoRenew"
            type="checkbox"
            checked={values.autoRenew}
            onChange={(e) => set('autoRenew', e.target.checked)}
            aria-describedby={`${idOf('autoRenew')}-hint`}
          />
          <label htmlFor={idOf('autoRenew')}>{intl.formatMessage(messages.autoRenew)}</label>
          <span id={`${idOf('autoRenew')}-hint`} className="field-hint">
            {intl.formatMessage(messages.autoRenewHint)}
          </span>
        </div>
        {field('loginUrl', messages.loginUrl, text('loginUrl', { type: 'url', maxLength: 2048 }), { optional: true })}
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
