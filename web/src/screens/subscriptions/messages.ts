import { defineMessages, type NoMessageValues } from 'react-intl';

// The values each message takes, so formatMessage checks them (react-intl's typed descriptors).
type Values = {
  title: NoMessageValues;
  loading: NoMessageValues;
  scopeAll: { subscriptions: number };
  scopeAssigned: { clients: number; subscriptions: number };
  scopeAssignedEmpty: NoMessageValues;
  columnService: NoMessageValues;
  columnClient: NoMessageValues;
  columnEndsOn: NoMessageValues;
  columnStatus: NoMessageValues;
  statusActive: NoMessageValues;
  statusDueSoon: NoMessageValues;
  statusExpired: NoMessageValues;
  statusCancelled: NoMessageValues;
  statusRenewed: NoMessageValues;
  clientUnavailable: NoMessageValues;
  more: { shown: number };
};

export const messages = defineMessages<Values>({
  title: { id: 'subscriptions.list.title', defaultMessage: 'Subscriptions' },
  loading: { id: 'subscriptions.list.loading', defaultMessage: 'Loading…' },
  // The scope declaration (PLATFORM_CORE 3.5) in human phrasing. Under 'all' the tenant's count is the visible count;
  // under 'assigned' no total figure is shown at all (Test 19).
  scopeAll: {
    id: 'subscriptions.list.scopeAll',
    defaultMessage:
      "You are shown all of the organization's subscriptions: {subscriptions, plural, one {# subscription} other {# subscriptions}}.",
  },
  scopeAssigned: {
    id: 'subscriptions.list.scopeAssigned',
    defaultMessage:
      'You are shown only the subscriptions of the clients assigned to you: {clients, plural, one {# client} other {# clients}}, {subscriptions, plural, one {# subscription} other {# subscriptions}}.',
  },
  scopeAssignedEmpty: {
    id: 'subscriptions.list.scopeAssignedEmpty',
    defaultMessage: 'You are shown only the subscriptions of the clients assigned to you, and they have none now.',
  },
  columnService: { id: 'subscriptions.list.columnService', defaultMessage: 'Service' },
  columnClient: { id: 'subscriptions.list.columnClient', defaultMessage: 'Client' },
  columnEndsOn: { id: 'subscriptions.list.columnEndsOn', defaultMessage: 'Ends on' },
  columnStatus: { id: 'subscriptions.list.columnStatus', defaultMessage: 'Status' },
  statusActive: { id: 'subscriptions.list.statusActive', defaultMessage: 'Active' },
  statusDueSoon: { id: 'subscriptions.list.statusDueSoon', defaultMessage: 'Due soon' },
  statusExpired: { id: 'subscriptions.list.statusExpired', defaultMessage: 'Expired' },
  // The lifecycle (Amendment 01 §2.9), shown in place of the date-derived status once the row has left 'active'.
  statusCancelled: { id: 'subscriptions.list.statusCancelled', defaultMessage: 'Cancelled' },
  statusRenewed: { id: 'subscriptions.list.statusRenewed', defaultMessage: 'Renewed' },
  clientUnavailable: { id: 'subscriptions.list.clientUnavailable', defaultMessage: 'Not shown' },
  more: {
    id: 'subscriptions.list.more',
    defaultMessage:
      'Showing the first {shown, plural, one {# subscription} other {# subscriptions}}; there are more within your scope.',
  },
});
