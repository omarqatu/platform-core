import { defineMessages } from 'react-intl';

export const messages = defineMessages({
  language: { id: 'common.language.label', defaultMessage: 'Language' },
  // Each language is named in itself, whatever the interface's language.
  arabic: { id: 'common.language.arabic', defaultMessage: 'العربية' },
  english: { id: 'common.language.english', defaultMessage: 'English' },
  loading: { id: 'common.status.loading', defaultMessage: 'Loading…' },
  identityLabel: { id: 'identity.bar.label', defaultMessage: 'Your session' },
  signedInAs: { id: 'identity.bar.signedInAs', defaultMessage: 'Signed in as' },
  organization: { id: 'identity.bar.organization', defaultMessage: 'Organization' },
  noOrganization: { id: 'identity.bar.noOrganization', defaultMessage: 'None chosen' },
  switchOrganization: { id: 'identity.bar.switchOrganization', defaultMessage: 'Switch organization' },
  signOut: { id: 'identity.bar.signOut', defaultMessage: 'Sign out' },
  navigation: { id: 'identity.bar.navigation', defaultMessage: 'Main' },
  subscriptions: { id: 'identity.bar.subscriptions', defaultMessage: 'Subscriptions' },
});
