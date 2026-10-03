/** A refusal from the API: its code, never text (the interface translates it — errorMessage). */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string | null,
  ) {
    super(`API ${status}${code ? ` ${code}` : ''}`);
  }
}

/** The API, same-origin under /api (Vite forwards it in development). */
export async function getJson<T>(path: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch(`/api${path}`, { credentials: 'same-origin', headers: { Accept: 'application/json' }, signal });
  if (!response.ok) {
    const body: unknown = await response.json().catch(() => null);
    const code = body && typeof body === 'object' && 'error' in body && typeof body.error === 'string' ? body.error : null;
    throw new ApiError(response.status, code);
  }
  return (await response.json()) as T;
}
