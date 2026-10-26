import { defineMessages, type NoMessageValues } from 'react-intl';

// The values each message takes, so formatMessage checks them (react-intl's typed descriptors).
type Values = {
  title: NoMessageValues;
  loading: NoMessageValues;
  count: { members: number };
  columnName: NoMessageValues;
  columnUsername: NoMessageValues;
  columnStatus: NoMessageValues;
  columnRoles: NoMessageValues;
  columnScope: NoMessageValues;
  columnActions: NoMessageValues;
  statusActive: NoMessageValues;
  statusDisabled: NoMessageValues;
  statusLeft: NoMessageValues;
  scopeAll: NoMessageValues;
  scopeAssigned: NoMessageValues;
  noRoles: NoMessageValues;
  ownMembership: NoMessageValues;
  hasLeft: NoMessageValues;
  more: { shown: number };
  working: NoMessageValues;
  actionsFor: { member: string };
  roles: NoMessageValues;
  rolesLabel: { member: string };
  manageScope: NoMessageValues;
  manageScopeLabel: { member: string };
  disable: NoMessageValues;
  disableLabel: { member: string };
  enable: NoMessageValues;
  enableLabel: { member: string };
  confirmDisable: { member: string };
  signInHelp: NoMessageValues;
  signInHelpLabel: { member: string };
  confirmSignInHelp: { member: string };
  signInHelpSent: NoMessageValues;

  invitationsTitle: NoMessageValues;
  invitationsNone: NoMessageValues;
  invite: NoMessageValues;
  columnEmail: NoMessageValues;
  columnRole: NoMessageValues;
  columnIntendedScope: NoMessageValues;
  columnExpires: NoMessageValues;
  invitationPending: NoMessageValues;
  invitationExpired: NoMessageValues;
  invitationExpiredNote: NoMessageValues;
  withdraw: NoMessageValues;
  withdrawLabel: { email: string };
  confirmWithdraw: { email: string };
  invitationActionsFor: { email: string };
  unknownRole: NoMessageValues;
};

/**
 * The member screens (PLATFORM_CORE v1.16 §3.10, §4.5; OPEN_ITEMS 100). Glossary terms throughout: an organization,
 * not a tenant; all clients / assigned clients for the two scope modes; an invitation is withdrawn, never deleted.
 */
export const messages = defineMessages<Values>({
  title: { id: 'members.list.title', defaultMessage: 'Members' },
  loading: { id: 'members.list.loading', defaultMessage: 'Loading…' },
  // No scope declaration sentence: the second axis does not narrow this list (OPEN_ITEMS 104), and whoever cannot
  // read it is refused the endpoint outright (T8.2), so there is no partial view to declare.
  count: {
    id: 'members.list.count',
    defaultMessage: '{members, plural, one {# member} other {# members}} in this organization.',
  },
  columnName: { id: 'members.list.columnName', defaultMessage: 'Name' },
  columnUsername: { id: 'members.list.columnUsername', defaultMessage: 'Username' },
  columnStatus: { id: 'members.list.columnStatus', defaultMessage: 'Status' },
  columnRoles: { id: 'members.list.columnRoles', defaultMessage: 'Roles' },
  columnScope: { id: 'members.list.columnScope', defaultMessage: 'Scope' },
  columnActions: { id: 'members.list.columnActions', defaultMessage: 'Actions' },
  statusActive: { id: 'members.list.statusActive', defaultMessage: 'Active' },
  statusDisabled: { id: 'members.list.statusDisabled', defaultMessage: 'Disabled' },
  statusLeft: { id: 'members.list.statusLeft', defaultMessage: 'Left' },
  scopeAll: { id: 'members.list.scopeAll', defaultMessage: 'All clients' },
  scopeAssigned: { id: 'members.list.scopeAssigned', defaultMessage: 'Assigned clients' },
  noRoles: { id: 'members.list.noRoles', defaultMessage: 'No role' },
  ownMembership: { id: 'members.list.ownMembership', defaultMessage: 'Your own membership' },
  hasLeft: { id: 'members.list.hasLeft', defaultMessage: 'Left, no actions' },
  more: { id: 'members.list.more', defaultMessage: 'The first {shown} are shown.' },
  working: { id: 'members.list.working', defaultMessage: 'Working…' },
  actionsFor: { id: 'members.list.actionsFor', defaultMessage: 'Actions for {member}' },
  roles: { id: 'members.list.roles', defaultMessage: 'Roles' },
  rolesLabel: { id: 'members.list.rolesLabel', defaultMessage: "Roles for {member}" },
  manageScope: { id: 'members.list.manageScope', defaultMessage: 'Scope' },
  manageScopeLabel: { id: 'members.list.manageScopeLabel', defaultMessage: 'Scope for {member}' },
  disable: { id: 'members.list.disable', defaultMessage: 'Disable' },
  disableLabel: { id: 'members.list.disableLabel', defaultMessage: 'Disable {member}' },
  enable: { id: 'members.list.enable', defaultMessage: 'Enable' },
  enableLabel: { id: 'members.list.enableLabel', defaultMessage: 'Enable {member}' },
  signInHelp: { id: 'members.list.signInHelp', defaultMessage: 'Sign-in help' },
  signInHelpLabel: { id: 'members.list.signInHelpLabel', defaultMessage: 'Sign-in help for {member}' },
  // Says what is sent and what is not: a message with the username and the forgot-password page, never a new
  // password and never a link that sets one (item 37 — the member starts the recovery themselves).
  confirmSignInHelp: {
    id: 'members.list.confirmSignInHelp',
    defaultMessage:
      'Send {member} an e-mail with their username and a link to the forgot-password page? Their password is not changed, and no link to set one is sent: they start that themselves.',
  },
  signInHelpSent: { id: 'members.list.signInHelpSent', defaultMessage: 'Sent' },
  confirmDisable: {
    id: 'members.list.confirmDisable',
    defaultMessage:
      'Disable {member}? They will not be able to enter this organization. Their work, their roles and the clients assigned to them are not changed, and you can enable them again.',
  },

  invitationsTitle: { id: 'members.invitations.title', defaultMessage: 'Invitations' },
  invitationsNone: { id: 'members.invitations.none', defaultMessage: 'No invitation is waiting.' },
  invite: { id: 'members.invitations.invite', defaultMessage: 'Invite a member' },
  columnEmail: { id: 'members.invitations.columnEmail', defaultMessage: 'E-mail' },
  columnRole: { id: 'members.invitations.columnRole', defaultMessage: 'Role' },
  columnIntendedScope: { id: 'members.invitations.columnIntendedScope', defaultMessage: 'Scope' },
  columnExpires: { id: 'members.invitations.columnExpires', defaultMessage: 'Expires' },
  invitationPending: { id: 'members.invitations.pending', defaultMessage: 'Waiting' },
  invitationExpired: { id: 'members.invitations.expired', defaultMessage: 'Expired' },
  invitationExpiredNote: { id: 'members.invitations.expiredNote', defaultMessage: 'Expired, no actions' },
  withdraw: { id: 'members.invitations.withdraw', defaultMessage: 'Withdraw' },
  withdrawLabel: { id: 'members.invitations.withdrawLabel', defaultMessage: 'Withdraw the invitation to {email}' },
  confirmWithdraw: {
    id: 'members.invitations.confirmWithdraw',
    defaultMessage: 'Withdraw the invitation to {email}? Its link stops working. You can invite the same address again.',
  },
  invitationActionsFor: { id: 'members.invitations.actionsFor', defaultMessage: 'Actions for the invitation to {email}' },
  unknownRole: { id: 'members.invitations.unknownRole', defaultMessage: 'A role that no longer exists' },
});
