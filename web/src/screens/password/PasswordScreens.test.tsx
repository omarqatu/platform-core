import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { setUnauthorizedHandler } from '../../api/client';
import { I18nProvider } from '../../i18n/I18nProvider';
import type { Lang } from '../../i18n/locale';
import { capturePendingResetToken, clearPendingResetToken } from '../../password/pendingResetToken';
import { ChangePasswordScreen } from './ChangePasswordScreen';
import { ForgotPasswordScreen } from './ForgotPasswordScreen';
import { ResetPasswordScreen } from './ResetPasswordScreen';

interface Call {
  method: string;
  path: string;
  body: unknown;
}

/** The API as these screens reach it: the calls it received, and the answer the test chose. */
function api(answer: () => Response | Promise<Response>) {
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

const noContent = () => new Response(null, { status: 204 });
const refusal = (status: number, code: string) => () => Response.json({ error: code }, { status });

/** Where a screen navigated to, and with what it got there. */
function Probe() {
  const location = useLocation();
  return <output data-testid="went">{`${location.pathname} ${JSON.stringify(location.state)}`}</output>;
}

function show(node: ReactNode, lang: Lang = 'en') {
  return render(
    <I18nProvider initial={lang}>
      <MemoryRouter initialEntries={['/start']}>
        <Routes>
          <Route path="/start" element={node} />
          <Route path="/login" element={<Probe />} />
        </Routes>
      </MemoryRouter>
    </I18nProvider>,
  );
}

function type(label: RegExp, value: string) {
  fireEvent.change(screen.getByLabelText(label), { target: { value } });
}

afterEach(() => {
  vi.unstubAllGlobals();
  setUnauthorizedHandler(null);
  clearPendingResetToken();
  cleanup();
  document.cookie = 'lang=; Path=/; Max-Age=0';
});

describe('forgot — one answer for every address (Amendment 01 §5.1)', () => {
  const ask = async (address: string) => {
    type(/email/i, address);
    fireEvent.click(screen.getByRole('button', { name: /send the link/i }));
    await screen.findByRole('status');
    return screen.getByRole('status').textContent ?? '';
  };

  it('says the same thing for an address with an account and one without, and never shows the address', async () => {
    api(noContent);
    show(<ForgotPasswordScreen />);
    const first = await ask('known@example.test');
    cleanup();
    show(<ForgotPasswordScreen />);
    const second = await ask('nobody@example.test');

    expect(first).toBe(second);
    expect(first).not.toContain('known');
    expect(first).not.toContain('nobody');
  });

  it('leaves the screen where it is: no redirect carries the answer', async () => {
    api(noContent);
    show(<ForgotPasswordScreen />);
    await ask('someone@example.test');
    expect(screen.queryByTestId('went')).toBeNull();
    // The form is replaced by the one panel, so a second ask starts over rather than reporting on the first.
    expect(screen.queryByLabelText(/email/i)).toBeNull();
  });

  it('sends the address once, to the forgot path', async () => {
    const calls = api(noContent);
    show(<ForgotPasswordScreen />);
    await ask('someone@example.test');
    expect(calls).toEqual([
      { method: 'POST', path: '/auth/password/forgot', body: { email: 'someone@example.test' } },
    ]);
  });

  it('shows the ip ceiling’s refusal, which depends on this caller alone (§5.2)', async () => {
    api(refusal(429, 'too_many_attempts'));
    show(<ForgotPasswordScreen />);
    type(/email/i, 'someone@example.test');
    fireEvent.click(screen.getByRole('button', { name: /send the link/i }));
    expect((await screen.findByRole('alert')).textContent).toMatch(/too many attempts/i);
  });

  it('is Arabic and right-to-left when the interface is', async () => {
    api(noContent);
    const { container } = show(<ForgotPasswordScreen />, 'ar');
    expect(container.querySelector('h1')?.textContent).toBe('نسيت كلمة السر');
    expect(document.documentElement.dir).toBe('rtl');
  });
});

describe('reset — the token from the fragment, one message for a token that cannot be used', () => {
  const link = (token: string) => {
    const url = new URL(`/app/password/reset#token=${token}`, 'https://interface.test');
    capturePendingResetToken(url as unknown as Location, { state: null, replaceState: vi.fn() } as unknown as History);
  };

  it('refuses to show the form with no token in memory, and offers a new link', () => {
    api(noContent);
    show(<ResetPasswordScreen />);
    expect(screen.getByRole('alert').textContent).toMatch(/not valid/i);
    expect(screen.getByRole('link', { name: /ask for a new link/i }).getAttribute('href')).toBe(
      '/app/password/forgot',
    );
    expect(screen.queryByLabelText(/new password/i)).toBeNull();
  });

  it('sends the token from memory with the new password, and ends on the sign-in screen', async () => {
    const calls = api(noContent);
    link('secret-token');
    show(<ResetPasswordScreen />);
    type(/^new password$/i, 'a new password');
    type(/repeat the new password/i, 'a new password');
    fireEvent.click(screen.getByRole('button', { name: /set the password/i }));

    await waitFor(() => expect(screen.getByTestId('went').textContent).toContain('/login'));
    expect(screen.getByTestId('went').textContent).toContain('"passwordSet":true');
    expect(calls).toEqual([
      {
        method: 'POST',
        path: '/auth/password/reset',
        body: { token: 'secret-token', new_password: 'a new password' },
      },
    ]);
  });

  it('keeps unknown, used and expired one message — and stops offering the form', async () => {
    api(refusal(400, 'invalid_token'));
    link('secret-token');
    show(<ResetPasswordScreen />);
    type(/^new password$/i, 'a new password');
    type(/repeat the new password/i, 'a new password');
    fireEvent.click(screen.getByRole('button', { name: /set the password/i }));

    expect((await screen.findByRole('alert')).textContent).toMatch(
      /not valid: it may have expired or already been used/i,
    );
    expect(screen.queryByLabelText(/^new password$/i)).toBeNull();
  });

  it('asks nothing of the API when the two fields differ', () => {
    const calls = api(noContent);
    link('secret-token');
    show(<ResetPasswordScreen />);
    type(/^new password$/i, 'one password');
    type(/repeat the new password/i, 'another password');
    fireEvent.click(screen.getByRole('button', { name: /set the password/i }));

    expect(screen.getByRole('alert').textContent).toMatch(/not the same/i);
    expect(calls).toEqual([]);
  });
});

describe('change — this session survives, every other one ends', () => {
  const fill = (current = 'the current one') => {
    type(/current password/i, current);
    type(/^new password$/i, 'a new password');
    type(/repeat the new password/i, 'a new password');
    fireEvent.click(screen.getByRole('button', { name: /change the password/i }));
  };

  it('sends both passwords and says what happened to the other sessions', async () => {
    const calls = api(noContent);
    show(<ChangePasswordScreen />);
    fill();

    expect((await screen.findByRole('status')).textContent).toMatch(
      /You stay signed in here; everywhere else you were signed in, the session has ended/i,
    );
    expect(calls).toEqual([
      {
        method: 'PUT',
        path: '/me/password',
        body: { current_password: 'the current one', new_password: 'a new password' },
      },
    ]);
  });

  it('a wrong current password is not an ended session: the screen says so and the session stands', async () => {
    api(refusal(401, 'invalid_credentials'));
    const ended = vi.fn();
    setUnauthorizedHandler(ended);
    show(<ChangePasswordScreen />);
    fill('not the current one');

    expect((await screen.findByRole('alert')).textContent).toMatch(/current password is incorrect/i);
    expect(ended).not.toHaveBeenCalled();
  });

  it('a stale security stamp on the same request does end the session (§2.1)', async () => {
    api(refusal(401, 'session_expired'));
    const ended = vi.fn();
    setUnauthorizedHandler(ended);
    show(<ChangePasswordScreen />);
    fill();

    await waitFor(() => expect(ended).toHaveBeenCalledOnce());
  });

  it('is Arabic and right-to-left when the interface is', async () => {
    const { container } = show(<ChangePasswordScreen />, 'ar');
    expect(container.querySelector('h1')?.textContent).toBe('تغيير كلمة السر');
    expect(document.documentElement.dir).toBe('rtl');
  });
});
