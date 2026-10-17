import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  capturePendingResetToken,
  clearPendingResetToken,
  pendingResetToken,
  subscribePendingResetToken,
  watchResetLinks,
} from './pendingResetToken';

/** An address and a history that record what the capture does to them. */
function at(href: string) {
  const url = new URL(href, 'https://interface.test');
  const history = { state: { idx: 0 }, replaceState: vi.fn() } as unknown as History;
  return { location: url as unknown as Location, history };
}

describe('capturePendingResetToken — the token into memory, out of the address', () => {
  afterEach(() => clearPendingResetToken());

  it('takes the token from the fragment, and replaces the history entry with the bare path', () => {
    const { location, history } = at('/app/password/reset#token=secret-token');
    capturePendingResetToken(location, history);
    expect(pendingResetToken()).toBe('secret-token');
    expect(history.replaceState).toHaveBeenCalledWith({ idx: 0 }, '', '/app/password/reset');
  });

  it('removes a token sent in the query string too', () => {
    const { location, history } = at('/app/password/reset?token=secret-token');
    capturePendingResetToken(location, history);
    expect(pendingResetToken()).toBe('secret-token');
    expect(history.replaceState).toHaveBeenCalledWith({ idx: 0 }, '', '/app/password/reset');
  });

  it('holds nothing for the screen opened without a link', () => {
    const { location, history } = at('/app/password/reset');
    capturePendingResetToken(location, history);
    expect(pendingResetToken()).toBeNull();
    expect(history.replaceState).not.toHaveBeenCalled();
  });

  it('touches no other page — the invitation link keeps its own token', () => {
    const { location, history } = at('/app/invitations/accept#tenant=t-1&token=invitation-token');
    capturePendingResetToken(location, history);
    expect(pendingResetToken()).toBeNull();
    expect(history.replaceState).not.toHaveBeenCalled();
  });
});

describe('watchResetLinks — a second link in a tab already on the reset screen', () => {
  afterEach(() => clearPendingResetToken());

  it('captures the new fragment, takes it out of the address, and tells the screen', () => {
    const { location, history } = at('/app/password/reset#token=second-token');
    const target = new EventTarget() as unknown as Window;
    Object.assign(target, { location, history });
    const listener = vi.fn();
    const unsubscribe = subscribePendingResetToken(listener);
    watchResetLinks(target);

    target.dispatchEvent(new Event('hashchange'));

    expect(pendingResetToken()).toBe('second-token');
    expect(history.replaceState).toHaveBeenCalledWith({ idx: 0 }, '', '/app/password/reset');
    expect(listener).toHaveBeenCalledOnce();
    unsubscribe();
  });
});
