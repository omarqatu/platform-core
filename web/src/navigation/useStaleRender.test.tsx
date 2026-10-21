import { act, fireEvent, render, screen } from '@testing-library/react';
import { useEffect, useState } from 'react';
import { BrowserRouter, Link, MemoryRouter, Route, Routes, useLocation } from 'react-router';
import { afterEach, describe, expect, it } from 'vitest';
import { useStaleRender } from './useStaleRender';

// The hook alone: a commit of the tree the person has left is stale; any other commit is not. The page re-renders
// itself on a button (a state change, as the switch's end is), and a child reports isStale() from its effect.
const seen: boolean[] = [];

function Report({ isStale }: { isStale: () => boolean }) {
  useEffect(() => {
    seen.push(isStale());
  });
  return null;
}

function Page() {
  const location = useLocation();
  const isStale = useStaleRender(location.pathname + location.search + location.hash);
  const [, rerender] = useState(0);
  return (
    <main>
      <h1>Page</h1>
      <button type="button" onClick={() => rerender((n) => n + 1)}>
        Again
      </button>
      <Link to="/elsewhere">Elsewhere</Link>
      <Report isStale={isStale} />
    </main>
  );
}

const routes = (
  <Routes>
    <Route path="/page" element={<Page />} />
    <Route path="/elsewhere" element={<h1>Elsewhere</h1>} />
  </Routes>
);

afterEach(() => {
  seen.length = 0;
  window.history.replaceState(null, '', '/');
});

describe('useStaleRender', () => {
  it('on a BrowserRouter: a render of the left tree, inside the transition, is stale; one that stays is not', async () => {
    window.history.replaceState(null, '', '/page');
    render(<BrowserRouter>{routes}</BrowserRouter>);
    fireEvent.click(screen.getByRole('button', { name: 'Again' }));
    expect(seen).toEqual([false, false]);

    await act(async () => {
      fireEvent.click(screen.getByRole('link', { name: 'Elsewhere' }));
      expect(window.location.pathname).toBe('/elsewhere');
      // The left tree renders again (a state change) before the new location renders: that commit is stale.
      fireEvent.click(screen.getByRole('button', { name: 'Again' }));
      await new Promise((r) => setTimeout(r, 5));
      expect(seen.at(-1)).toBe(true);
    });
    expect(screen.getByRole('heading', { name: 'Elsewhere' })).toBeDefined();
  });

  it('on a MemoryRouter: the address never moves, so no render is stale', () => {
    render(<MemoryRouter initialEntries={['/page']}>{routes}</MemoryRouter>);
    fireEvent.click(screen.getByRole('button', { name: 'Again' }));
    fireEvent.click(screen.getByRole('button', { name: 'Again' }));
    expect(seen).toEqual([false, false, false]);
    fireEvent.click(screen.getByRole('link', { name: 'Elsewhere' }));
    expect(screen.getByRole('heading', { name: 'Elsewhere' })).toBeDefined();
  });
});
