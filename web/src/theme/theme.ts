import type { PreferenceStore } from './preferenceStore';

/** The display modes (tokens.css, data-theme). */
export const themeModes = ['light', 'dark', 'contrast'] as const;
export type ThemeMode = (typeof themeModes)[number];

export function isThemeMode(value: unknown): value is ThemeMode {
  return typeof value === 'string' && (themeModes as readonly string[]).includes(value);
}

/** The user's explicit choice, or null: follow the system. */
export function readThemePreference(store: PreferenceStore): ThemeMode | null {
  const value = store.read('theme');
  return isThemeMode(value) ? value : null;
}

type MatchMedia = (query: string) => { matches: boolean };

export const CONTRAST_QUERY = '(prefers-contrast: more)';
export const DARK_QUERY = '(prefers-color-scheme: dark)';

/** The system's mode: more contrast asked for → high contrast; then a dark color scheme → dark; else light. */
export function systemTheme(matchMedia: MatchMedia | undefined = globalThis.matchMedia?.bind(globalThis)): ThemeMode {
  if (!matchMedia) return 'light';
  if (matchMedia(CONTRAST_QUERY).matches) return 'contrast';
  if (matchMedia(DARK_QUERY).matches) return 'dark';
  return 'light';
}

/** data-theme on <html>. */
export function applyTheme(mode: ThemeMode) {
  document.documentElement.dataset.theme = mode;
}
