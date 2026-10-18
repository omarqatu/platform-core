import { describe, expect, it } from 'vitest';
import { EMPTY, fromDetail, toBody, validate, westernDigits, type FormValues } from './clientForm';

const complete: FormValues = {
  name: 'Nour Co',
  email: 'nour@example.com',
  phoneE164: '+962791234567',
  telegramChatId: '-1001234567890',
};

describe("the client form's rules", () => {
  it('accepts a complete client', () => {
    expect(validate(complete)).toEqual({});
  });

  it('needs a name and nothing else', () => {
    expect(validate(EMPTY)).toEqual({ name: 'required' });
    expect(validate({ ...EMPTY, name: 'Only a name' })).toEqual({});
  });

  it('refuses a name longer than the column', () => {
    expect(validate({ ...EMPTY, name: 'x'.repeat(257) })).toEqual({ name: 'tooLong' });
    expect(validate({ ...EMPTY, name: 'x'.repeat(256) })).toEqual({});
  });

  it.each([
    ['not-an-address', 'email'],
    ['two@@example.com', 'email'],
    ['no domain@example', 'email'],
    ['name@example.com', undefined],
  ])('holds the e-mail to one shape: %s', (email, expected) => {
    expect(validate({ ...complete, email }).email).toBe(expected);
  });

  it.each([
    ['0791234567', 'phone'],
    ['+0791234567', 'phone'],
    ['962791234567', 'phone'],
    ['+٩٦٢٧٩١٢٣٤٥٦٧', 'phone'],
    ['+962791234567', undefined],
    ['+12', undefined],
  ])('holds the phone to E.164: %s', (phoneE164, expected) => {
    expect(validate({ ...complete, phoneE164 }).phoneE164).toBe(expected);
  });

  it.each([
    ['abc', 'telegram'],
    ['12 34', 'telegram'],
    ['-1001234567890', undefined],
    ['1234567890', undefined],
  ])('holds the Telegram chat id to a signed integer: %s', (telegramChatId, expected) => {
    expect(validate({ ...complete, telegramChatId }).telegramChatId).toBe(expected);
  });

  // The same conversion the subscription form applies: a number typed on an Arabic keyboard is still a number.
  it('converts Arabic-Indic digits as they are typed', () => {
    expect(westernDigits('+٩٦٢٧٩١٢٣٤٥٦٧')).toBe('+962791234567');
    expect(westernDigits('-۱۰۰۱')).toBe('-1001');
  });
});

describe('the body it sends', () => {
  it('sends every channel it was given, trimmed', () => {
    expect(toBody({ ...complete, name: '  Nour Co  ' })).toEqual({
      name: 'Nour Co',
      email: 'nour@example.com',
      phone_e164: '+962791234567',
      telegram_chat_id: '-1001234567890',
    });
  });

  // An empty field is "no channel", and null is how the API is told so — never ''. A '' would pass the column's
  // NULL check and match nothing when the sender looks for an address.
  it('sends null for a channel left empty, never an empty string', () => {
    expect(toBody({ ...EMPTY, name: 'P' })).toEqual({ name: 'P', email: null, phone_e164: null, telegram_chat_id: null });
  });

  // is_active is another permission on another endpoint: the form never sends it, whatever it holds.
  it('never carries is_active', () => {
    expect(toBody(complete)).not.toHaveProperty('is_active');
  });

  it('round-trips a stored client', () => {
    const values = fromDetail({
      id: 'c1',
      name: 'Nour Co',
      is_active: false,
      email: null,
      phone_e164: '+962791234567',
      telegram_chat_id: null,
    });
    expect(values).toEqual({ name: 'Nour Co', email: '', phoneE164: '+962791234567', telegramChatId: '' });
    expect(toBody(values)).toEqual({ name: 'Nour Co', email: null, phone_e164: '+962791234567', telegram_chat_id: null });
  });
});
