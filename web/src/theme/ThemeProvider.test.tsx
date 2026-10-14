import { act, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { cookieStore, preferenceCookie, readCookie, type PreferenceName, type PreferenceStore } from './preferenceStore';
import { applyInitialAppearance, ThemeProvider, useAppearance } from './ThemeProvider';
import { systemTheme } from './theme';

function memoryStore(initial: Partial<Record<PreferenceName, string>> = {}): PreferenceStore & { values: Map<string, string> } {
  const values = new Map<string, string>(Object.entries(initial));
  return {
    values,
    read: (name) => values.get(name) ?? null,
    write: (name, value) => (value === null ? values.delete(name) : values.set(name, value)),
  };
}

/** A matchMedia whose answers the test sets, and whose change listeners it can fire. */
function fakeMedia(state: { contrast: boolean; dark: boolean }) {
  const listeners = new Set<() => void>();
  const matchMedia = vi.fn((query: string) => ({
    matches: query.includes('contrast') ? state.contrast : query.includes('dark') ? state.dark : false,
    addEventListener: (_: string, l: () => void) => listeners.add(l),
    removeEventListener: (_: string, l: () => void) => listeners.delete(l),
  }));
  vi.stubGlobal('matchMedia', matchMedia);
  return { change: () => listeners.forEach((l) => l()) };
}

function Probe() {
  const { theme, themeChoice, setTheme, accent, setAccent } = useAppearance();
  return (
    <>
      <p data-testid="state">{`${theme} ${themeChoice ?? 'system'} ${accent ?? 'default'}`}</p>
      <button onClick={() => setTheme('dark')}>dark</button>
      <button onClick={() => setTheme(null)}>system</button>
      <button onClick={() => setAccent('teal')}>teal</button>
      <button onClick={() => setAccent(null)}>reset</button>
    </>
  );
}

const root = document.documentElement;

afterEach(() => {
  vi.unstubAllGlobals();
  delete root.dataset.theme;
  delete root.dataset.accent;
});

describe('systemTheme', () => {
  it('more contrast first, then a dark color scheme, else light', () => {
    expect(systemTheme((q) => ({ matches: q.includes('contrast') || q.includes('dark') }))).toBe('contrast');
    expect(systemTheme((q) => ({ matches: q.includes('dark') }))).toBe('dark');
    expect(systemTheme(() => ({ matches: false }))).toBe('light');
    expect(systemTheme(undefined)).toBe('light');
  });
});

describe('applyInitialAppearance — before the first render', () => {
  it('follows the system with no choice kept', () => {
    fakeMedia({ contrast: false, dark: true });
    applyInitialAppearance(memoryStore());
    expect(root.dataset.theme).toBe('dark');
    expect(root.dataset.accent).toBeUndefined();
  });

  it('takes high contrast when the system asks for more contrast', () => {
    fakeMedia({ contrast: true, dark: true });
    applyInitialAppearance(memoryStore());
    expect(root.dataset.theme).toBe('contrast');
  });

  it('an explicit choice wins over the system; an unknown value is ignored', () => {
    fakeMedia({ contrast: true, dark: true });
    applyInitialAppearance(memoryStore({ theme: 'light', accent: 'rust' }));
    expect(root.dataset.theme).toBe('light');
    expect(root.dataset.accent).toBe('rust');

    applyInitialAppearance(memoryStore({ theme: 'sepia', accent: 'pink' }));
    expect(root.dataset.theme).toBe('contrast');
    expect(root.dataset.accent).toBeUndefined();
  });
});

describe('ThemeProvider', () => {
  it('follows the system as it changes, until a choice is made; then keeps the choice', () => {
    const state = { contrast: false, dark: false };
    const media = fakeMedia(state);
    const store = memoryStore();
    render(<ThemeProvider store={store}><Probe /></ThemeProvider>);
    expect(root.dataset.theme).toBe('light');

    state.dark = true;
    act(() => media.change());
    expect(root.dataset.theme).toBe('dark');
    expect(store.values.size).toBe(0); // following the system writes nothing

    act(() => screen.getByRole('button', { name: 'dark' }).click());
    state.dark = false;
    act(() => media.change());
    expect(screen.getByTestId('state').textContent).toBe('dark dark default');
    expect(store.values.get('theme')).toBe('dark');

    act(() => screen.getByRole('button', { name: 'system' }).click());
    expect(root.dataset.theme).toBe('light');
    expect(store.values.has('theme')).toBe(false);
  });

  it('sets the accent, and resets it to the organization default', () => {
    const store = memoryStore();
    render(<ThemeProvider store={store}><Probe /></ThemeProvider>);
    act(() => screen.getByRole('button', { name: 'teal' }).click());
    expect(root.dataset.accent).toBe('teal');
    expect(store.values.get('accent')).toBe('teal');

    act(() => screen.getByRole('button', { name: 'reset' }).click());
    expect(root.dataset.accent).toBeUndefined();
    expect(store.values.has('accent')).toBe(false);
  });

  it('starts from what the store keeps', () => {
    render(<ThemeProvider store={memoryStore({ theme: 'contrast', accent: 'violet' })}><Probe /></ThemeProvider>);
    expect(screen.getByTestId('state').textContent).toBe('contrast contrast violet');
    expect(root.dataset.theme).toBe('contrast');
    expect(root.dataset.accent).toBe('violet');
  });
});

describe('cookieStore', () => {
  afterEach(() => {
    document.cookie = preferenceCookie('theme', null);
    document.cookie = preferenceCookie('accent', null);
  });

  it('keeps each preference in its own cookie, and never in localStorage or sessionStorage', () => {
    const setItem = vi.spyOn(Storage.prototype, 'setItem');
    cookieStore.write('theme', 'dark');
    cookieStore.write('accent', 'blue');
    expect(cookieStore.read('theme')).toBe('dark');
    expect(cookieStore.read('accent')).toBe('blue');
    cookieStore.write('accent', null);
    expect(cookieStore.read('accent')).toBeNull();
    expect(setItem).not.toHaveBeenCalled();
    setItem.mockRestore();
  });

  it('writes the cookie as lang is written', () => {
    expect(preferenceCookie('theme', 'dark')).toMatch(/^theme=dark; Path=\/; Max-Age=\d+; SameSite=Lax$/);
    expect(preferenceCookie('theme', null)).toBe('theme=; Path=/; Max-Age=0; SameSite=Lax');
    expect(readCookie('lang=ar; theme=contrast', 'theme')).toBe('contrast');
  });
});
