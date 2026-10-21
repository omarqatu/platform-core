import { useCallback, useEffect, useRef } from 'react';

/**
 * Whether a commit is of a tree the person has already left (DEVELOPER_GUIDE §15, OPEN_ITEMS 96). React-router 8's
 * BrowserRouter renders a location change inside startTransition: the address changes at once, and until the new
 * location renders, the old tree can still render again — on a state change, say — with the old location. A guard's
 * redirect in that render would land on top of where the person went.
 *
 * The hook records, at each commit, the address and the location rendered. `isStale()`, called in an effect of a
 * child — whose effects run before this component's, so it sees the previous commit — is true when this commit
 * renders that same location while the address has moved since. A MemoryRouter (the tests) never moves the address,
 * so nothing rendered under one is stale. (Read in an effect, not in render: React's rules keep refs out of render.)
 *
 *   const isStale = useStaleRender(location.pathname + location.search + location.hash);
 *   …
 *   <Redirect to=… isStale={isStale} />  // navigates in its effect unless isStale()
 */
export function useStaleRender(rendered: string): () => boolean {
  const committed = useRef<{ href: string; rendered: string } | null>(null);
  const isStale = useCallback(() => {
    const last = committed.current;
    return last !== null && last.rendered === rendered && last.href !== window.location.href;
  }, [rendered]);
  useEffect(() => {
    committed.current = { href: window.location.href, rendered };
  });
  return isStale;
}
