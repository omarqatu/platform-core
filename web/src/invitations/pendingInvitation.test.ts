import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  capturePendingInvitation,
  clearPendingInvitation,
  pendingInvitation,
  subscribePendingInvitation,
  watchInvitationLinks,
} from './pendingInvitation';

/** An address and a history that record what the capture does to them. */
function at(href: string) {
  const url = new URL(href, 'https://interface.test');
  const history = { state: { idx: 0 }, replaceState: vi.fn() } as unknown as History;
  return { location: url as unknown as Location, history };
}

describe('capturePendingInvitation — the token into memory, out of the address', () => {
  afterEach(() => clearPendingInvitation());

  it('takes tenant and token from the fragment, and replaces the history entry with the bare path', () => {
    const { location, history } = at('/app/invitations/accept#tenant=t-1&token=secret-token');
    capturePendingInvitation(location, history);
    expect(pendingInvitation()).toEqual({ tenantId: 't-1', token: 'secret-token' });
    expect(history.replaceState).toHaveBeenCalledWith({ idx: 0 }, '', '/app/invitations/accept');
  });

  it('removes a token sent in the query string too', () => {
    const { location, history } = at('/app/invitations/accept?tenant=t-1&token=secret-token');
    capturePendingInvitation(location, history);
    expect(pendingInvitation()).toEqual({ tenantId: 't-1', token: 'secret-token' });
    expect(history.replaceState).toHaveBeenCalledWith({ idx: 0 }, '', '/app/invitations/accept');
  });

  it('removes an incomplete link from the address, and holds nothing', () => {
    const { location, history } = at('/app/invitations/accept#token=secret-token');
    capturePendingInvitation(location, history);
    expect(pendingInvitation()).toBeNull();
    expect(history.replaceState).toHaveBeenCalledOnce();
  });

  it('touches no other page', () => {
    const { location, history } = at('/login#tenant=t-1&token=secret-token');
    capturePendingInvitation(location, history);
    expect(pendingInvitation()).toBeNull();
    expect(history.replaceState).not.toHaveBeenCalled();
  });
});

describe('watchInvitationLinks — a second link in a tab already on the acceptance', () => {
  afterEach(() => clearPendingInvitation());

  it('captures the new fragment, takes it out of the address, and tells the screen', () => {
    const { location, history } = at('/app/invitations/accept#tenant=t-2&token=second-token');
    const target = new EventTarget() as unknown as Window;
    Object.assign(target, { location, history });
    const listener = vi.fn();
    const unsubscribe = subscribePendingInvitation(listener);
    watchInvitationLinks(target);

    target.dispatchEvent(new Event('hashchange'));

    expect(pendingInvitation()).toEqual({ tenantId: 't-2', token: 'second-token' });
    expect(history.replaceState).toHaveBeenCalledWith({ idx: 0 }, '', '/app/invitations/accept');
    expect(listener).toHaveBeenCalledOnce();
    unsubscribe();
  });
});
