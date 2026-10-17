import { defineMessages } from 'react-intl';

export const messages = defineMessages({
  // Asking for a recovery link (Amendment 01 §5.1).
  forgotTitle: { id: 'password.forgot.title', defaultMessage: 'Forgot your password' },
  forgotIntro: {
    id: 'password.forgot.intro',
    defaultMessage: 'Enter the email address of your account. If it has one, we send a link to set a new password.',
  },
  email: { id: 'password.forgot.email', defaultMessage: 'Email' },
  forgotSubmit: { id: 'password.forgot.submit', defaultMessage: 'Send the link' },
  forgotSubmitting: { id: 'password.forgot.submitting', defaultMessage: 'Sending…' },
  // The one answer, for an address that has an account and one that does not alike: the API answers the same 204
  // either way, and so must the screen (Amendment 01 §5.1).
  forgotSent: {
    id: 'password.forgot.sent',
    defaultMessage: 'If that email address has an account, a link to set a new password is on its way. The link stops working after a short while, and asking again replaces it.',
  },
  forgotSentHint: {
    id: 'password.forgot.sentHint',
    defaultMessage: 'Nothing arrived? Check the spam folder, or ask again.',
  },
  backToSignIn: { id: 'password.forgot.backToSignIn', defaultMessage: 'Back to sign in' },

  // Setting a password from a recovery link.
  resetTitle: { id: 'password.reset.title', defaultMessage: 'Set a new password' },
  resetIntro: { id: 'password.reset.intro', defaultMessage: 'Choose the password you will sign in with from now on.' },
  newPassword: { id: 'password.reset.newPassword', defaultMessage: 'New password' },
  confirmPassword: { id: 'password.reset.confirmPassword', defaultMessage: 'Repeat the new password' },
  resetSubmit: { id: 'password.reset.submit', defaultMessage: 'Set the password' },
  resetSubmitting: { id: 'password.reset.submitting', defaultMessage: 'Setting…' },
  // One message for a token that is unknown, expired or already used — and for a link opened again after a reload.
  resetInvalid: {
    id: 'password.reset.invalid',
    defaultMessage: 'This link is not valid: it may have expired or already been used. Ask for a new one.',
  },
  askAgain: { id: 'password.reset.askAgain', defaultMessage: 'Ask for a new link' },
  // Changing one's own password, signed in.
  changeTitle: { id: 'password.change.title', defaultMessage: 'Change your password' },
  currentPassword: { id: 'password.change.currentPassword', defaultMessage: 'Current password' },
  changeSubmit: { id: 'password.change.submit', defaultMessage: 'Change the password' },
  changeSubmitting: { id: 'password.change.submitting', defaultMessage: 'Changing…' },
  changeDone: { id: 'password.change.done', defaultMessage: 'Your password has been changed.' },
  // People expect to be signed out here and worry when they are not: the server re-issues this session's cookie
  // with the new security stamp, and every other session of the same user ends (Amendment 01 §2.1).
  changeOtherSessions: {
    id: 'password.change.otherSessions',
    defaultMessage: 'You stay signed in here; everywhere else you were signed in, the session has ended.',
  },
  changeWrongCurrent: {
    id: 'password.change.wrongCurrent',
    defaultMessage: 'The current password is incorrect.',
  },

  // Both screens that set a password: the two fields must match. No other rule is checked, here or anywhere
  // else in the platform (OPEN_ITEMS 62).
  mismatch: { id: 'password.form.mismatch', defaultMessage: 'The two passwords are not the same.' },
});
