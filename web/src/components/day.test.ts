import { describe, expect, it } from 'vitest';
import { isDay, typeDay } from './day';

/** Each key as the browser reports it: the text after the key, given the text before it. */
function type(keys: string, from = ''): string {
  let text = from;
  for (const key of keys) text = typeDay(text, text + key);
  return text;
}
const backspace = (text: string) => typeDay(text, text.slice(0, -1));

// The project owner's item 6: a phone's numeric keypad has no hyphen, so the field writes them.
describe('typing a day', () => {
  it('turns eight digits into yyyy-MM-dd, writing each hyphen as soon as its part is complete', () => {
    expect(type('2027')).toBe('2027-');
    expect(type('202703')).toBe('2027-03-');
    expect(type('20270307')).toBe('2027-03-07');
    expect(isDay(type('20270307'))).toBe(true);
  });

  it('takes no ninth digit, and drops what is not a digit', () => {
    expect(type('202703079')).toBe('2027-03-07');
    expect(type('2027a03.07')).toBe('2027-03-07');
  });

  it('accepts the hyphens typed by hand too, on a keyboard that has them', () => {
    expect(type('2027-03-07')).toBe('2027-03-07');
  });

  it('takes Arabic-Indic and Eastern Arabic-Indic digits as Western ones', () => {
    expect(type('٢٠٢٧٠٣٠٧')).toBe('2027-03-07');
    expect(type('۲۰۲۷۰۳۰۷')).toBe('2027-03-07');
  });

  it('lands a paste of a whole day as that day, in any digits', () => {
    expect(typeDay('', '2027-03-07')).toBe('2027-03-07');
    expect(typeDay('', '٢٠٢٧-٠٣-٠٧')).toBe('2027-03-07');
    expect(typeDay('2026-01-01', '2027-03-07')).toBe('2027-03-07');
  });

  it('backspaces sanely: a digit at a time, never writing a hyphen back, and a hyphen goes with the digit before it', () => {
    expect(backspace('2027-03-07')).toBe('2027-03-0');
    expect(backspace('2027-03-0')).toBe('2027-03');
    expect(backspace('2027-03')).toBe('2027-0');
    // Straight after an automatic hyphen, one key removes the hyphen and the digit before it: never a key that
    // does nothing, never the hyphen written again.
    expect(backspace('2027-03-')).toBe('2027-0');
    expect(backspace('2027-')).toBe('202');
    let text = '2027-03-07';
    for (let i = 0; i < 12; i++) text = backspace(text);
    expect(text).toBe('');
  });
});
