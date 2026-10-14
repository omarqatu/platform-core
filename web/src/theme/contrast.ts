/**
 * WCAG contrast arithmetic on hex colors (docs/ui/reference/src/App.tsx): the token checks, and later the
 * organization's brand color.
 */

/** "#fff" or "fff" → "#ffffff"; six digits are lower-cased. Anything else is an error. */
export function normalizeHex(hex: string): string {
  const value = hex.trim().replace(/^#/, '').toLowerCase();
  if (/^[0-9a-f]{3}$/.test(value)) return `#${[...value].map((c) => c + c).join('')}`;
  if (/^[0-9a-f]{6}$/.test(value)) return `#${value}`;
  throw new Error(`Not a hex color: ${hex}`);
}

function channels(hex: string): [number, number, number] {
  const value = normalizeHex(hex);
  return [1, 3, 5].map((i) => parseInt(value.slice(i, i + 2), 16)) as [number, number, number];
}

function luminance(hex: string): number {
  const [r, g, b] = channels(hex).map((c) => {
    const v = c / 255;
    return v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

/** The WCAG contrast ratio of two colors, 1 to 21. */
export function contrast(hex1: string, hex2: string): number {
  const l1 = luminance(hex1);
  const l2 = luminance(hex2);
  return (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05);
}

/** The color moved toward black or white by amount (0 to 1). */
export function mixHex(hex: string, target: '#000000' | '#ffffff', amount: number): string {
  const destination = target === '#ffffff' ? 255 : 0;
  return `#${channels(hex)
    .map((c) => Math.round(c + (destination - c) * amount).toString(16).padStart(2, '0'))
    .join('')}`;
}

/**
 * A brand color's variant for dark surfaces (dark mode and the dark sidebar): the color itself, or the first tint
 * toward white that reaches 3:1 on the dark surface and the light mode's sidebar, and 4.5:1 on the dark canvas.
 */
export function darkVariant(brand: string): string {
  for (let step = 0; step <= 100; step += 1) {
    const candidate = step === 0 ? normalizeHex(brand) : mixHex(brand, '#ffffff', step / 100);
    if (contrast(candidate, '#18191d') >= 3 && contrast(candidate, '#17181c') >= 3 && contrast(candidate, '#101114') >= 4.5) {
      return candidate;
    }
  }
  return '#8b84ff';
}
