import { defineMessages } from 'react-intl';

export const messages = defineMessages({
  title: { id: 'invitations.accept.title', defaultMessage: 'Accept the invitation' },
  newAccountIntro: {
    id: 'invitations.accept.newAccountIntro',
    defaultMessage: 'Create your account to accept the invitation. Use the email address the invitation was sent to.',
  },
  fullName: { id: 'invitations.accept.fullName', defaultMessage: 'Full name' },
  email: { id: 'invitations.accept.email', defaultMessage: 'Email' },
  username: { id: 'invitations.accept.username', defaultMessage: 'Username' },
  password: { id: 'invitations.accept.password', defaultMessage: 'Password' },
  createAndAccept: { id: 'invitations.accept.createAndAccept', defaultMessage: 'Create account and accept' },
  haveAccount: { id: 'invitations.accept.haveAccount', defaultMessage: 'Already have an account?' },
  signInToAccept: { id: 'invitations.accept.signInToAccept', defaultMessage: 'Sign in to accept' },
  signedInIntro: {
    id: 'invitations.accept.signedInIntro',
    defaultMessage: 'Accepting adds the organization that invited you to your organizations.',
  },
  accept: { id: 'invitations.accept.accept', defaultMessage: 'Accept the invitation' },
  accepting: { id: 'invitations.accept.accepting', defaultMessage: 'Accepting…' },
  // One message for a token that is unknown, expired or already used — and for a link opened again after a reload.
  invalid: {
    id: 'invitations.accept.invalid',
    defaultMessage: 'This invitation link is not valid: it may have expired or already been used. Ask for a new invitation.',
  },
  toOrganizations: { id: 'invitations.accept.toOrganizations', defaultMessage: 'Go to your organizations' },
});
