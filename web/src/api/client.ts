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
 * Origin, which the browser adds itself (CsrfProtection, OPEN_ITEMS 28): a custom header that no other origin can send
 * without a CORS preflight.
 */
export const REQUESTED_WITH = { 'X-Requested-With': 'platform-web' } as const;

/** The one way to reach the API: same-origin under /api (Vite forwards it in development), the session cookie included. */
export async function request<T>(method: string, path: string, body?: unknown, signal?: AbortSignal): Promise<T> {
  const headers: Record<string, string> = { Accept: 'application/json', ...REQUESTED_WITH };
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  const response = await fetch(`/api${path}`, {
    method,
    credentials: 'same-origin',
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
    signal,
  });
  if (!response.ok) {
    const error: unknown = await response.json().catch(() => null);
    const code = error && typeof error === 'object' && 'error' in error && typeof error.error === 'string' ? error.error : null;
    throw new ApiError(response.status, code);
  }
  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

export function getJson<T>(path: string, signal?: AbortSignal): Promise<T> {
  return request<T>('GET', path, undefined, signal);
}
