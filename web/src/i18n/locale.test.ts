import { describe, expect, it } from 'vitest';
import { initialLang, langCookie, langOfTag, readLangCookie } from './locale';

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
