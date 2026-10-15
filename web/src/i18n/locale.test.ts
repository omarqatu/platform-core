import { createIntl } from 'react-intl';
import { describe, expect, it } from 'vitest';
import { INTL_LOCALE, initialLang, langCookie, langOfTag, readLangCookie } from './locale';

describe('initialLang — cookie, then the browser languages, then Arabic', () => {
  it('takes the cookie first', () => expect(initialLang('a=1; lang=en', ['ar-EG'])).toBe('en'));
  it('ignores a cookie that is not a language of the interface', () => expect(initialLang('lang=fr', ['en-US'])).toBe('en'));
  it('takes the first browser language the interface has', () => expect(initialLang('', ['fr-FR', 'en-GB', 'ar'])).toBe('en'));
  it('falls back to Arabic', () => expect(initialLang('', ['fr-FR', 'de'])).toBe('ar'));
  it('falls back to Arabic with nothing at all', () => expect(initialLang('', [])).toBe('ar'));
});

describe('tags and cookie', () => {
  it('reads the primary language of a tag', () => {
    expect(langOfTag('ar-SA')).toBe('ar');
    expect(langOfTag('EN_us')).toBe('en');
    expect(langOfTag('fa')).toBeNull();
  });
  it('writes a cookie it reads back', () => {
    expect(langCookie('en')).toMatch(/^lang=en; Path=\/; Max-Age=\d+; SameSite=Lax$/);
    expect(readLangCookie(langCookie('en').split(';')[0]!)).toBe('en');
  });
});

describe('Arabic numbers and dates — Western digits, Gregorian months', () => {
  const ARABIC_INDIC = /[٠-٩۰-۹]/;
  const intl = createIntl({ locale: INTL_LOCALE.ar, messages: {} });

  it('asks Intl for Latin digits explicitly (a browser defaults Arabic to Arabic-Indic)', () => {
    expect(new Intl.Locale(INTL_LOCALE.ar).numberingSystem).toBe('latn');
  });

  it('formats numbers with 0-9 only', () => {
    const number = intl.formatNumber(1234567.89);
    expect(number).toMatch(/1.234.567.89/);
    expect(number).not.toMatch(ARABIC_INDIC);
    expect(intl.formatNumber(0.5, { style: 'percent' })).not.toMatch(ARABIC_INDIC);
  });

  it('formats dates with 0-9 and Gregorian month names', () => {
    const date = intl.formatDate(new Date('2026-10-15T00:00:00Z'), { year: 'numeric', month: 'long', day: 'numeric', timeZone: 'UTC' });
    expect(date).toContain('أكتوبر');
    expect(date).toContain('15');
    expect(date).toContain('2026');
    expect(date).not.toMatch(ARABIC_INDIC);
    expect(new Intl.DateTimeFormat(INTL_LOCALE.ar).resolvedOptions().calendar).toBe('gregory');
  });
});
