import {
  useCallback,
  useEffect,
  useId,
  useLayoutEffect,
  useRef,
  useState,
  type FocusEvent,
  type KeyboardEvent,
} from 'react';
import { Link } from 'react-router';
import { Icon, type IconName } from './Icon';

/** One of a row's actions: a link to a screen, or something done in place (a button). */
export interface RowAction {
  key: string;
  /** The name shown beside the icon. */
  label: string;
  /** The full accessible name ("Edit VPS"); it begins with the label, so what is seen is what is said. */
  accessibleName?: string;
  icon: IconName;
  /** A destructive action: shown last, apart, in the danger color. */
  danger?: boolean;
  to?: string;
  onSelect?: () => void;
  disabled?: boolean;
}

/** The Popover API puts the menu in the top layer, out of reach of the table's scroll container. */
export const supportsPopover = () => typeof HTMLElement !== 'undefined' && typeof HTMLElement.prototype.showPopover === 'function';

/**
 * A row's actions (the data-table pattern, DEVELOPER_GUIDE §15): one fixed-width button, whatever the language and
 * however many actions, opening a menu of icon-and-name items — so every action is named on touch, where there is no
 * hover. The ARIA menu-button pattern: the button says it has a menu and whether it is open; in the menu the arrow
 * keys move (wrapping), Home and End jump, Escape closes and returns to the button, Tab moves on and the menu closes
 * behind it, and a press outside closes it. A destructive action comes last, apart, in the danger color.
 *
 * The menu is fixed to the viewport beside the button. With the Popover API it is also in the top layer; without
 * it, it is the same fixed menu with no top layer (a browser older than the floor in §15 still opens it — a menu
 * that never opened would be a row with no actions).
 *
 * popover="manual", not "auto", on purpose. The fallback needs its own press-outside and Escape handling whatever
 * the top layer does, so "auto" would not remove that code — it would add a second path beside it. And "auto"'s
 * light dismiss counts a press on this menu's own button as outside: it would close the menu on pointerdown and the
 * button's click would open it again, unless the menu stayed mounted with popoverTarget — a different structure
 * again. With "manual", one path serves both: a press outside or Tab closes it, so opening another row's menu
 * closes this one, as "auto" would have.
 */
export function RowActions({ label, actions }: { label: string; actions: RowAction[] }) {
  const [open, setOpen] = useState(false);
  const [focusFirst, setFocusFirst] = useState<'first' | 'last'>('first');
  const button = useRef<HTMLButtonElement>(null);
  const menu = useRef<HTMLDivElement>(null);
  const id = useId();
  const popover = supportsPopover();

  const ordered = [...actions.filter((a) => !a.danger), ...actions.filter((a) => a.danger)];
  const firstDanger = ordered.findIndex((a) => a.danger);

  const close = useCallback((returnFocus: boolean) => {
    setOpen(false);
    if (returnFocus) button.current?.focus();
  }, []);

  const show = (at: 'first' | 'last') => {
    setFocusFirst(at);
    setOpen(true);
  };

  // Placed below the button, its inline-end edge on the button's — the same logical rule in both directions — or
  // above it when there is no room below. Fixed to the viewport, so a table's scroll container cannot clip it. A
  // fixed menu does not follow its button, so it closes instead of drifting: when a scroll of anything (capture phase
  // — the table's container, the page) has moved the button, and on a resize. "Has moved" because a scroll event
  // arrives on the frame after its scroll: one from before the menu opened can land after, and moved nothing.
  useLayoutEffect(() => {
    const el = menu.current;
    const anchor = button.current;
    if (!open || !el || !anchor) return;
    if (popover) el.showPopover();
    const b = anchor.getBoundingClientRect();
    const viewport = document.documentElement;
    const rtl = getComputedStyle(anchor).direction === 'rtl';
    const fromEnd = rtl ? b.left : viewport.clientWidth - b.right;
    const below = b.bottom + 4;
    const fits = below + el.offsetHeight <= viewport.clientHeight;
    el.style.insetInlineEnd = `${Math.max(0, fromEnd)}px`;
    el.style.insetBlockStart = `${fits ? below : Math.max(0, b.top - 4 - el.offsetHeight)}px`;
    const away = (event: Event) => {
      if (event.type === 'scroll') {
        // The menu's own scroll, if it ever has one, is not the row moving away. (A window's scroll has no Node target.)
        if (event.target instanceof Node && el.contains(event.target)) return;
        const now = anchor.getBoundingClientRect();
        if (Math.abs(now.top - b.top) < 0.5 && Math.abs(now.left - b.left) < 0.5) return;
      }
      close(false);
    };
    window.addEventListener('resize', away);
    window.addEventListener('scroll', away, true);
    return () => {
      window.removeEventListener('resize', away);
      window.removeEventListener('scroll', away, true);
      // A popover removed from the document is hidden already, and hiding a detached one throws.
      if (popover && el.isConnected) {
        try {
          el.hidePopover();
        } catch {
          // already hidden
        }
      }
    };
  }, [open, popover, close]);

  // Focus moves into the menu once it is shown.
  useEffect(() => {
    if (!open) return;
    const items = menuItems(menu.current);
    // Without scrolling: a focus that scrolled the table's container would close the menu it just opened.
    (focusFirst === 'first' ? items[0] : items[items.length - 1])?.focus({ preventScroll: true });
  }, [open, focusFirst]);

  // A press anywhere else closes it — without taking focus back from where the user pressed.
  useEffect(() => {
    if (!open) return;
    const outside = (event: PointerEvent) => {
      const target = event.target as Node;
      if (!menu.current?.contains(target) && !button.current?.contains(target)) close(false);
    };
    document.addEventListener('pointerdown', outside, true);
    return () => document.removeEventListener('pointerdown', outside, true);
  }, [open, close]);

  const onButtonKey = (event: KeyboardEvent<HTMLButtonElement>) => {
    if (event.key === 'ArrowDown') {
      event.preventDefault();
      show('first');
    } else if (event.key === 'ArrowUp') {
      event.preventDefault();
      show('last');
    }
  };

  const onMenuKey = (event: KeyboardEvent<HTMLDivElement>) => {
    const items = menuItems(menu.current);
    const at = items.indexOf(document.activeElement as HTMLElement);
    const move = (to: number) => {
      event.preventDefault();
      items[(to + items.length) % items.length]?.focus({ preventScroll: true });
    };
    switch (event.key) {
      case 'ArrowDown':
        return move(at + 1);
      case 'ArrowUp':
        return move(at - 1);
      case 'Home':
        return move(0);
      case 'End':
        return move(items.length - 1);
      case 'Escape':
        event.preventDefault();
        return close(true);
      case ' ':
        // Space activates a menu item, a link one included.
        if (at >= 0) {
          event.preventDefault();
          items[at]?.click();
        }
        return;
    }
  };

  // Tab, or anything else that moves focus out: the menu closes behind it. Closing on the Tab keydown itself would
  // unmount the focused item before the browser moved focus, and Tab would start again from the top of the page.
  const onMenuBlur = (event: FocusEvent<HTMLDivElement>) => {
    const next = event.relatedTarget as Node | null;
    if (!next || (!menu.current?.contains(next) && next !== button.current)) close(false);
  };

  const activate = (action: RowAction) => {
    close(!action.to);
    action.onSelect?.();
  };

  return (
    <>
      <button
        ref={button}
        type="button"
        className="row-actions-button"
        aria-label={label}
        aria-haspopup="menu"
        aria-expanded={open}
        aria-controls={open ? id : undefined}
        onClick={() => (open ? close(true) : show('first'))}
        onKeyDown={onButtonKey}
      >
        <Icon name="more" size={18} />
      </button>
      {open && (
        <div
          ref={menu}
          id={id}
          role="menu"
          aria-label={label}
          className="row-actions-menu"
          popover={popover ? 'manual' : undefined}
          data-popover={popover ? 'top-layer' : 'fallback'}
          onKeyDown={onMenuKey}
          onBlur={onMenuBlur}
        >
          {ordered.map((action, index) => (
            <MenuItem key={action.key} action={action} separated={index === firstDanger && index > 0} onActivate={activate} />
          ))}
        </div>
      )}
    </>
  );
}

function MenuItem({ action, separated, onActivate }: { action: RowAction; separated: boolean; onActivate: (a: RowAction) => void }) {
  const className = action.danger ? 'row-actions-item danger' : 'row-actions-item';
  const content = (
    <>
      <Icon name={action.icon} size={16} />
      <span>{action.label}</span>
    </>
  );
  return (
    <>
      {separated && <div role="separator" className="row-actions-separator" />}
      {action.to && !action.disabled ? (
        <Link
          to={action.to}
          role="menuitem"
          tabIndex={-1}
          className={className}
          aria-label={action.accessibleName}
          onClick={() => onActivate(action)}
        >
          {content}
        </Link>
      ) : (
        <button
          type="button"
          role="menuitem"
          tabIndex={-1}
          className={className}
          aria-label={action.accessibleName}
          aria-disabled={action.disabled || undefined}
          onClick={() => !action.disabled && onActivate(action)}
        >
          {content}
        </button>
      )}
    </>
  );
}

function menuItems(menu: HTMLElement | null): HTMLElement[] {
  return menu ? [...menu.querySelectorAll<HTMLElement>('[role="menuitem"]')] : [];
}
