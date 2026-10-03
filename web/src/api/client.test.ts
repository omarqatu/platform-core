import { afterEach, describe, expect, it, vi } from 'vitest';
import { abortAll, ApiError, getJson, isAbort, request, setUnauthorizedHandler } from './client';

function fetchReturning(response: Response) {
  const fetch = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>(async () => response);
  vi.stubGlobal('fetch', fetch);
  return fetch;
}

afterEach(() => vi.unstubAllGlobals());

describe('the API client', () => {
  it('sends X-Requested-With on an unsafe request, same-origin under /api', async () => {
    const fetch = fetchReturning(new Response(null, { status: 204 }));
    await request('POST', '/me/leave');
    const [url, init] = fetch.mock.calls[0]!;
    expect(url).toBe('/api/me/leave');
    expect(init!.method).toBe('POST');
    expect(init!.credentials).toBe('same-origin');
    expect((init!.headers as Record<string, string>)['X-Requested-With']).toBe('platform-web');
  });

  it('sends it on a GET too, and a JSON body as JSON', async () => {
    const fetch = fetchReturning(Response.json({ ok: true }));
    await getJson('/tenants');
    expect((fetch.mock.calls[0]![1]!.headers as Record<string, string>)['X-Requested-With']).toBe('platform-web');

    const post = fetchReturning(Response.json({}));
    await request('POST', '/auth/login', { username: 'u' });
    const init = post.mock.calls[0]![1]!;
    expect((init.headers as Record<string, string>)['Content-Type']).toBe('application/json');
    expect(init.body).toBe('{"username":"u"}');
  });

  it('turns a refusal into its code', async () => {
    fetchReturning(Response.json({ error: 'csrf_rejected' }, { status: 403 }));
    await expect(request('POST', '/auth/login', {})).rejects.toEqual(new ApiError(403, 'csrf_rejected'));
  });
});

describe('the API client and the session', () => {
  it('reads a known code from a 500 instead of a generic server error', async () => {
    fetchReturning(Response.json({ error: 'membership_scope_missing' }, { status: 500 }));
    await expect(request('POST', '/tenants/t/select')).rejects.toEqual(new ApiError(500, 'membership_scope_missing'));
  });

  it('has no code for a 500 without a body, so the interface shows "unknown"', async () => {
    fetchReturning(new Response('<html>oops</html>', { status: 500 }));
    await expect(request('GET', '/me')).rejects.toEqual(new ApiError(500, null));
  });

  it("calls the 401 handler for an ended session, never for login's refusal", async () => {
    const handler = vi.fn();
    setUnauthorizedHandler(handler);
    try {
      fetchReturning(Response.json({ error: 'invalid_credentials' }, { status: 401 }));
      await expect(request('POST', '/auth/login', {})).rejects.toBeInstanceOf(ApiError);
      expect(handler).not.toHaveBeenCalled();

      fetchReturning(new Response(null, { status: 401 }));
      await expect(getJson('/subscriptions')).rejects.toEqual(new ApiError(401, null));
      expect(handler).toHaveBeenCalledTimes(1);
    } finally {
      setUnauthorizedHandler(null);
    }
  });

  it('aborts every request in flight on abortAll', async () => {
    let seen: AbortSignal | undefined;
    vi.stubGlobal(
      'fetch',
      vi.fn(
        (_url: RequestInfo | URL, init?: RequestInit) =>
          new Promise<Response>((_, reject) => {
            seen = init!.signal!;
            seen.addEventListener('abort', () => reject(new DOMException('aborted', 'AbortError')));
          }),
      ),
    );
    const pending = getJson('/subscriptions');
    abortAll();
    await expect(pending).rejects.toSatisfy(isAbort);
    expect(seen!.aborted).toBe(true);
  });
});
