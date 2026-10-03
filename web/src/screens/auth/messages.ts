import { defineMessages } from 'react-intl';

export const messages = defineMessages({
  title: { id: 'auth.login.title', defaultMessage: 'Sign in' },
  username: { id: 'auth.login.username', defaultMessage: 'Username' },
  password: { id: 'auth.login.password', defaultMessage: 'Password' },
  submit: { id: 'auth.login.submit', defaultMessage: 'Sign in' },
  submitting: { id: 'auth.login.submitting', defaultMessage: 'Signing in…' },
  accountCreated: {
    id: 'auth.login.accountCreated',
    defaultMessage: 'Your account has been created and the invitation accepted. Sign in to continue.',
  },
  toAcceptInvitation: { id: 'auth.login.toAcceptInvitation', defaultMessage: 'Sign in to accept the invitation.' },
  sessionEnded: { id: 'auth.login.sessionEnded', defaultMessage: 'Your session has ended. Sign in again.' },
});
