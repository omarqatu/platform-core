/**
 * The client form's data, apart from React (Amendment 01 §2.8): the shapes the API answers with, the form's values,
 * its rules, and the request body built from them. The server holds the same rules and decides; these say what is
 * wrong beside the field before the round trip, and the database holds them a third time as CHECKs.
 */

/** GET /subscriptions/clients — one row of the list. */
export interface ClientItem {
  id: string;
  name: string;
  is_active: boolean;
}

/** GET /subscriptions/clients/{id}. */
export interface ClientDetail extends ClientItem {
  email: string | null;
  phone_e164: string | null;
  telegram_chat_id: string | null;
}

/** What the fields hold: text as typed, '' for nothing entered. is_active is not here — it is a different endpoint. */
export interface FormValues {
  name: string;
  email: string;
  phoneE164: string;
  telegramChatId: string;
}

export const EMPTY: FormValues = { name: '', email: '', phoneE164: '', telegramChatId: '' };

export type FieldError = 'required' | 'tooLong' | 'email' | 'phone' | 'telegram';
export type FieldErrors = Partial<Record<keyof FormValues, FieldError>>;

const MAX_NAME = 256; // SubscriptionEndpoints.MaxClientName
const MAX_EMAIL = 320; // SubscriptionEndpoints.MaxEmail
const EMAIL = /^[^@\s]+@[^@\s]+\.[^@\s]+$/;
const PHONE = /^\+[1-9][0-9]{1,14}$/; // E.164
const TELEGRAM = /^-?[0-9]{1,20}$/; // a group's chat id is negative

/**
 * Western digits, whatever keyboard typed them — the same conversion the subscription form applies to its date and
 * its cost. A phone number and a chat id are read by machines that accept 0–9 and nothing else, and the interface
 * shows Western digits in Arabic too (web/README).
 */
export function westernDigits(text: string): string {
  return text.replace(/[٠-٩۰-۹]/g, (c) => {
    const code = c.charCodeAt(0);
    return String(code - (code >= 0x06f0 ? 0x06f0 : 0x0660));
  });
}

/**
 * Every field's first broken rule; empty when the form can be sent. The three channels are optional — a client
 * reachable by one is not reachable by three — but an empty field is "no channel" and is sent as null, never as ''.
 */
export function validate(values: FormValues): FieldErrors {
  const errors: FieldErrors = {};
  const name = values.name.trim();
  const email = values.email.trim();
  const phone = values.phoneE164.trim();
  const telegram = values.telegramChatId.trim();

  if (!name) errors.name = 'required';
  else if (name.length > MAX_NAME) errors.name = 'tooLong';
  if (email.length > MAX_EMAIL) errors.email = 'tooLong';
  else if (email && !EMAIL.test(email)) errors.email = 'email';
  if (phone && !PHONE.test(phone)) errors.phoneE164 = 'phone';
  if (telegram && !TELEGRAM.test(telegram)) errors.telegramChatId = 'telegram';
  return errors;
}

/** The request body for POST and PUT alike: both carry the same four fields, and neither carries is_active. */
export function toBody(values: FormValues): Record<string, unknown> {
  const optional = (text: string) => text.trim() || null;
  return {
    name: values.name.trim(),
    email: optional(values.email),
    phone_e164: optional(values.phoneE164),
    telegram_chat_id: optional(values.telegramChatId),
  };
}

/** A stored client, back into the fields. */
export function fromDetail(detail: ClientDetail): FormValues {
  return {
    name: detail.name,
    email: detail.email ?? '',
    phoneE164: detail.phone_e164 ?? '',
    telegramChatId: detail.telegram_chat_id ?? '',
  };
}
