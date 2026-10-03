import { defineMessages } from 'react-intl';

export const messages = defineMessages({
  title: { id: 'tenants.select.title', defaultMessage: 'Choose an organization' },
  intro: { id: 'tenants.select.intro', defaultMessage: 'Your active memberships:' },
  entering: { id: 'tenants.select.entering', defaultMessage: 'Entering…' },
  noMembershipTitle: { id: 'tenants.select.noMembershipTitle', defaultMessage: 'No active membership' },
  noMembershipBody: {
    id: 'tenants.select.noMembershipBody',
    defaultMessage:
      'Your account has no active membership in any organization. Ask an organization administrator for an invitation.',
  },
});
