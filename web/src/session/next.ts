/**
 * The path to return to after signing in, from ?next=. Only a path inside the interface is accepted: it starts with
 * one "/" — not "//" (another host) nor "/\" (which browsers read as "//") — and holds no backslash, no control
 * character, and resolves to this origin. Anything else is null, never an open redirect.
 */
export function safeNext(raw: string | null | undefined): string | null {
  if (!raw || raw.length > 2048) return null;
  if (!raw.startsWith('/') || raw.startsWith('//')) return null;
  // eslint-disable-next-line no-control-regex
  if (/[\\\u0000-\u001f\u007f]/.test(raw)) return null;
  const base = 'https://interface.invalid';
  let url: URL;
  try {
    url = new URL(raw, base);
  } catch {
    return null;
  }
  if (url.origin !== base) return null;
  const path = url.pathname + url.search + url.hash;
  return path.startsWith('/login') ? null : path;
}

/** The query string that carries a return path to the login or selection screen, if it is safe. */
export function nextQuery(path: string | null): string {
  const next = safeNext(path);
  return next && next !== '/' ? `?next=${encodeURIComponent(next)}` : '';
}
