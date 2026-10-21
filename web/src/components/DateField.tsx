import { useRef, useState } from 'react';
import { useIntl } from 'react-intl';
import { isDay, typeDay } from './day';
import { Icon } from './Icon';
import { messages } from './messages';
import './DateField.css';

const HAS_SHOW_PICKER = typeof HTMLInputElement !== 'undefined' && typeof HTMLInputElement.prototype.showPicker === 'function';

/**
 * A calendar day, typed or picked (the project owner's item 6, OPEN_ITEMS 78) — for any screen of any vertical.
 *
 * The field is text, yyyy-MM-dd, left-to-right: a native <input type="date"> draws its value in the browser's own
 * format and language, never the page's (Chromium: "2027/03/07", and an Arabic placeholder on an English page), so it
 * is never what the user reads. The text takes the numeric keypad and writes the hyphens itself (typeDay).
 *
 * Beside it, a button opens the browser's picker on a hidden date input — whose value is always yyyy-MM-dd, by the
 * HTML standard — and the day picked is written to the text. Where showPicker() is missing, or refused, the date
 * input itself lies transparent over the button, so the tap lands on it and the browser opens its picker.
 */
export function DateField({
  id,
  name,
  value,
  onChange,
  invalid,
  describedBy,
}: {
  id: string;
  name?: string;
  value: string;
  onChange: (value: string) => void;
  invalid?: boolean;
  describedBy?: string;
}) {
  const intl = useIntl();
  const picker = useRef<HTMLInputElement>(null);
  const [overlay, setOverlay] = useState(!HAS_SHOW_PICKER);
  const label = intl.formatMessage(messages.datePick);

  const open = () => {
    const input = picker.current;
    if (!input) return;
    input.value = isDay(value) ? value : '';
    try {
      input.showPicker();
    } catch {
      // Refused (no user activation, a frame that may not open it): the next tap lands on the date input itself.
      setOverlay(true);
    }
  };

  return (
    <div className="date-field">
      <input
        id={id}
        name={name}
        type="text"
        inputMode="numeric"
        autoComplete="off"
        dir="ltr"
        value={value}
        aria-invalid={invalid || undefined}
        aria-describedby={describedBy}
        onChange={(e) => onChange(typeDay(value, e.target.value))}
      />
      <span className="date-field-pick">
        <button type="button" className="date-field-button" aria-label={label} onClick={open} tabIndex={overlay ? -1 : undefined} aria-hidden={overlay || undefined}>
          <Icon name="calendar" />
        </button>
        <input
          ref={picker}
          type="date"
          className={overlay ? 'date-field-native overlay' : 'date-field-native'}
          tabIndex={overlay ? undefined : -1}
          aria-hidden={overlay ? undefined : true}
          aria-label={overlay ? label : undefined}
          onFocus={(e) => {
            if (overlay) e.currentTarget.value = isDay(value) ? value : '';
          }}
          onChange={(e) => {
            if (isDay(e.target.value)) onChange(e.target.value);
          }}
        />
      </span>
    </div>
  );
}
