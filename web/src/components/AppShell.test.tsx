import { act, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { App } from '../App';
import { I18nProvider } from '../i18n/I18nProvider';
import { SessionProvider } from '../session/SessionProvider';
import type { PreferenceName, PreferenceStore } from '../theme/preferenceStore';
import { ThemeProvider } from '../theme/ThemeProvider';
import { initials } from './AppShell';

function memoryStore(initial: Partial<Record<PreferenceName, string>> = {}): PreferenceStore & { values: Map<string, string> } {
  const values = new Map<string, string>(Object.entries(initial));
  return {
    values,
    read: (name) => values.get(name) ?? null,
    write: (name, value) => (value === null ? values.delete(name) : values.set(name, value)),
  };
}

/** Signed in as sara, in Al-Amin, with an empty subscription list. */
function fakeApi() {
  const empty = { scope_mode: 'all', visible_count: 0, total_count: 0, has_more_in_scope: false, items: [] };
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const path = String(input).replace(/^\/api/, '');
      if (path === '/me') return Response.json({ username: 'sara', active_tenant: { tenant_id: 't', name: 'Al-Amin' } });
      if (path.startsWith('/subscriptions')) return Response.json(empty);
      return new Response(null, { status: 404 });
    }),
  );
}

async function renderShell(store = memoryStore()) {
  fakeApi();
  render(
    <I18nProvider initial="en">
      <ThemeProvider store={store}>
        <MemoryRouter initialEntries={['/app/subscriptions']}>
          <SessionProvider>
            <App />
          </SessionProvider>
        </MemoryRouter>
      </ThemeProvider>
    </I18nProvider>,
  );
  await screen.findByRole('heading', { name: 'Subscriptions' });
  return store;
}

const html = document.documentElement;

afterEach(() => {
  vi.unstubAllGlobals();
  delete html.dataset.accent;
  delete html.dataset.theme;
  document.cookie = 'lang=; Path=/; Max-Age=0';
});

describe('initials', () => {
  it('takes the first letter of the first two words, or the first two of a single word', () => {
    expect(initials('sara')).toBe('SA');
    expect(initials('omar qatu')).toBe('OQ');
    expect(initials('Al-Amin')).toBe('AA');
    expect(initials('شركة النور')).toBe('شا');
    expect(initials('')).toBe('');
  });
});

describe('the shell', () => {
  it('shows who and where, and marks the current module in the sidebar', async () => {
    await renderShell();
    expect(screen.getByTestId('identity-user').textContent).toBe('sara');
    expect(screen.getByTestId('identity-organization').textContent).toBe('Al-Amin');
    const nav = screen.getByRole('navigation', { name: 'Main' });
    expect(nav.querySelector('a[aria-current="page"]')!.textContent).toBe('Subscriptions');
    expect(screen.getByRole('link', { name: 'Skip to content' }).getAttribute('href')).toBe('#content');
  });

  it('switches to dark mode and back, and keeps the choice', async () => {
    const store = await renderShell();
    const toggle = screen.getByRole('button', { name: 'Dark mode' });
    expect(toggle.getAttribute('aria-pressed')).toBe('false');
    fireEvent.click(toggle);
    expect(html.dataset.theme).toBe('dark');
    expect(store.values.get('theme')).toBe('dark');
    expect(toggle.getAttribute('aria-pressed')).toBe('true');
    fireEvent.click(toggle);
    expect(html.dataset.theme).toBe('light');
  });

  it('switches the language, the other one named in itself', async () => {
    await renderShell();
    fireEvent.click(screen.getByRole('button', { name: 'العربية' }));
    expect(html.dir).toBe('rtl');
    expect(screen.getByRole('button', { name: 'English' }).getAttribute('lang')).toBe('en');
  });
});

describe('the accent picker', () => {
  it('opens on the first preset, moves and chooses with the arrows, and closes on Escape back to its button', async () => {
    const store = await renderShell();
    const button = screen.getByRole('button', { name: 'Accent color' });
    expect(button.getAttribute('aria-expanded')).toBe('false');
    fireEvent.click(button);

    const dialog = screen.getByRole('dialog', { name: 'Accent color' });
    expect(button.getAttribute('aria-controls')).toBe(dialog.id);
    expect(document.activeElement).toBe(screen.getByRole('radio', { name: 'Indigo' }));
    expect(screen.getAllByRole('radio').filter((r) => r.getAttribute('aria-checked') === 'true')).toHaveLength(0);

    fireEvent.keyDown(document.activeElement!, { key: 'ArrowDown' });
    const teal = screen.getByRole('radio', { name: 'Teal' });
    expect(teal.getAttribute('aria-checked')).toBe('true');
    expect(document.activeElement).toBe(teal);
    expect(html.dataset.accent).toBe('teal');
    expect(store.values.get('accent')).toBe('teal');

    fireEvent.keyDown(teal, { key: 'ArrowUp' });
    fireEvent.keyDown(document.activeElement!, { key: 'ArrowUp' });
    expect(html.dataset.accent).toBe('violet'); // wraps from the first to the last
    fireEvent.keyDown(document.activeElement!, { key: 'Home' });
    expect(html.dataset.accent).toBe('indigo');

    fireEvent.keyDown(document.activeElement!, { key: 'Escape' });
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(document.activeElement).toBe(button);
  });

  it('reopens on the chosen preset, and resets to the organization default', async () => {
    const store = await renderShell(memoryStore({ accent: 'rust' }));
    expect(html.dataset.accent).toBe('rust');
    fireEvent.click(screen.getByRole('button', { name: 'Accent color' }));
    expect(document.activeElement).toBe(screen.getByRole('radio', { name: 'Rust' }));
    fireEvent.click(screen.getByRole('button', { name: 'Reset to organization default' }));
    expect(html.dataset.accent).toBeUndefined();
    expect(store.values.has('accent')).toBe(false);
  });

  it('closes on a press outside it', async () => {
    await renderShell();
    fireEvent.click(screen.getByRole('button', { name: 'Accent color' }));
    act(() => {
      fireEvent.pointerDown(screen.getByTestId('identity-user'));
    });
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('says, in high contrast, that the accent is set aside', async () => {
    await renderShell(memoryStore({ theme: 'contrast', accent: 'teal' }));
    fireEvent.click(screen.getByRole('button', { name: 'Accent color' }));
    expect(screen.getByRole('note').textContent).toContain('High contrast replaces accent colors with black and white.');
    expect(screen.getByRole('radio', { name: 'Teal' }).getAttribute('aria-checked')).toBe('true');
  });
});
