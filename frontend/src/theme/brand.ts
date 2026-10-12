import { mix, moveUntilContrast, mustParseHex, parseHex, toHex } from './color';

export const BRAND_VARIABLE_NAMES = [
  '--brand-primary-light',
  '--brand-primary-hover-light',
  '--brand-accent-soft-light',
  '--brand-primary-dark',
  '--brand-primary-hover-dark',
  '--brand-accent-soft-dark',
] as const;

export type BrandVariableName = (typeof BRAND_VARIABLE_NAMES)[number];
export type BrandVariables = Record<BrandVariableName, string>;

const WHITE = mustParseHex('#ffffff');
const BLACK = mustParseHex('#000000');
const LIGHT_BACKGROUND = mustParseHex('#f4f7fa');
const DARK_SURFACE = mustParseHex('#0a1420');
const DARK_TEXT = mustParseHex('#04202a');

const MIN_TEXT_CONTRAST = 4.5;

// Turns the color an institution picked into colors that are always readable.
// It never rejects a color: it adjusts it, one version for each theme.
export function buildBrandVariables(primaryHex: string): BrandVariables | null {
  const base = parseHex(primaryHex);

  if (!base) {
    return null;
  }

  // Light theme: buttons carry white text and links sit on the page background.
  // Darkening until the color reads well on that background also covers the white text.
  const light = moveUntilContrast(base, BLACK, LIGHT_BACKGROUND, MIN_TEXT_CONTRAST);

  // Dark theme: buttons carry dark text, so the color is lightened until that text reads well.
  const dark = moveUntilContrast(base, WHITE, DARK_TEXT, MIN_TEXT_CONTRAST);

  return {
    '--brand-primary-light': toHex(light),
    '--brand-primary-hover-light': toHex(mix(light, BLACK, 0.14)),
    '--brand-accent-soft-light': toHex(mix(light, WHITE, 0.88)),
    '--brand-primary-dark': toHex(dark),
    '--brand-primary-hover-dark': toHex(mix(dark, WHITE, 0.16)),
    '--brand-accent-soft-dark': toHex(mix(dark, DARK_SURFACE, 0.8)),
  };
}