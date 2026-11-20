import type { DisplayPreferences } from '../api/types';

export const DEFAULT_PREFERENCES: DisplayPreferences = {
  uiMode: 'standard',
  fontScale: 1,
  readAloud: false,
  highContrast: false,
  reducedMotion: false,
};

// The same limits as the server (PreferenceService): from 100 % to 200 %.
export function clampFontScale(value: number): number {
  if (!Number.isFinite(value)) {
    return 1;
  }

  return Math.min(2, Math.max(1, Math.round(value * 100) / 100));
}

export function fontScaleLabel(value: number): string {
  return `${Math.round(clampFontScale(value) * 100)} %`;
}

// What the page is told: the CSS reacts to these values.
export function describePreferences(preferences: DisplayPreferences) {
  return {
    mode: preferences.uiMode === 'simple' ? 'simple' : 'standard',
    contrast: preferences.highContrast ? 'high' : 'normal',
    motion: preferences.reducedMotion ? 'reduced' : 'full',
    fontScale: clampFontScale(preferences.fontScale),
  };
}

// Writes the preferences into the page. Passing the defaults restores the normal look.
export function applyPreferences(preferences: DisplayPreferences): void {
  const root = document.documentElement;
  const described = describePreferences(preferences);

  root.dataset.uiMode = described.mode;
  root.dataset.contrast = described.contrast;
  root.dataset.motion = described.motion;
  root.style.setProperty('--font-scale', String(described.fontScale));
}