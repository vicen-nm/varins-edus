import { describe, expect, it } from 'vitest';
import { DEFAULT_PREFERENCES, clampFontScale, describePreferences, fontScaleLabel } from './preferencesRules';

describe('preferences rules', () => {
  it('keeps the font scale between 100 % and 200 % and rounds it', () => {
    expect(clampFontScale(0.5)).toBe(1);
    expect(clampFontScale(3)).toBe(2);
    expect(clampFontScale(Number.NaN)).toBe(1);
    expect(clampFontScale(1.2549)).toBe(1.25);
  });

  it('shows the font scale as a percentage', () => {
    expect(fontScaleLabel(1)).toBe('100 %');
    expect(fontScaleLabel(1.25)).toBe('125 %');
    expect(fontScaleLabel(3)).toBe('200 %');
  });

  it('describes the normal look by default and every option when they are on', () => {
    expect(describePreferences(DEFAULT_PREFERENCES)).toEqual({
      mode: 'standard',
      contrast: 'normal',
      motion: 'full',
      fontScale: 1,
    });

    expect(
      describePreferences({ uiMode: 'simple', fontScale: 1.5, readAloud: true, highContrast: true, reducedMotion: true }),
    ).toEqual({ mode: 'simple', contrast: 'high', motion: 'reduced', fontScale: 1.5 });
  });
});