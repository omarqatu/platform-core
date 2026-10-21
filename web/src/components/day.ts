/**
 * A calendar day as text (the project owner's items 5 and 6): yyyy-MM-dd, Western digits, whatever keyboard typed it.
 * DateField's rules, apart from React.
 */

const DAY = /^\d{4}-\d{2}-\d{2}$/;

/**
 * Western digits, whatever keyboard typed them: Arabic-Indic (٠–٩) and Eastern Arabic-Indic (۰–۹) digits become
 * 0–9, and the Arabic decimal separator (٫) a point. The interface shows Western digits in Arabic too (web/README),
 * and the fields the server parses — the date and the cost — accept nothing else.
 */
export function westernDigits(text: string): string {
  return text.replace(/[٠-٩۰-۹٫]/g, (c) => {
    if (c === '٫') return '.';
    const code = c.charCodeAt(0);
    return String(code - (code >= 0x06f0 ? 0x06f0 : 0x0660));
  });
}

/** A real calendar day written yyyy-MM-dd — 2027-02-30 is not one. */
export function isDay(text: string): boolean {
  if (!DAY.test(text)) return false;
  const date = new Date(`${text}T00:00:00Z`);
  return !Number.isNaN(date.getTime()) && date.toISOString().startsWith(text);
}

/**
 * The field's text after an edit, from the text before it: the digits only (Western, at most eight), with the
 * hyphens written for the user — a phone's numeric keypad has none. Typing: 2027 → "2027-", then "2027-03-", then
 * "2027-03-07". Deleting never writes a hyphen back, and a backspace that took a hyphen takes the digit before it
 * too ("2027-" → "202"), so one key always removes something. A paste of "2027-03-07", or of Arabic-Indic digits,
 * lands as the same day.
 */
export function typeDay(before: string, after: string): string {
  let digits = westernDigits(after).replace(/\D/g, '').slice(0, 8);
  const deleting = after.length < before.length;
  if (deleting && before.endsWith('-') && after === before.slice(0, -1)) digits = digits.slice(0, -1);
  const [year, month, day] = [digits.slice(0, 4), digits.slice(4, 6), digits.slice(6, 8)];
  let text = year;
  if (digits.length > 4 || (digits.length === 4 && !deleting)) text += `-${month}`;
  if (digits.length > 6 || (digits.length === 6 && !deleting)) text += `-${day}`;
  return text;
}
