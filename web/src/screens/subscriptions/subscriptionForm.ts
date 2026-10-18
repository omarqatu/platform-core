/**
 * The subscription form's data, apart from React (Amendment 02 §3.2): the shapes the API answers with, the form's
 * values, its rules, and the request body built from them. The server holds the same rules and decides; these say
 * what is wrong beside the field before the round trip.
 */

export interface SubcategoryItem {
  id: string;
  code: string;
  name_ar: string;
  name_en: string;
}

/** A category of the platform's catalog (§3.1), with its subcategories — none are seeded yet. */
export interface CategoryItem extends SubcategoryItem {
  subcategories: SubcategoryItem[];
}

export interface CategoriesResponse {
  categories: CategoryItem[];
}

export type BillingCycle = 'monthly' | 'yearly';
export const BILLING_CYCLES: readonly BillingCycle[] = ['monthly', 'yearly'];

/** GET /subscriptions/{id}. */
export interface SubscriptionDetail {
  id: string;
  client_id: string;
  service_name: string;
  ends_on: string; // yyyy-MM-dd
  reminder_type: string;
  category_id: string;
  subcategory_id: string | null;
  provider: string | null;
  /** A decimal string ("12.500"), never a JS number: money does not pass through binary floating point. */
  cost: string | null;
  currency: string | null;
  billing_cycle: BillingCycle;
  auto_renew: boolean;
  login_url: string | null;
  status: LifecycleStatus;
  renewed_from_id: string | null;
}

/** The subscription's own lifecycle (Amendment 01 §2.9) — not the date-derived status the list colours a row by. */
export type LifecycleStatus = 'active' | 'cancelled' | 'renewed';

/** What the fields hold: text as typed, '' for nothing chosen. */
export interface FormValues {
  clientId: string;
  serviceName: string;
  endsOn: string;
  categoryId: string;
  subcategoryId: string;
  provider: string;
  cost: string;
  currency: string;
  billingCycle: BillingCycle | '';
  autoRenew: boolean;
  loginUrl: string;
}

export const EMPTY: FormValues = {
  clientId: '',
  serviceName: '',
  endsOn: '',
  categoryId: '',
  subcategoryId: '',
  provider: '',
  cost: '',
  currency: '',
  billingCycle: '',
  autoRenew: false,
  loginUrl: '',
};

export type FieldError = 'required' | 'tooLong' | 'date' | 'cost' | 'currency' | 'costNeedsCurrency' | 'url';
export type FieldErrors = Partial<Record<keyof FormValues, FieldError>>;

const MAX_TEXT = 256; // service_name and provider (SubscriptionEndpoints)
const MAX_URL = 2048;
const DATE = /^\d{4}-\d{2}-\d{2}$/;
const COST = /^\d{1,11}(\.\d{1,3})?$/; // numeric(14,3)
const CURRENCY = /^[A-Z]{3}$/; // ISO 4217

/**
 * Western digits, whatever keyboard typed them: Arabic-Indic (٠–٩) and Eastern Arabic-Indic (۰–۹) digits become
 * 0–9, and the Arabic decimal separator (٫) a point. The interface shows Western digits in Arabic too (web/README),
 * and the fields the server parses — the date and the cost — accept nothing else.
 */
export function westernDigits(text: string): string {
  return text.replace(/[٠-٩۰-۹٫]/g, (c) => {
    if (c === '٫') return '.';
    const code = c.charCodeAt(0);
    return String(code - (code >= 0x06f0 ? 0x06f0 : 0x0660));
  });
}

/** A real calendar day written yyyy-MM-dd — 2027-02-30 is not one. */
export function isDay(text: string): boolean {
  if (!DATE.test(text)) return false;
  const date = new Date(`${text}T00:00:00Z`);
  return !Number.isNaN(date.getTime()) && date.toISOString().startsWith(text);
}

function isWebAddress(text: string): boolean {
  try {
    const url = new URL(text);
    return url.protocol === 'https:' || url.protocol === 'http:';
  } catch {
    return false;
  }
}

/** Every field's first broken rule; empty when the form can be sent. `create` also requires the client. */
export function validate(values: FormValues, mode: 'create' | 'edit'): FieldErrors {
  const errors: FieldErrors = {};
  const serviceName = values.serviceName.trim();
  const provider = values.provider.trim();
  const cost = values.cost.trim();
  const currency = values.currency.trim();
  const loginUrl = values.loginUrl.trim();

  if (mode === 'create' && !values.clientId) errors.clientId = 'required';
  if (!serviceName) errors.serviceName = 'required';
  else if (serviceName.length > MAX_TEXT) errors.serviceName = 'tooLong';
  if (!values.endsOn.trim()) errors.endsOn = 'required';
  else if (!isDay(values.endsOn.trim())) errors.endsOn = 'date';
  if (!values.categoryId) errors.categoryId = 'required';
  if (!values.billingCycle) errors.billingCycle = 'required';
  if (provider.length > MAX_TEXT) errors.provider = 'tooLong';
  if (cost && !COST.test(cost)) errors.cost = 'cost';
  if (currency && !CURRENCY.test(currency)) errors.currency = 'currency';
  else if (cost && !currency) errors.currency = 'costNeedsCurrency';
  if (loginUrl.length > MAX_URL) errors.loginUrl = 'tooLong';
  else if (loginUrl && !isWebAddress(loginUrl)) errors.loginUrl = 'url';
  return errors;
}

/**
 * The request body (POST includes the client; PUT does not). An empty optional field is null, never ''. The cost is
 * sent as the string typed, so no binary floating point stands between the field and numeric(14,3).
 */
export function toBody(values: FormValues, mode: 'create' | 'edit'): Record<string, unknown> {
  const optional = (text: string) => text.trim() || null;
  const cost = values.cost.trim();
  return {
    ...(mode === 'create' ? { client_id: values.clientId } : {}),
    service_name: values.serviceName.trim(),
    ends_on: values.endsOn.trim(),
    category_id: values.categoryId,
    subcategory_id: values.subcategoryId || null,
    provider: optional(values.provider),
    cost: cost || null, // as typed, already held to numeric(14,3) by validate
    currency: optional(values.currency),
    billing_cycle: values.billingCycle,
    auto_renew: values.autoRenew,
    login_url: optional(values.loginUrl),
  };
}

/** A stored subscription, back into the fields. */
export function fromDetail(detail: SubscriptionDetail): FormValues {
  return {
    clientId: detail.client_id,
    serviceName: detail.service_name,
    endsOn: detail.ends_on,
    categoryId: detail.category_id,
    subcategoryId: detail.subcategory_id ?? '',
    provider: detail.provider ?? '',
    cost: detail.cost ?? '',
    currency: detail.currency ?? '',
    billingCycle: detail.billing_cycle,
    autoRenew: detail.auto_renew,
    loginUrl: detail.login_url ?? '',
  };
}

/** A catalog entry's name in the interface's language: platform data, named in both, never translated here. */
export function catalogName(entry: SubcategoryItem, lang: 'ar' | 'en'): string {
  return lang === 'ar' ? entry.name_ar : entry.name_en;
}
