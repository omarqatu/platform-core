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
  // After a password was set from a recovery link: that path issues no cookie (Amendment 01 §5.1).
  passwordSet: { id: 'auth.login.passwordSet', defaultMessage: 'Your password is set. Sign in with it.' },
  forgotPassword: { id: 'auth.login.forgotPassword', defaultMessage: 'Forgot your password?' },
});
