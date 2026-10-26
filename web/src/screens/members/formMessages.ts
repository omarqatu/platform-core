import { defineMessages, type NoMessageValues } from 'react-intl';

type Values = {
  loading: NoMessageValues;
  cancel: NoMessageValues;

  inviteTitle: NoMessageValues;
  inviteLede: NoMessageValues;
  email: NoMessageValues;
  emailInvalid: NoMessageValues;
  alreadyMember: NoMessageValues;
  role: NoMessageValues;
  chooseRole: NoMessageValues;
  scope: NoMessageValues;
  scopeAll: NoMessageValues;
  scopeAssigned: NoMessageValues;
  scopeNote: NoMessageValues;
  scopeAllNeedsPermission: NoMessageValues;
  assignedManagerWarning: NoMessageValues;
  send: NoMessageValues;
  sending: NoMessageValues;
  sentTitle: NoMessageValues;
  sentBody: { email: string };
  sentLinkNote: NoMessageValues;
  copyLink: NoMessageValues;
  copied: NoMessageValues;
  backToMembers: NoMessageValues;

  rolesTitle: { member: string };
  rolesLede: NoMessageValues;
  rolesNone: NoMessageValues;
  saving: NoMessageValues;
  done: NoMessageValues;
};

/** The two member forms: inviting someone, and a member's roles (PLATFORM_CORE §4.5, §3.10 D5). */
export const formMessages = defineMessages<Values>({
  loading: { id: 'members.form.loading', defaultMessage: 'Loading…' },
  cancel: { id: 'members.form.cancel', defaultMessage: 'Cancel' },

  inviteTitle: { id: 'members.invite.title', defaultMessage: 'Invite a member' },
  // An invitation is not a membership: the person accepts, and that acceptance is their consent (§0). Said here
  // because "invite" reads like "add" to anyone who has used other software.
  inviteLede: {
    id: 'members.invite.lede',
    defaultMessage:
      'They receive a link and choose to accept it. No one is added to this organization until they do, and the link stops working after seven days.',
  },
  email: { id: 'members.invite.email', defaultMessage: 'E-mail address' },
  emailInvalid: { id: 'members.invite.emailInvalid', defaultMessage: 'Enter one e-mail address, such as name@example.com.' },
  // already_member's own message speaks to the invitee at acceptance; the inviter needs to hear what to do instead.
  alreadyMember: {
    id: 'members.invite.alreadyMember',
    defaultMessage:
      'This address already belongs to a member of this organization. A disabled member is enabled from the member list, not invited again.',
  },
  role: { id: 'members.invite.role', defaultMessage: 'Role' },
  chooseRole: { id: 'members.invite.chooseRole', defaultMessage: 'Choose a role' },
  scope: { id: 'members.invite.scope', defaultMessage: 'What they will see' },
  scopeAll: { id: 'members.invite.scopeAll', defaultMessage: 'All clients' },
  // Item 36: a warning, never a block — the combination is legitimate, and the person may well mean it.
  assignedManagerWarning: {
    id: 'members.invite.assignedManagerWarning',
    defaultMessage: 'This role manages members or scope, but they will see only their assigned clients. If that is what you intend, continue.',
  },
  scopeAssigned: { id: 'members.invite.scopeAssigned', defaultMessage: 'Assigned clients' },
  scopeNote: {
    id: 'members.invite.scopeNote',
    defaultMessage: 'You can change this after they accept, and assign clients to them then.',
  },
  scopeAllNeedsPermission: {
    id: 'members.invite.scopeAllNeedsPermission',
    defaultMessage: 'Only someone who manages scope can invite a member who sees all clients.',
  },
  send: { id: 'members.invite.send', defaultMessage: 'Send the invitation' },
  sending: { id: 'members.invite.sending', defaultMessage: 'Sending…' },
  sentTitle: { id: 'members.invite.sentTitle', defaultMessage: 'The invitation is on its way' },
  sentBody: { id: 'members.invite.sentBody', defaultMessage: 'An invitation was sent to {email}.' },
  sentLinkNote: {
    id: 'members.invite.sentLinkNote',
    defaultMessage:
      'The link is shown here once and is not kept anywhere. Copy it only if you need to pass it on yourself; otherwise you can leave this page.',
  },
  copyLink: { id: 'members.invite.copyLink', defaultMessage: 'Copy the link' },
  copied: { id: 'members.invite.copied', defaultMessage: 'Copied' },
  backToMembers: { id: 'members.invite.backToMembers', defaultMessage: 'Back to the members' },

  rolesTitle: { id: 'members.roles.title', defaultMessage: "{member}'s roles" },
  rolesLede: {
    id: 'members.roles.lede',
    defaultMessage: 'A role decides what they can do. What they can see is their scope, which is set separately.',
  },
  rolesNone: { id: 'members.roles.none', defaultMessage: 'This member holds no role and can do nothing yet.' },
  saving: { id: 'members.roles.saving', defaultMessage: 'Saving…' },
  done: { id: 'members.roles.done', defaultMessage: 'Back to the members' },
});

/** The scope and assignments screen (PLATFORM_CORE §4.8) — the second axis itself. */
export const scopeMessages = defineMessages<{
  title: { member: string };
  loading: NoMessageValues;
  back: NoMessageValues;
  modeLegend: NoMessageValues;
  modeAll: NoMessageValues;
  modeAssigned: NoMessageValues;
  modeAllNote: NoMessageValues;
  modeAssignedNote: NoMessageValues;
  assignmentsTitle: NoMessageValues;
  assignmentsUnderAll: NoMessageValues;
  assignmentsNone: NoMessageValues;
  ownScope: NoMessageValues;
  ownScopeBody: NoMessageValues;
  yourOwnClients: NoMessageValues;
  disabledClient: NoMessageValues;
  withdrawnNote: NoMessageValues;
  assignLabel: { client: string };
  saving: NoMessageValues;
  assignedManager: { member: string };
}>({
  title: { id: 'members.scope.title', defaultMessage: "What {member} sees" },
  loading: { id: 'members.scope.loading', defaultMessage: 'Loading…' },
  back: { id: 'members.scope.back', defaultMessage: 'Back to the members' },
  modeLegend: { id: 'members.scope.modeLegend', defaultMessage: 'Scope' },
  // Item 36: shown whenever the member manages members or scope with scope 'assigned' — on load too, so the page
  // answers "why does this admin see two clients" a week later. Present tense: it describes the state.
  assignedManager: {
    id: 'members.scope.assignedManagerWarning',
    defaultMessage: '{member} manages members or scope, but sees only their assigned clients. If that is what you intend, nothing more is needed.',
  },
  modeAll: { id: 'members.scope.modeAll', defaultMessage: 'All clients' },
  modeAssigned: { id: 'members.scope.modeAssigned', defaultMessage: 'Assigned clients' },
  modeAllNote: {
    id: 'members.scope.modeAllNote',
    defaultMessage: 'They see every client of this organization, and every subscription of those clients.',
  },
  modeAssignedNote: {
    id: 'members.scope.modeAssignedNote',
    defaultMessage: 'They see only the clients assigned below, and nothing about the others — not even how many there are.',
  },
  assignmentsTitle: { id: 'members.scope.assignmentsTitle', defaultMessage: 'Clients' },
  // Kept visible under 'all', not hidden: the rows are permanent and come back into force the moment the mode
  // returns to 'assigned' (§4.8). Hiding them would open visibility nobody had seen.
  assignmentsUnderAll: {
    id: 'members.scope.assignmentsUnderAll',
    defaultMessage: 'This member sees all clients, so these have no effect now. They are kept, and apply again if you set the scope back to assigned clients.',
  },
  assignmentsNone: { id: 'members.scope.assignmentsNone', defaultMessage: 'This organization has no client yet.' },
  ownScope: { id: 'members.scope.ownScope', defaultMessage: 'This is your own membership' },
  ownScopeBody: {
    id: 'members.scope.ownScopeBody',
    defaultMessage: 'No one sets their own scope — it is refused by the database, not only by this page. Ask another member who manages scope.',
  },
  // An assigned manager assigns only what they themselves can see: GET /subscriptions/clients is under the same
  // second axis. Said plainly, so the short list does not look like a fault.
  yourOwnClients: {
    id: 'members.scope.yourOwnClients',
    defaultMessage: 'You are shown the clients within your own scope, so this list may be shorter than the organization’s.',
  },
  disabledClient: { id: 'members.scope.disabledClient', defaultMessage: 'Disabled' },
  withdrawnNote: { id: 'members.scope.withdrawnNote', defaultMessage: 'Withdrawn' },
  assignLabel: { id: 'members.scope.assignLabel', defaultMessage: 'Assign {client}' },
  saving: { id: 'members.scope.saving', defaultMessage: 'Saving…' },
});
