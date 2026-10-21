import { fireEvent, render, screen } from '@testing-library/react';
import { useState } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { I18nProvider } from '../i18n/I18nProvider';
import type { Lang } from '../i18n/locale';

// DateField reads showPicker's presence once, when it loads: each test loads it afresh for the browser it pretends.
async function field(options: { showPicker?: () => void; lang?: Lang; initial?: string } = {}) {
  vi.resetModules();
  const proto = HTMLInputElement.prototype as unknown as { showPicker?: () => void };
  if (options.showPicker) proto.showPicker = options.showPicker;
  else delete proto.showPicker;
  const { DateField } = await import('./DateField');
  const seen: string[] = [];
  function Host() {
    const [value, setValue] = useState(options.initial ?? '');
    return (
      <DateField
        id="ends"
        value={value}
        onChange={(v) => {
          seen.push(v);
          setValue(v);
        }}
      />
    );
  }
  const view = render(
    <I18nProvider initial={options.lang ?? 'en'}>
      <Host />
    </I18nProvider>,
  );
  const text = view.container.querySelector<HTMLInputElement>('#ends')!;
  const native = view.container.querySelector<HTMLInputElement>('input[type="date"]')!;
  return { view, text, native, seen };
}

afterEach(() => {
  delete (HTMLInputElement.prototype as unknown as { showPicker?: () => void }).showPicker;
});

describe('DateField', () => {
  it('is a left-to-right text field on the numeric keypad, and eight digits typed become yyyy-MM-dd', async () => {
    const { text } = await field();
    expect(text.type).toBe('text');
    expect(text.inputMode).toBe('numeric');
    expect(text.dir).toBe('ltr');

    for (const key of '20270307') fireEvent.change(text, { target: { value: text.value + key } });

    expect(text.value).toBe('2027-03-07');
  });

  it('opens the picker with showPicker() from a 44 px button named in the language, and writes the day picked as ISO', async () => {
    const showPicker = vi.fn();
    const { native, text, view } = await field({ showPicker, lang: 'ar', initial: '2027-03-07' });
    const button = screen.getByRole('button', { name: 'اختيار تاريخ' });

    fireEvent.click(button);

    expect(showPicker).toHaveBeenCalledTimes(1);
    expect(native.value).toBe('2027-03-07'); // the picker opens on the day already typed
    expect(native.getAttribute('aria-hidden')).toBe('true');
    expect(native.tabIndex).toBe(-1);
    fireEvent.change(native, { target: { value: '2028-01-31' } });
    expect(text.value).toBe('2028-01-31');
    expect(view.container.querySelector('.overlay')).toBeNull();
  });

  it('lays the date input over the button where showPicker() is missing, named as the button was', async () => {
    const { native, text } = await field();

    expect(native.className).toContain('overlay');
    expect(native.getAttribute('aria-label')).toBe('Choose a date');
    expect(native.getAttribute('aria-hidden')).toBeNull();
    expect(screen.getByRole('button', { hidden: true }).getAttribute('aria-hidden')).toBe('true');
    fireEvent.change(native, { target: { value: '2027-12-31' } });
    expect(text.value).toBe('2027-12-31');
  });

  it('falls back to the overlay when showPicker() is refused', async () => {
    const { native } = await field({
      showPicker: () => {
        throw new DOMException('not allowed', 'NotAllowedError');
      },
    });
    expect(native.className).not.toContain('overlay');

    fireEvent.click(screen.getByRole('button', { name: 'Choose a date' }));

    expect(native.className).toContain('overlay');
    expect(native.getAttribute('aria-label')).toBe('Choose a date');
  });

  it('ignores a picker that clears itself, keeping the day already written', async () => {
    const { native, text } = await field({ showPicker: vi.fn(), initial: '2027-03-07' });
    fireEvent.change(native, { target: { value: '' } });
    expect(text.value).toBe('2027-03-07');
  });
});
