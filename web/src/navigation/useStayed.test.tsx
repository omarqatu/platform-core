import { act, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { BrowserRouter, Link, MemoryRouter, Route, Routes } from 'react-router';
import { useStayed } from './useStayed';

// The hook alone, on each router: the screen that began a request asks, when it answers, whether the person is
// still there. Each of its two signals is the one that answers on one router.
let stayed: () => boolean = () => true;

function Here() {
  const begin = useStayed();
  return (
    <main>
      <h1>Here</h1>
      <button type="button" onClick={() => (stayed = begin())}>
        Begin
      </button>
      <Link to="/elsewhere">Elsewhere</Link>
    </main>
  );
}

const routes = (
  <Routes>
    <Route path="/here" element={<Here />} />
    <Route path="/elsewhere" element={<h1>Elsewhere</h1>} />
  </Routes>
);

afterEach(() => window.history.replaceState(null, '', '/'));

describe('useStayed', () => {
  it('on a MemoryRouter: stayed while nothing moves, not once the screen is gone (the unmount signal)', () => {
    render(<MemoryRouter initialEntries={['/here']}>{routes}</MemoryRouter>);

    fireEvent.click(screen.getByRole('button', { name: 'Begin' }));
    expect(stayed()).toBe(true);
    const href = window.location.href;

    fireEvent.click(screen.getByRole('link', { name: 'Elsewhere' }));
    expect(screen.getByRole('heading', { name: 'Elsewhere' })).toBeDefined();
    expect(window.location.href).toBe(href); // the address is not this router's: only unmounting can say it
    expect(stayed()).toBe(false);
  });

  it('on a BrowserRouter: not stayed as soon as the address moves, though the screen is still mounted', async () => {
    window.history.replaceState(null, '', '/here');
    render(<BrowserRouter>{routes}</BrowserRouter>);

    fireEvent.click(screen.getByRole('button', { name: 'Begin' }));
    expect(stayed()).toBe(true);

    await act(async () => {
      fireEvent.click(screen.getByRole('link', { name: 'Elsewhere' }));
      // Inside one act React holds the render: the address is the new one, the screen that began is still here —
      // the window the browser has while the router renders the move inside a transition.
      expect(window.location.pathname).toBe('/elsewhere');
      expect(screen.getByRole('heading', { name: 'Here' })).toBeDefined();
      expect(stayed()).toBe(false);
    });
    expect(screen.getByRole('heading', { name: 'Elsewhere' })).toBeDefined();
  });
});
