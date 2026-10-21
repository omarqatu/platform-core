import { defineMessages, type NoMessageValues } from 'react-intl';

/** The list's filter (the project owner's item 4): one lifecycle status, or all of them. */
export type StatusFilter = 'active' | 'renewed' | 'cancelled' | 'all';

// The values each message takes, so formatMessage checks them (react-intl's typed descriptors).
type Values = {
  title: NoMessageValues;
  loading: NoMessageValues;
  filterLabel: NoMessageValues;
  filterActive: NoMessageValues;
  filterRenewed: NoMessageValues;
  filterCancelled: NoMessageValues;
  filterAll: NoMessageValues;
  scopeAll: { status: StatusFilter; subscriptions: number };
  scopeAssigned: { status: StatusFilter; clients: number; subscriptions: number };
  scopeAssignedEmpty: { status: StatusFilter };
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
  // The filter: which lifecycle status the list shows. "Active" is the default and is not written in the address.
  filterLabel: { id: 'subscriptions.list.filterLabel', defaultMessage: 'Show' },
  filterActive: { id: 'subscriptions.list.filterActive', defaultMessage: 'Active' },
  filterRenewed: { id: 'subscriptions.list.filterRenewed', defaultMessage: 'Renewed' },
  filterCancelled: { id: 'subscriptions.list.filterCancelled', defaultMessage: 'Cancelled' },
  filterAll: { id: 'subscriptions.list.filterAll', defaultMessage: 'All' },
  // The scope declaration (PLATFORM_CORE 3.5) in human phrasing, for the filter in force: it says what is shown,
  // counted after the filter (spec item m). Under 'all' the tenant's count is the visible count; under 'assigned' no
  // total figure is shown at all (Test 19, PROOF_SPEC T8.2). The 'other' branch is the "All" filter.
  scopeAll: {
    id: 'subscriptions.list.scopeAll',
    defaultMessage:
      "{status, select, active {You are shown the organization's active subscriptions: {subscriptions, plural, one {# subscription} other {# subscriptions}}.} renewed {You are shown the organization's renewed subscriptions: {subscriptions, plural, one {# subscription} other {# subscriptions}}.} cancelled {You are shown the organization's cancelled subscriptions: {subscriptions, plural, one {# subscription} other {# subscriptions}}.} other {You are shown all of the organization's subscriptions: {subscriptions, plural, one {# subscription} other {# subscriptions}}.}}",
  },
  scopeAssigned: {
    id: 'subscriptions.list.scopeAssigned',
    defaultMessage:
      '{status, select, active {You are shown only the active subscriptions of the clients assigned to you: {clients, plural, one {# client} other {# clients}}, {subscriptions, plural, one {# subscription} other {# subscriptions}}.} renewed {You are shown only the renewed subscriptions of the clients assigned to you: {clients, plural, one {# client} other {# clients}}, {subscriptions, plural, one {# subscription} other {# subscriptions}}.} cancelled {You are shown only the cancelled subscriptions of the clients assigned to you: {clients, plural, one {# client} other {# clients}}, {subscriptions, plural, one {# subscription} other {# subscriptions}}.} other {You are shown only the subscriptions of the clients assigned to you: {clients, plural, one {# client} other {# clients}}, {subscriptions, plural, one {# subscription} other {# subscriptions}}.}}',
  },
  scopeAssignedEmpty: {
    id: 'subscriptions.list.scopeAssignedEmpty',
    defaultMessage:
      '{status, select, active {You are shown only the active subscriptions of the clients assigned to you, and they have none now.} renewed {You are shown only the renewed subscriptions of the clients assigned to you, and they have none.} cancelled {You are shown only the cancelled subscriptions of the clients assigned to you, and they have none.} other {You are shown only the subscriptions of the clients assigned to you, and they have none now.}}',
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
