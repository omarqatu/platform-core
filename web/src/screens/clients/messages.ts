import { defineMessages, type NoMessageValues } from 'react-intl';

// The values each message takes, so formatMessage checks them (react-intl's typed descriptors).
type Values = {
  title: NoMessageValues;
  loading: NoMessageValues;
  scopeAll: { clients: number };
  scopeAssigned: { clients: number };
  scopeAssignedEmpty: NoMessageValues;
  columnName: NoMessageValues;
  columnStatus: NoMessageValues;
  columnActions: NoMessageValues;
  statusActive: NoMessageValues;
  statusDisabled: NoMessageValues;
  more: { shown: number };
  newClient: NoMessageValues;
  edit: NoMessageValues;
  editLabel: { client: string };
  disable: NoMessageValues;
  disableLabel: { client: string };
  enable: NoMessageValues;
  enableLabel: { client: string };
  actionsFor: { client: string };
  confirmDisable: { client: string };
  working: NoMessageValues;
};

/**
 * The client list (Amendment 01 §2.8, §5.1). A disabled client stays in the list with its badge — it is disabled,
 * not deleted, and hiding it would be the interface filtering what the server shows.
 */
export const messages = defineMessages<Values>({
  title: { id: 'clients.list.title', defaultMessage: 'Clients' },
  loading: { id: 'clients.list.loading', defaultMessage: 'Loading…' },
  // The scope declaration (PLATFORM_CORE 3.5), as the subscription list states it: under 'all' the organization's
  // count is the visible count; under 'assigned' no total figure is shown at all.
  scopeAll: {
    id: 'clients.list.scopeAll',
    defaultMessage: "You are shown all of the organization's clients: {clients, plural, one {# client} other {# clients}}.",
  },
  scopeAssigned: {
    id: 'clients.list.scopeAssigned',
    defaultMessage: 'You are shown only the clients assigned to you: {clients, plural, one {# client} other {# clients}}.',
  },
  scopeAssignedEmpty: {
    id: 'clients.list.scopeAssignedEmpty',
    defaultMessage: 'You are shown only the clients assigned to you, and none are assigned to you now.',
  },
  columnName: { id: 'clients.list.columnName', defaultMessage: 'Client' },
  columnStatus: { id: 'clients.list.columnStatus', defaultMessage: 'Status' },
  columnActions: { id: 'clients.list.columnActions', defaultMessage: 'Actions' },
  statusActive: { id: 'clients.list.statusActive', defaultMessage: 'Active' },
  statusDisabled: { id: 'clients.list.statusDisabled', defaultMessage: 'Disabled' },
  more: {
    id: 'clients.list.more',
    defaultMessage: 'Showing the first {shown, plural, one {# client} other {# clients}}; there are more within your scope.',
  },
  newClient: { id: 'clients.list.newClient', defaultMessage: 'New client' },
  edit: { id: 'clients.list.edit', defaultMessage: 'Edit' },
  editLabel: { id: 'clients.list.editLabel', defaultMessage: 'Edit {client}' },
  disable: { id: 'clients.list.disable', defaultMessage: 'Disable' },
  disableLabel: { id: 'clients.list.disableLabel', defaultMessage: 'Disable {client}' },
  enable: { id: 'clients.list.enable', defaultMessage: 'Enable' },
  enableLabel: { id: 'clients.list.enableLabel', defaultMessage: 'Enable {client}' },
  // The row's actions button (RowActions): which client's actions it opens.
  actionsFor: { id: 'clients.list.actionsFor', defaultMessage: 'Actions for {client}' },
  // Said plainly, because the act is easy to misread: it stops reminders and leaves every subscription as it is.
  confirmDisable: {
    id: 'clients.list.confirmDisable',
    defaultMessage: 'Stop the reminders for {client}? Its subscriptions are not changed, and you can enable it again.',
  },
  working: { id: 'clients.list.working', defaultMessage: 'Working…' },
});
