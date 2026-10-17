/**
 * The recovery token being used, held in memory only — never in storage, never in the URL. The link the e-mail
 * carries is `/app/password/reset#token=<token>` (`Identity:ForgotPassword:ResetLinkTemplate`): the fragment never
 * reaches the server (no access log, no proxy) and is never sent as a referrer. This is the invitation link's rule
 * (DEVELOPER_GUIDE §9) applied to recovery, and the module is deliberately the same shape as
 * {@link ../invitations/pendingInvitation}: {@link capturePendingResetToken} runs once, before the interface
 * renders, takes the token out of the address and replaces the history entry with the bare path, so neither the
 * address bar, the history, nor the router ever holds it. A token sent in the query string by mistake is removed
 * the same way.
 *
 * A reload loses it, and the screen then says the link is not valid — ask for a new one. A second link opened in
 * the same tab while the screen is shown changes the fragment alone — no page load — so the fragment is watched
 * too ({@link watchResetLinks}), and the screen follows the token in memory
 * ({@link subscribePendingResetToken}).
 */
export const RESET_PATH = '/app/password/reset';

let pending: string | null = null;
const listeners = new Set<() => void>();

export function pendingResetToken(): string | null {
  return pending;
}

/** Called when a link brings another token into memory. */
export function subscribePendingResetToken(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

/**
 * After the reset ends (the password set, or the token refused for good): nothing is left to use. Listeners are not
 * called: the screen that ends it is leaving, or shows how it ended.
 */
export function clearPendingResetToken() {
  pending = null;
}

/** Reads the token out of the current address, if it is the reset screen's, and removes it from the address. */
export function capturePendingResetToken(location: Location = window.location, history: History = window.history) {
  if (location.pathname !== RESET_PATH) return;
  const fragment = new URLSearchParams(location.hash.replace(/^#/, ''));
  const query = new URLSearchParams(location.search);
  const token = fragment.get('token') ?? query.get('token');
  if (location.hash || query.has('token')) history.replaceState(history.state, '', RESET_PATH);
  if (token) {
    pending = token;
    listeners.forEach((listener) => listener());
  }
}

/** A link opened in a tab already showing the screen: the fragment changes alone, and is captured the same way. */
export function watchResetLinks(target: Window = window) {
  target.addEventListener('hashchange', () => capturePendingResetToken(target.location, target.history));
}
