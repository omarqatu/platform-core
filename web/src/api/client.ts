/** A refusal from the API: its code, never text (the interface translates it — errorMessage). */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string | null,
  ) {
    super(`API ${status}${code ? ` ${code}` : ''}`);
  }
}

/**
 * Sent on every request. Api refuses any unsafe request (POST, PUT, PATCH, DELETE) without it and without an allowed
 * Origin, which the browser adds itself (CsrfProtection): a custom header that no other origin can send without a CORS
 * preflight.
 */
export const REQUESTED_WITH = { 'X-Requested-With': 'platform-web' } as const;

/** The login path answers a refusal with 401 too; that 401 is a wrong credential, not an ended session. */
const LOGIN_PATH = '/auth/login';

let onUnauthorized: (() => void) | null = null;
let generation = new AbortController();

/** Registered by the session: called for every 401 (but login's), after the request has failed. */
export function setUnauthorizedHandler(handler: (() => void) | null) {
  onUnauthorized = handler;
}

/**
 * Aborts every request in flight. The session calls it when the tenant or the user changes (a switch, a logout, an
 * ended session), so no answer meant for the previous one can arrive afterwards.
 */
export function abortAll() {
  generation.abort();
  generation = new AbortController();
}

/** The one way to reach the API: same-origin, under /api (where every API route lives), the session cookie included. */
export async function request<T>(method: string, path: string, body?: unknown, signal?: AbortSignal): Promise<T> {
  const headers: Record<string, string> = { Accept: 'application/json', ...REQUESTED_WITH };
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  const response = await fetch(`/api${path}`, {
    method,
    credentials: 'same-origin',
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
    signal: signal ? AbortSignal.any([signal, generation.signal]) : generation.signal,
  });
  if (!response.ok) {
    // The code is read whatever the status: a 500 can carry one the interface knows (membership_scope_missing).
    const error: unknown = await response.json().catch(() => null);
    const code = error && typeof error === 'object' && 'error' in error && typeof error.error === 'string' ? error.error : null;
    if (response.status === 401 && path !== LOGIN_PATH) onUnauthorized?.();
    throw new ApiError(response.status, code);
  }
  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

export function getJson<T>(path: string, signal?: AbortSignal): Promise<T> {
  return request<T>('GET', path, undefined, signal);
}

export function isAbort(error: unknown): boolean {
  return error instanceof DOMException && error.name === 'AbortError';
}
