import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { accentIds, type AccentId } from './accentPreference';
import { contrast } from './contrast';
import { themeModes, type ThemeMode } from './theme';

// Read from disk: Vitest does not hand style sheets to tests (css: false), ?raw included.
const css = readFileSync(join(import.meta.dirname, 'tokens.css'), 'utf8');

// The rule blocks of tokens.css, in order: every one is on the root element (:root, :root[data-…='…']).
const blocks = [...css.replace(/\/\*[\s\S]*?\*\//g, '').matchAll(/([^{}]+)\{([^{}]*)\}/g)].map(([, selector, body]) => ({
  selector: selector!.trim(),
  declarations: [...body!.matchAll(/(--[\w-]+)\s*:\s*([^;]+);/g)].map(([, name, value]) => [name!, value!.trim()] as const),
}));

function matches(selector: string, theme: ThemeMode, accent: AccentId | null): boolean {
  if (selector === ':root') return true;
  const attribute = /^:root\[data-(theme|accent)='([a-z]+)'\]$/.exec(selector);
  if (!attribute) throw new Error(`tokens.css: unexpected selector ${selector}`);
  return attribute[1] === 'theme' ? attribute[2] === theme : attribute[2] === accent;
}

/** The tokens as the root element would compute them: the matching blocks in order, var() resolved. */
function resolve(theme: ThemeMode, accent: AccentId | null): (name: string) => string {
  const values = new Map<string, string>();
  for (const block of blocks) {
    if (matches(block.selector, theme, accent)) for (const [name, value] of block.declarations) values.set(name, value);
  }
  const get = (name: string, depth = 0): string => {
    const value = values.get(name);
    if (value === undefined) throw new Error(`tokens.css: ${name} is not defined`);
    if (depth > 10) throw new Error(`tokens.css: ${name} does not resolve`);
    const ref = /^var\((--[\w-]+)\)$/.exec(value);
    return ref ? get(ref[1]!, depth + 1) : value;
  };
  return (name) => get(name);
}

// The approved presets, exactly as in docs/ui/reference/src/index.css.
const REFERENCE: Record<AccentId, { light: string; dark: string }> = {
  indigo: { light: '#5146e5', dark: '#8b84ff' },
  teal: { light: '#006b5f', dark: '#4fd1b5' },
  rust: { light: '#9a3412', dark: '#fb923c' },
  blue: { light: '#075985', dark: '#38bdf8' },
  violet: { light: '#7e22ce', dark: '#c084fc' },
};

const combinations = themeModes.flatMap((theme) => [null, ...accentIds].map((accent) => ({ theme, accent })));

describe('tokens.css', () => {
  it('has the five accent presets of the reference, light and dark', () => {
    const token = resolve('light', null);
    for (const id of accentIds) {
      expect(token(`--accent-${id}-light`)).toBe(REFERENCE[id].light);
      expect(token(`--accent-${id}-dark`)).toBe(REFERENCE[id].dark);
    }
  });

  it('defaults to indigo, the organization default, with no accent chosen', () => {
    expect(resolve('light', null)('--brand')).toBe(REFERENCE.indigo.light);
    expect(resolve('dark', null)('--brand')).toBe(REFERENCE.indigo.dark);
  });

  it.each(accentIds)('%s: the light variant on light surfaces, the dark variant in dark mode', (accent) => {
    expect(resolve('light', accent)('--brand')).toBe(REFERENCE[accent].light);
    expect(resolve('dark', accent)('--brand')).toBe(REFERENCE[accent].dark);
  });

  it.each(accentIds)('%s: the dark sidebar always takes the dark variant, with dark text', (accent) => {
    for (const theme of ['light', 'dark'] as const) {
      const token = resolve(theme, accent);
      expect(token('--nav-active-bg')).toBe(REFERENCE[accent].dark);
      expect(token('--nav-active-text')).toBe('#101114');
    }
  });

  it.each(accentIds)('%s: high contrast sets the accent aside for black and white', (accent) => {
    const token = resolve('contrast', accent);
    expect(token('--brand')).toBe('#000000');
    expect(token('--on-brand')).toBe('#ffffff');
    expect(token('--nav-active-bg')).toBe('#ffffff');
    expect(token('--nav-active-text')).toBe('#000000');
  });

  it('defines spacing, radius and the type scale', () => {
    const token = resolve('light', null);
    expect([1, 2, 3, 4, 6, 8, 12, 16].map((n) => token(`--space-${n}`))).toEqual(
      ['4px', '8px', '12px', '16px', '24px', '32px', '48px', '64px'],
    );
    expect(['sm', 'md', 'lg'].map((r) => token(`--radius-${r}`))).toEqual(['6px', '9px', '12px']);
    for (const step of ['display', 'h1', 'h2', 'body', 'label']) {
      for (const part of ['size', 'line-height', 'weight']) expect(token(`--type-${step}-${part}`)).toBeTruthy();
    }
  });
});

// WCAG 2.x: text 4.5:1; UI boundaries and focus indicators 3:1 — in every mode, with every accent.
const PAIRS: [string, string, number][] = [
  ['--text', '--canvas', 4.5],
  ['--text-secondary', '--surface', 4.5],
  ['--on-brand', '--brand', 4.5],
  ['--brand', '--canvas', 3],
  ['--brand', '--surface', 3],
  ['--focus', '--surface', 3],
  ['--border-strong', '--surface', 3],
  ['--nav-text', '--nav-surface', 4.5],
  ['--nav-active-text', '--nav-active-bg', 4.5],
  ['--nav-active-bg', '--nav-surface', 3],
];

describe.each(combinations)('contrast — $theme, accent $accent', ({ theme, accent }) => {
  const token = resolve(theme, accent);
  it.each(PAIRS)('%s on %s ≥ %s', (fg, bg, minimum) => {
    expect(contrast(token(fg), token(bg))).toBeGreaterThanOrEqual(minimum);
  });
});
