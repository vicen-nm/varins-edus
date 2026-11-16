import { describe, expect, it } from 'vitest';
import { isLive, isValidPlate, normalizePlate, statusLabel } from './vehicleRules';

describe('vehicle rules', () => {
  it('normalizes plates to upper case letters and digits', () => {
    expect(normalizePlate(' mot-123 456 ')).toBe('MOT123456');
    expect(normalizePlate('ñandú-12')).toBe('AND12');
    expect(normalizePlate('--')).toBe('');
  });

  it('accepts plates of 3 to 10 characters', () => {
    expect(isValidPlate('AB')).toBe(false);
    expect(isValidPlate('ABC')).toBe(true);
    expect(isValidPlate('ABCDEFGHIJ')).toBe(true);
    expect(isValidPlate('ABCDEFGHIJK')).toBe(false);
  });

  it('translates known statuses and keeps unknown ones as they are', () => {
    expect(statusLabel('pending')).toBe('Pendiente de revisión');
    expect(statusLabel('suspended')).toBe('Suspendido');
    expect(statusLabel('something-new')).toBe('something-new');
  });

  it('counts only pending, approved and suspended vehicles as live', () => {
    for (const status of ['pending', 'approved', 'suspended']) {
      expect(isLive(status)).toBe(true);
    }

    for (const status of ['rejected', 'withdrawn']) {
      expect(isLive(status)).toBe(false);
    }
  });
});