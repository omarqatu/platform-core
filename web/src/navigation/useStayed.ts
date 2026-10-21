import { useCallback, useEffect, useRef } from 'react';

/**
 * Whether the person is still where they were when a request began (DEVELOPER_GUIDE §15; OPEN_ITEMS 94, 96): a
 * navigation that follows an `await` is theirs to skip once they have gone somewhere else, or it takes them back.
 *
 *   const begin = useStayed();
 *   …
 *   const stayed = begin(); // right before the await: the address the request began at
 *   await request(…);
 *   if (stayed()) navigate(…);
 *
 * Two signals, because one is late. React-router 8's BrowserRouter renders a location change inside
 * startTransition: the address changes at once, while the screen that made the request stays mounted a moment
 * longer — so the address is the signal there. A MemoryRouter (the tests) never changes the address — so being
 * unmounted is the signal there.
 */
export function useStayed(): () => () => boolean {
  const mounted = useRef(true);
  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
    };
  }, []);
  return useCallback(() => {
    const from = window.location.href;
    return () => mounted.current && window.location.href === from;
  }, []);
}
