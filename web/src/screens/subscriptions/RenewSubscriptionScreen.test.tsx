import { render } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { describe, expect, it } from 'vitest';
import { I18nProvider } from '../../i18n/I18nProvider';
import type { Lang } from '../../i18n/locale';
import { RenewView } from './RenewSubscriptionScreen';
import type { SubscriptionDetail } from './subscriptionForm';

const ARABIC_INDIC_DIGITS = /[٠-٩۰-۹]/;

const subscription: SubscriptionDetail = {
  id: 's1', client_id: 'c1', service_name: 'VPS', ends_on: '2027-03-07', reminder_type: 'subscription', category_id: 'k',
  subcategory_id: null, provider: null, cost: null, currency: null, billing_cycle: 'yearly', auto_renew: false,
  login_url: null, status: 'active', renewed_from_id: null,
};

// The project owner's item 5: the renew screen's current end date in the one format, as the list shows it.
describe('the renew screen', () => {
  it.each<Lang>(['ar', 'en'])('shows the current end date as yyyy-MM-dd, left-to-right, in %s', (lang) => {
    const page = render(
      <I18nProvider initial={lang}>
        <MemoryRouter>
          <RenewView state={{ status: 'ready', subscription }} />
        </MemoryRouter>
      </I18nProvider>,
    ).container;

    const time = page.querySelector('[data-field="currentEnd"] time')!;
    expect(time.textContent).toBe('2027-03-07');
    expect(time.textContent).not.toMatch(ARABIC_INDIC_DIGITS);
    expect(time.querySelector('bdi')!.getAttribute('dir')).toBe('ltr');
  });
});
