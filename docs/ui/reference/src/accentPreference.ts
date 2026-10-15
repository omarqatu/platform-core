export const accentIds = ["indigo", "teal", "rust", "blue", "violet"] as const;
export type AccentId = (typeof accentIds)[number];

export const ACCENT_STORAGE_KEY = "platform-core-accent";

export function isAccentId(value: string | null): value is AccentId {
  return value !== null && accentIds.includes(value as AccentId);
}

export function readAccentPreference(): AccentId | null {
  try {
    const value = window.localStorage.getItem(ACCENT_STORAGE_KEY);
    return isAccentId(value) ? value : null;
  } catch {
    return null;
  }
}

export function applyAccentPreference(accent: AccentId | null) {
  if (accent) document.documentElement.dataset.accent = accent;
  else delete document.documentElement.dataset.accent;
}

export function saveAccentPreference(accent: AccentId | null) {
  applyAccentPreference(accent);
  try {
    if (accent) window.localStorage.setItem(ACCENT_STORAGE_KEY, accent);
    else window.localStorage.removeItem(ACCENT_STORAGE_KEY);
  } catch {
    // The in-memory preference still applies when storage is unavailable.
  }
}
