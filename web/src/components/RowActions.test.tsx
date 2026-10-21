import { act, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { RowActions, type RowAction } from './RowActions';

// The data-table pattern's row actions (DEVELOPER_GUIDE §15): the ARIA menu-button pattern, a destructive action
// last and apart, and a menu that opens with or without the Popover API.

function renderActions(actions: RowAction[], label = 'Actions for VPS') {
  return render(
    <MemoryRouter>
      <p>
        <button type="button">before</button>
      </p>
      <RowActions label={label} actions={actions} />
      <p>
        <button type="button">after</button>
      </p>
    </MemoryRouter>,
  );
}

const cancel = vi.fn();
const actions: RowAction[] = [
  // Given first on purpose: the danger action is shown last whatever the order it comes in.
  { key: 'cancel', label: 'Cancel subscription', accessibleName: 'Cancel VPS', icon: 'cancel', danger: true, onSelect: cancel },
  { key: 'edit', label: 'Edit', accessibleName: 'Edit VPS', icon: 'edit', to: '/app/subscriptions/1/edit' },
  { key: 'renew', label: 'Renew', accessibleName: 'Renew VPS', icon: 'renew', to: '/app/subscriptions/1/renew' },
];

const trigger = () => screen.getByRole('button', { name: 'Actions for VPS' });
const items = () => screen.getAllByRole('menuitem');

afterEach(() => {
  cancel.mockReset();
  vi.unstubAllGlobals();
  // The popover stubs below are put on the prototype; remove them so every test starts from jsdom's own (none).
  delete (HTMLElement.prototype as Partial<HTMLElement>).showPopover;
  delete (HTMLElement.prototype as Partial<HTMLElement>).hidePopover;
});

describe('the row-actions button', () => {
  it('is one button, named for its row, that says it has a menu and that it is closed', () => {
    renderActions(actions);

    expect(trigger().getAttribute('aria-haspopup')).toBe('menu');
    expect(trigger().getAttribute('aria-expanded')).toBe('false');
    expect(screen.queryByRole('menu')).toBeNull();
  });

  it('opens on a click, says so, and puts focus on the first item', () => {
    renderActions(actions);

    fireEvent.click(trigger());

    expect(trigger().getAttribute('aria-expanded')).toBe('true');
    expect(screen.getByRole('menu', { name: 'Actions for VPS' }).isConnected).toBe(true);
    expect(trigger().getAttribute('aria-controls')).toBe(screen.getByRole('menu').id);
    expect(document.activeElement).toBe(items()[0]);
  });

  it('opens on ArrowDown at the first item, and on ArrowUp at the last', () => {
    renderActions(actions);

    fireEvent.keyDown(trigger(), { key: 'ArrowDown' });
    expect(document.activeElement).toBe(items()[0]);

    fireEvent.keyDown(screen.getByRole('menu'), { key: 'Escape' });
    fireEvent.keyDown(trigger(), { key: 'ArrowUp' });
    expect(document.activeElement).toBe(items()[items().length - 1]);
  });
});

describe('the menu', () => {
  it('names each action with an icon and a visible word, its accessible name beginning with that word', () => {
    renderActions(actions);
    fireEvent.click(trigger());

    for (const [item, word, name] of [
      [items()[0]!, 'Edit', 'Edit VPS'],
      [items()[1]!, 'Renew', 'Renew VPS'],
      [items()[2]!, 'Cancel subscription', 'Cancel VPS'],
    ] as const) {
      expect(item.textContent).toBe(word);
      expect(screen.getByRole('menuitem', { name })).toBe(item);
      expect(name.startsWith(word.split(' ')[0]!)).toBe(true);
      expect(item.querySelector('svg')!.getAttribute('aria-hidden')).toBe('true');
    }
  });

  it('shows the destructive action last, apart, in the danger style — whatever order it was given in', () => {
    renderActions(actions);
    fireEvent.click(trigger());

    expect(items().map((i) => i.textContent)).toEqual(['Edit', 'Renew', 'Cancel subscription']);
    const separator = screen.getByRole('separator');
    expect(separator.nextElementSibling).toBe(items()[2]);
    expect(items()[2].classList.contains('danger')).toBe(true);
    expect(items()[0].classList.contains('danger')).toBe(false);
  });

  it('moves with the arrow keys, wrapping at both ends, and jumps with Home and End', () => {
    renderActions(actions);
    fireEvent.click(trigger());
    const menu = screen.getByRole('menu');
    const [first, second, last] = items();

    fireEvent.keyDown(menu, { key: 'ArrowDown' });
    expect(document.activeElement).toBe(second);
    fireEvent.keyDown(menu, { key: 'ArrowDown' });
    fireEvent.keyDown(menu, { key: 'ArrowDown' });
    expect(document.activeElement).toBe(first); // wrapped past the end
    fireEvent.keyDown(menu, { key: 'ArrowUp' });
    expect(document.activeElement).toBe(last); // wrapped past the start
    fireEvent.keyDown(menu, { key: 'Home' });
    expect(document.activeElement).toBe(first);
    fireEvent.keyDown(menu, { key: 'End' });
    expect(document.activeElement).toBe(last);
  });

  it('closes on Escape and gives focus back to the button', () => {
    renderActions(actions);
    fireEvent.click(trigger());

    fireEvent.keyDown(screen.getByRole('menu'), { key: 'Escape' });

    expect(screen.queryByRole('menu')).toBeNull();
    expect(trigger().getAttribute('aria-expanded')).toBe('false');
    expect(document.activeElement).toBe(trigger());
  });

  it('closes when focus leaves it (Tab), and leaves focus where it went', () => {
    renderActions(actions);
    fireEvent.click(trigger());
    const after = screen.getByRole('button', { name: 'after' });

    // What Tab does: focus moves on to the next element, out of the menu.
    act(() => after.focus());

    expect(screen.queryByRole('menu')).toBeNull();
    expect(document.activeElement).toBe(after);
  });

  it('closes on a press outside, without taking focus from where the user pressed', () => {
    renderActions(actions);
    fireEvent.click(trigger());
    const before = screen.getByRole('button', { name: 'before' });

    fireEvent.pointerDown(before);
    act(() => before.focus());

    expect(screen.queryByRole('menu')).toBeNull();
    expect(document.activeElement).toBe(before);
  });

  it('stays open on a press inside it', () => {
    renderActions(actions);
    fireEvent.click(trigger());

    fireEvent.pointerDown(items()[1]!);

    expect(screen.getByRole('menu').isConnected).toBe(true);
  });

  it('does an in-place action once, closes, and gives focus back to the button', () => {
    renderActions(actions);
    fireEvent.click(trigger());

    fireEvent.click(screen.getByRole('menuitem', { name: 'Cancel VPS' }));

    expect(cancel).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole('menu')).toBeNull();
    expect(document.activeElement).toBe(trigger());
  });

  it('activates the focused item with Space, a link one included', () => {
    renderActions(actions);
    fireEvent.click(trigger());
    const menu = screen.getByRole('menu');

    fireEvent.keyDown(menu, { key: 'End' });
    fireEvent.keyDown(menu, { key: ' ' });

    expect(cancel).toHaveBeenCalledTimes(1);
  });

  it('makes a link item a link to its screen', () => {
    renderActions(actions);
    fireEvent.click(trigger());

    expect(screen.getByRole('menuitem', { name: 'Edit VPS' }).getAttribute('href')).toBe('/app/subscriptions/1/edit');
  });

  // A fixed menu does not follow its button: once a scroll has moved the button — the page's, or a container's
  // around the table — it closes rather than drift away from its row; and on a resize. jsdom has no layout, so the
  // button's position is moved by hand here.
  it('closes when a scroll moves its button, and on a resize', () => {
    renderActions(actions);
    const scroller = screen.getByRole('button', { name: 'before' }).parentElement!;
    let top = 100;
    vi.spyOn(trigger(), 'getBoundingClientRect').mockImplementation(() => DOMRect.fromRect({ x: 10, y: top, width: 36, height: 36 }));

    fireEvent.click(trigger());
    top = 60; // a container around it scrolled
    fireEvent.scroll(scroller);
    expect(screen.queryByRole('menu')).toBeNull();

    fireEvent.click(trigger());
    top = 20; // the page scrolled
    fireEvent.scroll(window);
    expect(screen.queryByRole('menu')).toBeNull();

    fireEvent.click(trigger());
    act(() => {
      window.dispatchEvent(new Event('resize'));
    });
    expect(screen.queryByRole('menu')).toBeNull();
  });

  // A scroll that leaves the button where it is — the sticky actions column under a sideways scroll of the table, or
  // a scroll event arriving on the frame after a scroll from before the menu opened — leaves the menu beside it, open.
  it('stays open on a scroll that did not move its button', () => {
    renderActions(actions);
    const scroller = screen.getByRole('button', { name: 'before' }).parentElement!;
    vi.spyOn(trigger(), 'getBoundingClientRect').mockImplementation(() => DOMRect.fromRect({ x: 10, y: 100, width: 36, height: 36 }));

    fireEvent.click(trigger());
    fireEvent.scroll(scroller);
    fireEvent.scroll(window);

    expect(screen.getByRole('menu').isConnected).toBe(true);
  });

  it('opening it does not close it: focusing the first item scrolls nothing', () => {
    const focus = vi.spyOn(HTMLElement.prototype, 'focus');
    renderActions(actions);

    fireEvent.click(trigger());

    expect(screen.getByRole('menu').isConnected).toBe(true);
    expect(focus.mock.calls.at(-1)).toEqual([{ preventScroll: true }]);
    focus.mockRestore();
  });

  it('closes on a second click of its button', () => {
    renderActions(actions);
    fireEvent.click(trigger());

    fireEvent.click(trigger());

    expect(screen.queryByRole('menu')).toBeNull();
    expect(document.activeElement).toBe(trigger());
  });
});

describe('with and without the Popover API', () => {
  it('opens without it: the same fixed menu, no top layer (jsdom has none, like a browser below the floor)', () => {
    expect(typeof HTMLElement.prototype.showPopover).toBe('undefined');
    renderActions(actions);

    fireEvent.click(trigger());

    const menu = screen.getByRole('menu');
    expect(menu.hasAttribute('popover')).toBe(false);
    expect(menu.getAttribute('data-popover')).toBe('fallback');
    expect(items()).toHaveLength(3);
    expect(document.activeElement).toBe(items()[0]);
  });

  it('with it: a manual popover, shown on opening and hidden on closing', () => {
    const show = vi.fn();
    const hide = vi.fn();
    Object.assign(HTMLElement.prototype, { showPopover: show, hidePopover: hide });
    renderActions(actions);

    fireEvent.click(trigger());
    // A stubbed showPopover cannot really open it, and jsdom's style sheet hides a popover that is not open, so the
    // menu is found among hidden elements here; a real browser shows it in the top layer.
    const menu = screen.getByRole('menu', { hidden: true });
    expect(menu.getAttribute('popover')).toBe('manual');
    expect(menu.getAttribute('data-popover')).toBe('top-layer');
    expect(show).toHaveBeenCalledTimes(1);
    expect(show.mock.contexts[0]).toBe(menu);

    fireEvent.keyDown(menu, { key: 'Escape' });
    expect(screen.queryByRole('menu')).toBeNull();
    expect(hide.mock.calls.length).toBeLessThanOrEqual(1); // hidden, or already detached — never a throw
  });

  it('places the menu by its inline-end edge, from the viewport', () => {
    renderActions(actions);

    fireEvent.click(trigger());

    const style = screen.getByRole('menu').style;
    expect(style.insetInlineEnd).toMatch(/px$/);
    expect(style.insetBlockStart).toMatch(/px$/);
  });
});
