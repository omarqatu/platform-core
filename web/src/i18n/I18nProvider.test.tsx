import { act, render, screen } from '@testing-library/react';
import { useIntl } from 'react-intl';
import { afterEach, describe, expect, it } from 'vitest';
import { apiErrors } from './apiErrors';
import { I18nProvider, useLocale } from './I18nProvider';

function Probe() {
  const intl = useIntl();
  const { lang, setLang } = useLocale();
  return (
    <>
      <p data-testid="text">{intl.formatMessage(apiErrors.not_permitted)}</p>
      <p data-testid="number">{intl.formatNumber(1234567.5)}</p>
      <button onClick={() => setLang(lang === 'ar' ? 'en' : 'ar')}>{lang}</button>
    </>
  );
}

function clearCookie() {
  document.cookie = 'lang=; Path=/; Max-Age=0';
}

afterEach(clearCookie);

describe('I18nProvider', () => {
  it('starts from the cookie, and sets lang and dir on the document', () => {
    document.cookie = 'lang=en; Path=/';
    render(<I18nProvider><Probe /></I18nProvider>);
    expect(screen.getByTestId('text').textContent).toBe('You do not have permission to do this.');
    expect(document.documentElement.lang).toBe('en');
    expect(document.documentElement.dir).toBe('ltr');
  });

  it('switches the text, lang, dir and the cookie together', () => {
    render(<I18nProvider initial="en"><Probe /></I18nProvider>);
    act(() => screen.getByRole('button').click());
    expect(screen.getByTestId('text').textContent).toBe('ليست لديك صلاحية لهذا الإجراء.');
    expect(document.documentElement.lang).toBe('ar');
    expect(document.documentElement.dir).toBe('rtl');
    expect(document.cookie).toContain('lang=ar');

    act(() => screen.getByRole('button').click());
    expect(document.documentElement.dir).toBe('ltr');
    expect(document.cookie).toContain('lang=en');
  });

  it('keeps the choice across a reload: a new provider reads the cookie', () => {
    const first = render(<I18nProvider initial="en"><Probe /></I18nProvider>);
    act(() => screen.getByRole('button').click()); // → ar, written to the cookie
    first.unmount();
    render(<I18nProvider><Probe /></I18nProvider>);
    expect(screen.getByRole('button').textContent).toBe('ar');
  });

  it('formats Arabic numbers with Latin digits', () => {
    render(<I18nProvider initial="ar"><Probe /></I18nProvider>);
    const number = screen.getByTestId('number').textContent!;
    expect(number).toMatch(/1.234.567.5/);
    expect(number).not.toMatch(/[٠-٩۰-۹]/);
  });
});
