import { describe, expect, it } from 'vitest';
import { contrast, darkVariant, mixHex, normalizeHex } from './contrast';

describe('normalizeHex', () => {
  it('expands three digits (minified CSS writes #fff)', () => {
    expect(normalizeHex('#fff')).toBe('#ffffff');
    expect(normalizeHex('#0aF')).toBe('#00aaff');
    expect(normalizeHex('abc')).toBe('#aabbcc');
  });
  it('lower-cases six digits and adds the #', () => {
    expect(normalizeHex('#5146E5')).toBe('#5146e5');
    expect(normalizeHex(' 101114 ')).toBe('#101114');
  });
  it('refuses anything else', () => {
    expect(() => normalizeHex('#ffff')).toThrow();
    expect(() => normalizeHex('red')).toThrow();
    expect(() => normalizeHex('')).toThrow();
  });
});

describe('contrast', () => {
  it('is 21 for black on white, 1 for a color on itself, in either order', () => {
    expect(contrast('#000000', '#ffffff')).toBeCloseTo(21, 5);
    expect(contrast('#ffffff', '#000')).toBeCloseTo(21, 5);
    expect(contrast('#5146e5', '#5146e5')).toBe(1);
  });
  it('reads three digits as their six', () => {
    expect(contrast('#fff', '#767982')).toBe(contrast('#ffffff', '#767982'));
  });
  it('matches a known ratio', () => {
    expect(contrast('#767676', '#ffffff')).toBeCloseTo(4.54, 2);
  });
});

describe('mixHex', () => {
  it('moves toward white or black', () => {
    expect(mixHex('#000000', '#ffffff', 0.5)).toBe('#808080');
    expect(mixHex('#ffffff', '#000000', 1)).toBe('#000000');
    expect(mixHex('#fff', '#000000', 0)).toBe('#ffffff');
  });
});

describe('darkVariant', () => {
  it('keeps a color that already reads on the dark surfaces', () => {
    expect(darkVariant('#8B84FF')).toBe('#8b84ff');
  });
  it('lightens a color that does not, until it does', () => {
    const variant = darkVariant('#5146e5');
    expect(variant).not.toBe('#5146e5');
    expect(contrast(variant, '#101114')).toBeGreaterThanOrEqual(4.5);
    expect(contrast(variant, '#18191d')).toBeGreaterThanOrEqual(3);
    expect(contrast(variant, '#17181c')).toBeGreaterThanOrEqual(3);
  });
});
