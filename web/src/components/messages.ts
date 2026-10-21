import { defineMessages } from 'react-intl';

export const messages = defineMessages({
  language: { id: 'common.language.label', defaultMessage: 'Language' },
  // Each language is named in itself, whatever the interface's language.
  arabic: { id: 'common.language.arabic', defaultMessage: 'العربية' },
  english: { id: 'common.language.english', defaultMessage: 'English' },
  loading: { id: 'common.status.loading', defaultMessage: 'Loading…' },
  // DateField: the button beside a date's text that opens the browser's own date picker.
  datePick: { id: 'components.dateField.pick', defaultMessage: 'Choose a date' },
  identityLabel: { id: 'identity.bar.label', defaultMessage: 'Your session' },
  signedInAs: { id: 'identity.bar.signedInAs', defaultMessage: 'Signed in as' },
  organization: { id: 'identity.bar.organization', defaultMessage: 'Organization' },
  noOrganization: { id: 'identity.bar.noOrganization', defaultMessage: 'None chosen' },
  switchOrganization: { id: 'identity.bar.switchOrganization', defaultMessage: 'Switch organization' },
  signOut: { id: 'identity.bar.signOut', defaultMessage: 'Sign out' },
  changePassword: { id: 'identity.bar.changePassword', defaultMessage: 'Change password' },
  navigation: { id: 'identity.bar.navigation', defaultMessage: 'Main' },
  subscriptions: { id: 'identity.bar.subscriptions', defaultMessage: 'Subscriptions' },
  clients: { id: 'identity.bar.clients', defaultMessage: 'Clients' },
  // The shell (AppShell): the top bar, the sidebar, the appearance controls.
  skipToContent: { id: 'shell.topbar.skipToContent', defaultMessage: 'Skip to content' },
  brand: { id: 'shell.topbar.brand', defaultMessage: 'Platform' },
  darkMode: { id: 'shell.topbar.darkMode', defaultMessage: 'Dark mode' },
  accentTitle: { id: 'shell.accent.title', defaultMessage: 'Accent color' },
  accentSubtitle: { id: 'shell.accent.subtitle', defaultMessage: 'Personal preference' },
  accentPresets: { id: 'shell.accent.presets', defaultMessage: 'Accent presets' },
  accentReset: { id: 'shell.accent.reset', defaultMessage: 'Reset to organization default' },
  accentContrastNote: {
    id: 'shell.accent.contrastNote',
    defaultMessage:
      'High contrast replaces accent colors with black and white. Your choice is kept and returns when you switch modes.',
  },
  accentIndigo: { id: 'shell.accent.indigo', defaultMessage: 'Indigo' },
  accentTeal: { id: 'shell.accent.teal', defaultMessage: 'Teal' },
  accentRust: { id: 'shell.accent.rust', defaultMessage: 'Rust' },
  accentBlue: { id: 'shell.accent.blue', defaultMessage: 'Blue' },
  accentViolet: { id: 'shell.accent.violet', defaultMessage: 'Violet' },
  // The password rule, said before it is broken (OPEN_ITEMS 62). Shared by every screen that sets a password —
  // the two recovery screens and invitation acceptance — so the rule is explained in one wording everywhere.
  passwordRule: {
    id: 'common.password.rule',
    defaultMessage: 'At least 12 characters. No symbol, digit or capital letter is required.',
  },
});
