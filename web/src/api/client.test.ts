import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiError, getJson, request } from './client';

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
