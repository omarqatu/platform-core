import { defineMessages, type NoMessageValues } from 'react-intl';

type Values = {
  titleNew: NoMessageValues;
  titleEdit: NoMessageValues;
  loading: NoMessageValues;
  optional: NoMessageValues;
  name: NoMessageValues;
  email: NoMessageValues;
  phoneE164: NoMessageValues;
  phoneHint: NoMessageValues;
  telegramChatId: NoMessageValues;
  telegramHint: NoMessageValues;
  channelsNote: NoMessageValues;
  create: NoMessageValues;
  save: NoMessageValues;
  saving: NoMessageValues;
  cancel: NoMessageValues;
  fix: NoMessageValues;
  required: NoMessageValues;
  tooLong: NoMessageValues;
  emailInvalid: NoMessageValues;
  phoneInvalid: NoMessageValues;
  telegramInvalid: NoMessageValues;
};

/** Creating and editing a client (Amendment 01 §2.8). Disabling is not on this form: it is another permission. */
export const messages = defineMessages<Values>({
  titleNew: { id: 'clients.form.titleNew', defaultMessage: 'New client' },
  titleEdit: { id: 'clients.form.titleEdit', defaultMessage: 'Edit client' },
  loading: { id: 'clients.form.loading', defaultMessage: 'Loading…' },
  optional: { id: 'clients.form.optional', defaultMessage: '(optional)' },
  name: { id: 'clients.form.name', defaultMessage: 'Name' },
  email: { id: 'clients.form.email', defaultMessage: 'E-mail' },
  phoneE164: { id: 'clients.form.phoneE164', defaultMessage: 'Phone' },
  phoneHint: { id: 'clients.form.phoneHint', defaultMessage: 'With the country code, for example +962791234567' },
  telegramChatId: { id: 'clients.form.telegramChatId', defaultMessage: 'Telegram chat id' },
  telegramHint: { id: 'clients.form.telegramHint', defaultMessage: "A number; a group's id starts with a minus sign" },
  channelsNote: {
    id: 'clients.form.channelsNote',
    defaultMessage: 'Where reminders reach this client. Fill in the ones you have; none is required.',
  },
  create: { id: 'clients.form.create', defaultMessage: 'Create' },
  save: { id: 'clients.form.save', defaultMessage: 'Save' },
  saving: { id: 'clients.form.saving', defaultMessage: 'Saving…' },
  cancel: { id: 'clients.form.cancel', defaultMessage: 'Cancel' },
  fix: { id: 'clients.form.fix', defaultMessage: 'Check the fields marked below.' },
  required: { id: 'clients.form.required', defaultMessage: 'Required' },
  tooLong: { id: 'clients.form.tooLong', defaultMessage: 'Too long' },
  emailInvalid: { id: 'clients.form.emailInvalid', defaultMessage: 'An e-mail address, for example name@example.com' },
  phoneInvalid: { id: 'clients.form.phoneInvalid', defaultMessage: 'A number with its country code, for example +962791234567' },
  telegramInvalid: { id: 'clients.form.telegramInvalid', defaultMessage: 'Digits only, with a leading minus sign for a group' },
});
