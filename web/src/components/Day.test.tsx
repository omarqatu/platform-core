import { render } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { I18nProvider } from '../i18n/I18nProvider';
import type { Lang } from '../i18n/locale';
import { Day } from './Day';

const ARABIC_INDIC_DIGITS = /[٠-٩۰-۹]/;

// The project owner's item 5: one date format, ISO, in both languages, Western digits.
describe('a calendar day', () => {
  it.each<Lang>(['ar', 'en'])('is yyyy-MM-dd as given, left-to-right, on one line, in %s', (lang) => {
    const time = render(
      <I18nProvider initial={lang}>
        <p>
          <Day day="2027-03-07" />
        </p>
      </I18nProvider>,
    ).container.querySelector('time')!;

    expect(time.getAttribute('datetime')).toBe('2027-03-07');
    expect(time.textContent).toBe('2027-03-07');
    expect(time.textContent).not.toMatch(ARABIC_INDIC_DIGITS);
    expect(time.className).toBe('day');
    expect(time.querySelector('bdi')!.getAttribute('dir')).toBe('ltr');
  });
});
