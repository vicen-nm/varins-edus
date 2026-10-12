export type Rgb = { r: number; g: number; b: number };

const HEX_COLOR = /^#[0-9a-fA-F]{6}$/;

export function parseHex(hex: string): Rgb | null {
  if (!HEX_COLOR.test(hex)) {
    return null;
  }

  return {
    r: parseInt(hex.slice(1, 3), 16),
    g: parseInt(hex.slice(3, 5), 16),
    b: parseInt(hex.slice(5, 7), 16),
  };
}

// For constants written in the code: a typo should fail loudly.
export function mustParseHex(hex: string): Rgb {
  const color = parseHex(hex);

  if (!color) {
    throw new Error(`Invalid hex color: ${hex}`);
  }

  return color;
}

function clamp(value: number): number {
  return Math.min(255, Math.max(0, value));
}

export function roundRgb({ r, g, b }: Rgb): Rgb {
  return { r: Math.round(clamp(r)), g: Math.round(clamp(g)), b: Math.round(clamp(b)) };
}

export function toHex(color: Rgb): string {
  const { r, g, b } = roundRgb(color);
  const part = (value: number) => value.toString(16).padStart(2, '0');

  return `#${part(r)}${part(g)}${part(b)}`;
}

// WCAG: sRGB channels are converted to linear light before computing luminance.
function toLinear(channel: number): number {
  const value = channel / 255;
  return value <= 0.04045 ? value / 12.92 : Math.pow((value + 0.055) / 1.055, 2.4);
}

export function luminance({ r, g, b }: Rgb): number {
  return 0.2126 * toLinear(r) + 0.7152 * toLinear(g) + 0.0722 * toLinear(b);
}

// Between 1 (identical) and 21 (black on white). Text needs at least 4.5.
export function contrastRatio(a: Rgb, b: Rgb): number {
  const la = luminance(a);
  const lb = luminance(b);
  const lighter = Math.max(la, lb);
  const darker = Math.min(la, lb);

  return (lighter + 0.05) / (darker + 0.05);
}

// amount = 0 gives `from`; amount = 1 gives `to`.
export function mix(from: Rgb, to: Rgb, amount: number): Rgb {
  return {
    r: from.r + (to.r - from.r) * amount,
    g: from.g + (to.g - from.g) * amount,
    b: from.b + (to.b - from.b) * amount,
  };
}

// Moves `color` toward `target` in small steps until it reaches `minContrast` against `against`.
// A color that already complies is returned unchanged. Rounding is part of the check,
// so the final hex value is the one that was actually measured.
export function moveUntilContrast(color: Rgb, target: Rgb, against: Rgb, minContrast: number): Rgb {
  for (let step = 0; step <= 50; step++) {
    const candidate = roundRgb(mix(color, target, step / 50));

    if (contrastRatio(candidate, against) >= minContrast) {
      return candidate;
    }
  }

  return roundRgb(target);
}