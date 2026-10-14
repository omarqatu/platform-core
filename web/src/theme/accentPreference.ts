import type { PreferenceStore } from './preferenceStore';

/** The personal accent presets (tokens.css). No preset = the organization default. */
export const accentIds = ['indigo', 'teal', 'rust', 'blue', 'violet'] as const;
export type AccentId = (typeof accentIds)[number];

export function isAccentId(value: unknown): value is AccentId {
  return typeof value === 'string' && (accentIds as readonly string[]).includes(value);
}

export function readAccentPreference(store: PreferenceStore): AccentId | null {
  const value = store.read('accent');
  return isAccentId(value) ? value : null;
}

/** data-accent on <html>; removed for the organization default. */
export function applyAccentPreference(accent: AccentId | null) {
  if (accent) document.documentElement.dataset.accent = accent;
  else delete document.documentElement.dataset.accent;
}

/** Applies the accent and keeps it; null resets to the organization default. */
export function saveAccentPreference(store: PreferenceStore, accent: AccentId | null) {
  applyAccentPreference(accent);
  store.write('accent', accent);
}
