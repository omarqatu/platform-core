/**
 * The invitation being accepted, held in memory only — never in storage, never in the URL. The link is
 * `/app/invitations/accept#tenant=<tenant id>&token=<token>`: the fragment never reaches the server (no access log,
 * no proxy) and is never sent as a referrer. {@link capturePendingInvitation} runs once, before the interface
 * renders: it takes both values out of the address and replaces the history entry with the bare path, so neither
 * the address bar, the history, nor the router ever holds the token. A token sent in the query string by mistake is
 * removed the same way.
 *
 * Memory survives the client-side navigation to the login screen and back ("sign in to accept"); a reload loses it,
 * and the screen then says the invitation is not valid — open the link again. A second link opened in the same tab
 * while the acceptance is shown changes the fragment alone — no page load — so the fragment is watched too
 * ({@link watchInvitationLinks}), and the screen follows the invitation in memory
 * ({@link subscribePendingInvitation}).
 */
export interface PendingInvitation {
  tenantId: string;
  token: string;
}

export const ACCEPT_PATH = '/app/invitations/accept';

let pending: PendingInvitation | null = null;
const listeners = new Set<() => void>();

export function pendingInvitation(): PendingInvitation | null {
  return pending;
}

/** Called when a link brings another invitation into memory. */
export function subscribePendingInvitation(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

/**
 * After the acceptance ends (accepted, or refused for good): nothing is left to accept. Listeners are not called: the
 * screen that ends it is leaving, or shows how it ended.
 */
export function clearPendingInvitation() {
  pending = null;
}

/** Reads the invitation out of the current address, if it is the acceptance's, and removes it from the address. */
export function capturePendingInvitation(location: Location = window.location, history: History = window.history) {
  if (location.pathname !== ACCEPT_PATH) return;
  const fragment = new URLSearchParams(location.hash.replace(/^#/, ''));
  const query = new URLSearchParams(location.search);
  const tenantId = fragment.get('tenant') ?? query.get('tenant');
  const token = fragment.get('token') ?? query.get('token');
  if (location.hash || query.has('token') || query.has('tenant')) history.replaceState(history.state, '', ACCEPT_PATH);
  if (tenantId && token) {
    pending = { tenantId, token };
    listeners.forEach((listener) => listener());
  }
}

/** A link opened in a tab already showing the acceptance: the fragment changes alone, and is captured the same way. */
export function watchInvitationLinks(target: Window = window) {
  target.addEventListener('hashchange', () => capturePendingInvitation(target.location, target.history));
}
