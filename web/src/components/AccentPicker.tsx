import { useEffect, useId, useRef, useState, type KeyboardEvent } from 'react';
import { useIntl, type MessageDescriptor } from 'react-intl';
import { accentIds, type AccentId } from '../theme/accentPreference';
import { useAppearance } from '../theme/ThemeProvider';
import { Icon } from './Icon';
import { messages } from './messages';

const NAMES: Record<AccentId, MessageDescriptor> = {
  indigo: messages.accentIndigo,
  teal: messages.accentTeal,
  rust: messages.accentRust,
  blue: messages.accentBlue,
  violet: messages.accentViolet,
};

/**
 * The personal accent (docs/ui/reference: the palette button in the global shell). Closed by default; open, a dialog
 * with a radio group of the five presets — the arrows, Home and End move and choose, Escape closes and returns focus to
 * the button, a press outside closes. Choosing applies at once and is kept (useAppearance → the "accent" cookie); the
 * reset removes the choice: the organization default. In high contrast the accent is set aside, and the dialog says so.
 */
export function AccentPicker() {
  const intl = useIntl();
  const { accent, setAccent, theme } = useAppearance();
  const [open, setOpen] = useState(false);
  const buttonRef = useRef<HTMLButtonElement>(null);
  const popoverRef = useRef<HTMLDivElement>(null);
  const optionRefs = useRef<(HTMLButtonElement | null)[]>([]);
  const popoverId = useId();
  const title = intl.formatMessage(messages.accentTitle);

  // On opening: focus on the chosen preset (or the first); a press outside the button and the dialog closes it.
  useEffect(() => {
    if (!open) return;
    optionRefs.current[accent ? accentIds.indexOf(accent) : 0]?.focus();
    const closeOnOutsidePress = (event: PointerEvent) => {
      const target = event.target as Node;
      if (popoverRef.current?.contains(target) || buttonRef.current?.contains(target)) return;
      setOpen(false);
    };
    document.addEventListener('pointerdown', closeOnOutsidePress);
    return () => document.removeEventListener('pointerdown', closeOnOutsidePress);
    // Focus moves on opening only, not on every choice (the arrows move it themselves).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  const close = () => {
    setOpen(false);
    buttonRef.current?.focus();
  };

  const onOptionKeyDown = (event: KeyboardEvent<HTMLButtonElement>, index: number) => {
    const last = accentIds.length - 1;
    // A single column: down and right go to the next preset, up and left to the previous, wrapping (WAI-ARIA radio group).
    const moves: Record<string, number> = {
      ArrowDown: index === last ? 0 : index + 1,
      ArrowRight: index === last ? 0 : index + 1,
      ArrowUp: index === 0 ? last : index - 1,
      ArrowLeft: index === 0 ? last : index - 1,
      Home: 0,
      End: last,
    };
    const next = moves[event.key];
    if (next === undefined) return;
    event.preventDefault();
    setAccent(accentIds[next]!);
    optionRefs.current[next]?.focus();
  };

  return (
    <div className="accent-picker">
      <button
        ref={buttonRef}
        type="button"
        className="icon-button"
        aria-label={title}
        aria-haspopup="dialog"
        aria-expanded={open}
        aria-controls={open ? popoverId : undefined}
        onClick={() => setOpen((o) => !o)}
      >
        <Icon name="palette" />
      </button>
      {open && (
        <div
          id={popoverId}
          ref={popoverRef}
          className="accent-popover"
          role="dialog"
          aria-label={title}
          onKeyDown={(event) => {
            if (event.key !== 'Escape') return;
            event.preventDefault();
            close();
          }}
        >
          <div className="accent-popover-heading">
            <strong>{title}</strong>
            <small>{intl.formatMessage(messages.accentSubtitle)}</small>
          </div>
          {theme === 'contrast' && (
            <p className="accent-contrast-note" role="note">
              <Icon name="warn" size={16} />
              <span>{intl.formatMessage(messages.accentContrastNote)}</span>
            </p>
          )}
          <div className="accent-options" role="radiogroup" aria-label={intl.formatMessage(messages.accentPresets)}>
            {accentIds.map((id, index) => {
              const checked = accent === id;
              return (
                <button
                  key={id}
                  ref={(node) => {
                    optionRefs.current[index] = node;
                  }}
                  type="button"
                  className="accent-option"
                  role="radio"
                  aria-checked={checked}
                  tabIndex={checked || (!accent && index === 0) ? 0 : -1}
                  onClick={() => setAccent(id)}
                  onKeyDown={(event) => onOptionKeyDown(event, index)}
                >
                  <span className={`accent-swatch swatch-${id}${theme === 'contrast' ? ' inactive' : ''}`} />
                  <span>{intl.formatMessage(NAMES[id])}</span>
                  <span className="accent-check">{checked && <Icon name="check" size={16} />}</span>
                </button>
              );
            })}
          </div>
          <button type="button" className="accent-reset" onClick={() => setAccent(null)}>
            {intl.formatMessage(messages.accentReset)}
          </button>
        </div>
      )}
    </div>
  );
}
