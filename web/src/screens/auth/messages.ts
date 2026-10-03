import { defineMessages } from 'react-intl';

export const messages = defineMessages({
  title: { id: 'auth.login.title', defaultMessage: 'Sign in' },
  username: { id: 'auth.login.username', defaultMessage: 'Username' },
  password: { id: 'auth.login.password', defaultMessage: 'Password' },
  submit: { id: 'auth.login.submit', defaultMessage: 'Sign in' },
  submitting: { id: 'auth.login.submitting', defaultMessage: 'Signing in…' },
  sessionEnded: { id: 'auth.login.sessionEnded', defaultMessage: 'Your session has ended. Sign in again.' },
});
