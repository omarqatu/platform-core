import { createContext, useCallback, useContext, useEffect, useLayoutEffect, useMemo, useState, type ReactNode } from 'react';
import { applyAccentPreference, readAccentPreference, saveAccentPreference, type AccentId } from './accentPreference';
import { cookieStore, type PreferenceStore } from './preferenceStore';
import { applyTheme, CONTRAST_QUERY, DARK_QUERY, readThemePreference, systemTheme, type ThemeMode } from './theme';

interface AppearanceContextValue {
  /** The explicit choice, or null: following the system. */
  themeChoice: ThemeMode | null;
  /** The mode in effect. */
  theme: ThemeMode;
  setTheme: (choice: ThemeMode | null) => void;
  /** The personal accent, or null: the organization default. */
  accent: AccentId | null;
  setAccent: (accent: AccentId | null) => void;
}

const AppearanceContext = createContext<AppearanceContextValue | null>(null);

/**
 * Before the first render (main.tsx): data-theme and data-accent from the cookies and the system, so the first frame
 * already has its colors.
 */
export function applyInitialAppearance(store: PreferenceStore = cookieStore) {
  applyTheme(readThemePreference(store) ?? systemTheme());
  applyAccentPreference(readAccentPreference(store));
}

/**
 * The display mode and the accent: an explicit choice (kept in the store), else the system's mode — followed as it
 * changes. Kept on <html> (data-theme, data-accent) before paint on every change, as I18nProvider does lang and dir.
 */
export function ThemeProvider({ children, store = cookieStore }: { children: ReactNode; store?: PreferenceStore }) {
  const [themeChoice, setThemeChoice] = useState<ThemeMode | null>(() => readThemePreference(store));
  const [system, setSystem] = useState<ThemeMode>(() => systemTheme());
  const [accent, setAccentState] = useState<AccentId | null>(() => readAccentPreference(store));
  const theme = themeChoice ?? system;

  useEffect(() => {
    if (typeof window.matchMedia !== 'function') return;
    const queries = [CONTRAST_QUERY, DARK_QUERY].map((q) => window.matchMedia(q));
    const update = () => setSystem(systemTheme());
    queries.forEach((q) => q.addEventListener('change', update));
    return () => queries.forEach((q) => q.removeEventListener('change', update));
  }, []);

  useLayoutEffect(() => applyTheme(theme), [theme]);
  useLayoutEffect(() => applyAccentPreference(accent), [accent]);

  const setTheme = useCallback(
    (choice: ThemeMode | null) => {
      setThemeChoice(choice);
      store.write('theme', choice);
    },
    [store],
  );

  const setAccent = useCallback(
    (next: AccentId | null) => {
      setAccentState(next);
      saveAccentPreference(store, next);
    },
    [store],
  );

  const value = useMemo(
    () => ({ themeChoice, theme, setTheme, accent, setAccent }),
    [themeChoice, theme, setTheme, accent, setAccent],
  );

  return <AppearanceContext.Provider value={value}>{children}</AppearanceContext.Provider>;
}

/** The display mode and the accent, and their setters — for the appearance controls. */
export function useAppearance(): AppearanceContextValue {
  const value = useContext(AppearanceContext);
  if (!value) throw new Error('useAppearance() is used outside ThemeProvider.');
  return value;
}
