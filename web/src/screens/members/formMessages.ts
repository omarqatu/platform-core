import { defineMessages, type NoMessageValues } from 'react-intl';

type Values = {
  loading: NoMessageValues;
  cancel: NoMessageValues;

  inviteTitle: NoMessageValues;
  inviteLede: NoMessageValues;
  email: NoMessageValues;
  role: NoMessageValues;
  chooseRole: NoMessageValues;
  scope: NoMessageValues;
  scopeAll: NoMessageValues;
  scopeAssigned: NoMessageValues;
  scopeNote: NoMessageValues;
  scopeAllNeedsPermission: NoMessageValues;
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
  role: { id: 'members.invite.role', defaultMessage: 'Role' },
  chooseRole: { id: 'members.invite.chooseRole', defaultMessage: 'Choose a role' },
  scope: { id: 'members.invite.scope', defaultMessage: 'What they will see' },
  scopeAll: { id: 'members.invite.scopeAll', defaultMessage: 'All clients' },
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
