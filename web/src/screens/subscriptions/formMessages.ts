import { defineMessages, type MessageTag, type NoMessageValues } from 'react-intl';

// The values each message takes, so formatMessage checks them (react-intl's typed descriptors).
type Values = {
  titleNew: NoMessageValues;
  titleEdit: NoMessageValues;
  loading: NoMessageValues;
  optional: NoMessageValues;
  client: NoMessageValues;
  chooseClient: NoMessageValues;
  serviceName: NoMessageValues;
  endsOn: NoMessageValues;
  endsOnHint: NoMessageValues;
  category: NoMessageValues;
  chooseCategory: NoMessageValues;
  subcategory: NoMessageValues;
  noSubcategory: NoMessageValues;
  provider: NoMessageValues;
  providerHint: NoMessageValues;
  cost: NoMessageValues;
  costHint: NoMessageValues;
  currency: NoMessageValues;
  currencyHint: NoMessageValues;
  billingCycle: NoMessageValues;
  chooseBillingCycle: NoMessageValues;
  monthly: NoMessageValues;
  yearly: NoMessageValues;
  autoRenew: NoMessageValues;
  autoRenewHint: NoMessageValues;
  loginUrl: NoMessageValues;
  create: NoMessageValues;
  save: NoMessageValues;
  saving: NoMessageValues;
  cancel: NoMessageValues;
  fix: NoMessageValues;
  required: NoMessageValues;
  tooLong: NoMessageValues;
  date: NoMessageValues;
  costInvalid: NoMessageValues;
  currencyInvalid: NoMessageValues;
  costNeedsCurrency: NoMessageValues;
  url: NoMessageValues;
  newSubscription: NoMessageValues;
  columnActions: NoMessageValues;
  edit: NoMessageValues;
  editLabel: { service: string };
  renew: NoMessageValues;
  renewLabel: { service: string };
  cancelSubscription: NoMessageValues;
  cancelLabel: { service: string };
  confirmCancel: { service: string };
  working: NoMessageValues;
  actionsFor: { service: string };
  expiredSince: { date: string; ltr: MessageTag };
  renewedNoActions: NoMessageValues;
  cancelledNoActions: NoMessageValues;
  renewTitle: NoMessageValues;
  renewService: NoMessageValues;
  renewCurrentEnd: NoMessageValues;
  renewNewEnd: NoMessageValues;
  renewNewEndHint: NoMessageValues;
  renewNote: NoMessageValues;
  renewSubmit: NoMessageValues;
  renewAfter: NoMessageValues;
};

/**
 * Creating and editing a subscription (Amendment 02 §3.2). The category names are the catalog's own (name_ar,
 * name_en), so they are data and not messages here; the billing cycles are a closed catalog of two, so they are.
 */
export const messages = defineMessages<Values>({
  titleNew: { id: 'subscriptions.form.titleNew', defaultMessage: 'New subscription' },
  titleEdit: { id: 'subscriptions.form.titleEdit', defaultMessage: 'Edit subscription' },
  loading: { id: 'subscriptions.form.loading', defaultMessage: 'Loading…' },
  optional: { id: 'subscriptions.form.optional', defaultMessage: '(optional)' },
  client: { id: 'subscriptions.form.client', defaultMessage: 'Client' },
  chooseClient: { id: 'subscriptions.form.chooseClient', defaultMessage: 'Choose a client' },
  serviceName: { id: 'subscriptions.form.serviceName', defaultMessage: 'Service' },
  endsOn: { id: 'subscriptions.form.endsOn', defaultMessage: 'Ends on' },
  endsOnHint: { id: 'subscriptions.form.endsOnHint', defaultMessage: 'Year-month-day, such as 2027-03-31.' },
  category: { id: 'subscriptions.form.category', defaultMessage: 'Category' },
  chooseCategory: { id: 'subscriptions.form.chooseCategory', defaultMessage: 'Choose a category' },
  subcategory: { id: 'subscriptions.form.subcategory', defaultMessage: 'Subcategory' },
  noSubcategory: { id: 'subscriptions.form.noSubcategory', defaultMessage: 'None' },
  provider: { id: 'subscriptions.form.provider', defaultMessage: 'Provider' },
  providerHint: { id: 'subscriptions.form.providerHint', defaultMessage: 'Who is paid for it.' },
  cost: { id: 'subscriptions.form.cost', defaultMessage: 'Cost' },
  costHint: {
    id: 'subscriptions.form.costHint',
    defaultMessage: 'Per billing cycle. Zero is a cost: a free subscription still gets its reminder.',
  },
  currency: { id: 'subscriptions.form.currency', defaultMessage: 'Currency' },
  currencyHint: { id: 'subscriptions.form.currencyHint', defaultMessage: 'A three-letter code, such as JOD or USD.' },
  billingCycle: { id: 'subscriptions.form.billingCycle', defaultMessage: 'Billing cycle' },
  chooseBillingCycle: { id: 'subscriptions.form.chooseBillingCycle', defaultMessage: 'Choose a cycle' },
  monthly: { id: 'subscriptions.form.monthly', defaultMessage: 'Monthly' },
  yearly: { id: 'subscriptions.form.yearly', defaultMessage: 'Yearly' },
  autoRenew: { id: 'subscriptions.form.autoRenew', defaultMessage: 'Renews automatically' },
  autoRenewHint: {
    id: 'subscriptions.form.autoRenewHint',
    defaultMessage: 'The provider charges by itself when the subscription ends.',
  },
  loginUrl: { id: 'subscriptions.form.loginUrl', defaultMessage: 'Management link' },
  create: { id: 'subscriptions.form.create', defaultMessage: 'Create subscription' },
  save: { id: 'subscriptions.form.save', defaultMessage: 'Save changes' },
  saving: { id: 'subscriptions.form.saving', defaultMessage: 'Saving…' },
  cancel: { id: 'subscriptions.form.cancel', defaultMessage: 'Cancel' },
  fix: { id: 'subscriptions.form.fix', defaultMessage: 'Some fields need attention.' },
  required: { id: 'subscriptions.form.required', defaultMessage: 'This field is required.' },
  tooLong: { id: 'subscriptions.form.tooLong', defaultMessage: 'This is too long.' },
  date: { id: 'subscriptions.form.date', defaultMessage: 'Write a real date as year-month-day, such as 2027-03-31.' },
  costInvalid: {
    id: 'subscriptions.form.costInvalid',
    defaultMessage: 'Write the cost as a number, with at most three decimal places.',
  },
  currencyInvalid: {
    id: 'subscriptions.form.currencyInvalid',
    defaultMessage: 'Write three Latin capital letters, such as JOD.',
  },
  costNeedsCurrency: { id: 'subscriptions.form.costNeedsCurrency', defaultMessage: 'A cost needs its currency.' },
  url: { id: 'subscriptions.form.url', defaultMessage: 'Write the full address, starting with https://.' },
  // On the list screen.
  newSubscription: { id: 'subscriptions.list.newSubscription', defaultMessage: 'New subscription' },
  columnActions: { id: 'subscriptions.list.columnActions', defaultMessage: 'Actions' },
  edit: { id: 'subscriptions.list.edit', defaultMessage: 'Edit' },
  // The link's accessible name: which row's "Edit" it is.
  editLabel: { id: 'subscriptions.list.editLabel', defaultMessage: 'Edit {service}' },
  renew: { id: 'subscriptions.list.renew', defaultMessage: 'Renew' },
  renewLabel: { id: 'subscriptions.list.renewLabel', defaultMessage: 'Renew {service}' },
  cancelSubscription: { id: 'subscriptions.list.cancelSubscription', defaultMessage: 'Cancel subscription' },
  cancelLabel: { id: 'subscriptions.list.cancelLabel', defaultMessage: 'Cancel {service}' },
  // Said plainly: cancelling keeps the row and its history — it leaves the default "Active" list for "Cancelled" — and
  // nothing about it can be changed afterwards.
  confirmCancel: {
    id: 'subscriptions.list.confirmCancel',
    defaultMessage: 'Cancel {service}? It is kept with its history, under “Cancelled”, and it cannot be edited or renewed afterwards.',
  },
  working: { id: 'subscriptions.list.working', defaultMessage: 'Working…' },
  // The row's actions button (RowActions): which row's actions it opens.
  actionsFor: { id: 'subscriptions.list.actionsFor', defaultMessage: 'Actions for {service}' },
  // Beside an active row whose end date has passed: why its menu is worth opening (renewing comes first in it). The
  // date is the end date, yyyy-MM-dd, isolated left-to-right inside the sentence by the <ltr> tag (a <bdi dir="ltr">).
  expiredSince: { id: 'subscriptions.list.expiredSince', defaultMessage: 'Expired since <ltr>{date}</ltr>' },
  // In place of the actions of a row that has left 'active' (Amendment 01 §2.9): a rule, said, not an empty cell.
  renewedNoActions: { id: 'subscriptions.list.renewedNoActions', defaultMessage: 'Renewed, no actions' },
  cancelledNoActions: { id: 'subscriptions.list.cancelledNoActions', defaultMessage: 'Cancelled, no actions' },
  // Renewing (Amendment 01 §2.9): a new subscription that keeps every detail of the one it renews but its end date.
  renewTitle: { id: 'subscriptions.renew.title', defaultMessage: 'Renew subscription' },
  renewService: { id: 'subscriptions.renew.service', defaultMessage: 'Service' },
  renewCurrentEnd: { id: 'subscriptions.renew.currentEnd', defaultMessage: 'Ends on' },
  renewNewEnd: { id: 'subscriptions.renew.newEnd', defaultMessage: 'New end date' },
  renewNewEndHint: { id: 'subscriptions.renew.newEndHint', defaultMessage: 'yyyy-MM-dd, for example 2028-01-31' },
  renewNote: {
    id: 'subscriptions.renew.note',
    defaultMessage:
      'A renewal is a new subscription that keeps the client and every other detail of this one. This one is kept too, marked as renewed.',
  },
  renewSubmit: { id: 'subscriptions.renew.submit', defaultMessage: 'Renew' },
  renewAfter: { id: 'subscriptions.renew.after', defaultMessage: 'Must be later than the current end date' },
});
