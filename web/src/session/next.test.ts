import { describe, expect, it } from 'vitest';
import { nextQuery, safeNext } from './next';

describe('safeNext — the return path after signing in', () => {
  it.each(['/', '/subscriptions', '/subscriptions?page=2#top', '/a/b%20c'])('accepts the internal path %s', (path) =>
    expect(safeNext(path)).toBe(path));

  it.each([
    '//evil.com',
    'https://evil.com',
    '/\\evil',
    '/\\/evil.com',
    '\\\\evil.com',
    'evil.com',
    'javascript:alert(1)',
    '/%0a//evil.com\n',
    '/\t/evil.com',
    ' /subscriptions',
    '',
    null,
    undefined,
  ])('refuses %j', (path) => expect(safeNext(path)).toBeNull());

  it('never returns to the login screen itself', () => expect(safeNext('/login?next=/x')).toBeNull());

  it('builds the query only for a safe path other than the root', () => {
    expect(nextQuery('/app/subscriptions')).toBe('?next=%2Fapp%2Fsubscriptions');
    expect(nextQuery('/')).toBe('');
    expect(nextQuery('//evil.com')).toBe('');
  });
});
