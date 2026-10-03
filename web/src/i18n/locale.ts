/** The interface's two languages. Arabic is the default when nothing else decides. */
export const LANGS = ['ar', 'en'] as const;
export type Lang = (typeof LANGS)[number];
export const DEFAULT_LANG: Lang = 'ar';

/** The cookie that keeps the chosen language across reloads (no per-user setting in v1.16). */
export const LANG_COOKIE = 'lang';
const ONE_YEAR_SECONDS = 365 * 24 * 60 * 60;

/**
 * The locale handed to Intl through IntlProvider. Arabic always takes Latin digits ("-u-nu-latn"), in every Intl format:
 * numbers, plurals, dates.
 */
export const INTL_LOCALE: Record<Lang, string> = { ar: 'ar-u-nu-latn', en: 'en' };

export const DIR: Record<Lang, 'rtl' | 'ltr'> = { ar: 'rtl', en: 'ltr' };

export function isLang(value: unknown): value is Lang {
  return typeof value === 'string' && (LANGS as readonly string[]).includes(value);
}

/** The language of a BCP 47 tag ("ar-EG" → ar, "en-US" → en), or null for any other language. */
export function langOfTag(tag: string): Lang | null {
  const primary = tag.trim().split(/[-_]/)[0]?.toLowerCase();
  return isLang(primary) ? primary : null;
}

export function readLangCookie(cookie: string): Lang | null {
  for (const part of cookie.split(';')) {
    const [name, ...value] = part.trim().split('=');
    if (name === LANG_COOKIE) {
      const lang = decodeURIComponent(value.join('='));
      return isLang(lang) ? lang : null;
    }
  }
  return null;
}

export function langCookie(lang: Lang): string {
  return `${LANG_COOKIE}=${lang}; Path=/; Max-Age=${ONE_YEAR_SECONDS}; SameSite=Lax`;
}

/**
 * The initial language, in order: the "lang" cookie; then the browser's languages — navigator.languages is what the
 * browser sends as Accept-Language, and the interface has no server render to read the header from; then Arabic.
 */
export function initialLang(cookie: string, browserLanguages: readonly string[]): Lang {
  return readLangCookie(cookie) ?? browserLanguages.map(langOfTag).find((lang) => lang !== null) ?? DEFAULT_LANG;
}

export function browserLanguages(): readonly string[] {
  if (typeof navigator === 'undefined') return [];
  return navigator.languages?.length ? navigator.languages : navigator.language ? [navigator.language] : [];
}
