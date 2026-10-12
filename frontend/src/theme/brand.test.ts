import { describe, expect, it } from 'vitest';
import { buildBrandVariables } from './brand';
import { contrastRatio, mustParseHex, parseHex } from './color';

const WHITE = mustParseHex('#ffffff');
const LIGHT_BACKGROUND = mustParseHex('#f4f7fa');
const DARK_SURFACE = mustParseHex('#101c2b');
const DARK_TEXT = mustParseHex('#04202a');

describe('contrastRatio', () => {
  it('is 21 for black on white', () => {
    expect(contrastRatio(mustParseHex('#000000'), WHITE)).toBeCloseTo(21, 1);
  });

  it('is 1 for the same color', () => {
    expect(contrastRatio(mustParseHex('#007690'), mustParseHex('#007690'))).toBeCloseTo(1, 5);
  });
});

describe('parseHex', () => {
  it.each(['red', '#fff', '007690', '#12345678', '#gggggg'])('rejects %s', (value) => {
    expect(parseHex(value)).toBeNull();
  });
});

describe('buildBrandVariables', () => {
  const colors = ['#ffe066', '#007690', '#0b1b2b', '#ff0000', '#7a1fa2', '#ffffff', '#000000'];

  it.each(colors)('keeps %s readable in light mode', (color) => {
    const primary = mustParseHex(buildBrandVariables(color)!['--brand-primary-light']);

    expect(contrastRatio(primary, LIGHT_BACKGROUND)).toBeGreaterThanOrEqual(4.5);
    expect(contrastRatio(primary, WHITE)).toBeGreaterThanOrEqual(4.5);
  });

  it.each(colors)('keeps %s readable in dark mode', (color) => {
    const primary = mustParseHex(buildBrandVariables(color)!['--brand-primary-dark']);

    expect(contrastRatio(primary, DARK_TEXT)).toBeGreaterThanOrEqual(4.5);
    expect(contrastRatio(primary, DARK_SURFACE)).toBeGreaterThanOrEqual(3);
  });

  it('leaves a color that already complies unchanged in light mode', () => {
    expect(buildBrandVariables('#007690')!['--brand-primary-light']).toBe('#007690');
  });

  it('returns null for an invalid color', () => {
    expect(buildBrandVariables('red')).toBeNull();
  });
});