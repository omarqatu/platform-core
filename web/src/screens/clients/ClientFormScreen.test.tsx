import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { I18nProvider } from '../../i18n/I18nProvider';
import type { Lang } from '../../i18n/locale';
import { ClientFormView, type FormData, type Mode } from './ClientFormScreen';
import { EMPTY } from './clientForm';

const ARABIC_INDIC_DIGITS = /[٠-٩۰-۹]/;

interface Call {
  method: string;
  path: string;
  body: unknown;
}

function api(answer: () => Response) {
  const calls: Call[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      calls.push({
        method: init?.method ?? 'GET',
        path: String(input).replace(/^\/api/, ''),
        body: init?.body === undefined ? undefined : JSON.parse(String(init.body)),
      });
      return answer();
    }),
  );
  return calls;
}

function Where() {
  return <output data-testid="went">{useLocation().pathname}</output>;
}

function show(mode: Mode, data: FormData = { values: EMPTY }, lang: Lang = 'en') {
  return render(
    <I18nProvider initial={lang}>
      <MemoryRouter initialEntries={['/form']}>
        <Routes>
          <Route path="/form" element={<ClientFormView mode={mode} state={{ status: 'ready', data }} />} />
          <Route path="/app/clients" element={<Where />} />
        </Routes>
      </MemoryRouter>
    </I18nProvider>,
  );
}

const change = (label: RegExp, value: string) => fireEvent.change(screen.getByLabelText(label), { target: { value } });

afterEach(() => {
  vi.unstubAllGlobals();
  document.cookie = 'lang=; Path=/; Max-Age=0';
});

describe('creating a client', () => {
  it('sends the name and every channel given, and returns to the list', async () => {
    const calls = api(() => Response.json({ id: 'new' }, { status: 201 }));
    show('create');
    change(/^Name/, 'Nour Co');
    change(/^E-mail/, 'nour@example.com');
    change(/^Phone/, '+٩٦٢٧٩١٢٣٤٥٦٧'); // typed on an Arabic keyboard
    change(/^Telegram chat id/, '-1001234567890');
    fireEvent.click(screen.getByRole('button', { name: 'Create' }));

    await waitFor(() => expect(screen.getByTestId('went').textContent).toBe('/app/clients'));
    expect(calls).toEqual([
      {
        method: 'POST',
        path: '/subscriptions/clients',
        body: {
          name: 'Nour Co',
          email: 'nour@example.com',
          phone_e164: '+962791234567',
          telegram_chat_id: '-1001234567890',
        },
      },
    ]);
  });

  it('sends null for every channel left empty', async () => {
    const calls = api(() => Response.json({ id: 'new' }, { status: 201 }));
    show('create');
    change(/^Name/, 'Only a name');
    fireEvent.click(screen.getByRole('button', { name: 'Create' }));

    await waitFor(() => expect(screen.getByTestId('went').textContent).toBe('/app/clients'));
    expect(calls[0].body).toEqual({ name: 'Only a name', email: null, phone_e164: null, telegram_chat_id: null });
  });

  it('says what is wrong beside the field, and sends nothing', () => {
    const calls = api(() => Response.json({}, { status: 201 }));
    show('create');
    change(/^Name/, 'P');
    change(/^Phone/, '0791234567');
    fireEvent.click(screen.getByRole('button', { name: 'Create' }));

    expect(screen.getByRole('alert').textContent).toBe('Check the fields marked below.');
    expect(screen.getByLabelText(/^Phone/).getAttribute('aria-invalid')).toBe('true');
    expect(calls).toEqual([]);
  });

  it('shows the server’s refusal and stays on the form', async () => {
    api(() => Response.json({ error: 'not_permitted' }, { status: 403 }));
    show('create');
    change(/^Name/, 'Refused');
    fireEvent.click(screen.getByRole('button', { name: 'Create' }));

    await waitFor(() => expect(screen.getByRole('alert').textContent).toBe('You do not have permission to do this.'));
    expect(screen.queryByTestId('went')).toBeNull();
  });
});

describe('editing a client', () => {
  it('sends the four fields to the client’s own path, and never is_active', async () => {
    const calls = api(() => new Response(null, { status: 204 }));
    show('edit', { id: 'c1', values: { name: 'Nour Co', email: '', phoneE164: '+962791234567', telegramChatId: '' } });
    change(/^Name/, 'Nour & Co');
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(screen.getByTestId('went').textContent).toBe('/app/clients'));
    expect(calls).toEqual([
      {
        method: 'PUT',
        path: '/subscriptions/clients/c1',
        body: { name: 'Nour & Co', email: null, phone_e164: '+962791234567', telegram_chat_id: null },
      },
    ]);
    expect(calls[0].body).not.toHaveProperty('is_active');
  });

  // Disabling is another permission on another endpoint: the form offers no way to reach it.
  it('offers no way to disable the client', () => {
    api(() => new Response(null, { status: 204 }));
    show('edit', { id: 'c1', values: { ...EMPTY, name: 'Nour Co' } });

    expect(screen.queryByLabelText(/active/i)).toBeNull();
    expect(screen.queryByRole('button', { name: /disable/i })).toBeNull();
  });
});

describe('in Arabic', () => {
  it('lays out right-to-left and writes the hints in Western digits', () => {
    const { container } = show('create', { values: EMPTY }, 'ar');

    expect(document.documentElement.dir).toBe('rtl');
    expect(container.querySelector('.client-form')).not.toBeNull();
    for (const hint of container.querySelectorAll('.field-hint'))
      expect(hint.textContent).not.toMatch(ARABIC_INDIC_DIGITS);
  });
});
