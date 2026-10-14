/**
 * Where the appearance preferences (display mode, accent) are kept: a cookie each, the same mechanism as the
 * language ("lang", locale.ts) — never localStorage or sessionStorage.
 *
 * TODO: a server-side user preference (an endpoint on /me) so the choice follows the user across devices; the cookie
 * then stays as the pre-paint copy.
 */
export type PreferenceName = 'theme' | 'accent';

export interface PreferenceStore {
  read(name: PreferenceName): string | null;
  /** null removes the preference. */
  write(name: PreferenceName, value: string | null): void;
}

const ONE_YEAR_SECONDS = 365 * 24 * 60 * 60;

export function readCookie(cookie: string, name: string): string | null {
  for (const part of cookie.split(';')) {
    const [key, ...value] = part.trim().split('=');
    if (key === name) return decodeURIComponent(value.join('='));
  }
  return null;
}

export function preferenceCookie(name: PreferenceName, value: string | null): string {
  return value === null
    ? `${name}=; Path=/; Max-Age=0; SameSite=Lax`
    : `${name}=${encodeURIComponent(value)}; Path=/; Max-Age=${ONE_YEAR_SECONDS}; SameSite=Lax`;
}

export const cookieStore: PreferenceStore = {
  read: (name) => readCookie(document.cookie, name),
  write: (name, value) => {
    document.cookie = preferenceCookie(name, value);
  },
};
