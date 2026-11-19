import { describe, expect, it } from 'vitest';
import { formatWindow, shortTime, statusLabel, toApiTime, todayIso } from './exitRules';

describe('exit rules', () => {
  it('translates the status of a permit and keeps unknown ones as they are', () => {
    expect(statusLabel('active')).toBe('Vigente');
    expect(statusLabel('used')).toBe('Usado');
    expect(statusLabel('cancelled')).toBe('Cancelado');
    expect(statusLabel('expired')).toBe('Vencido');
    expect(statusLabel('something-new')).toBe('something-new');
  });

  it('shows a window without seconds', () => {
    expect(shortTime('09:30:00')).toBe('09:30');
    expect(formatWindow('09:00:00', '11:15:00')).toBe('09:00 a 11:15');
  });

  it('converts the time input to the API format and builds the date of today', () => {
    expect(toApiTime('09:30')).toBe('09:30:00');
    expect(toApiTime('09:30:15')).toBe('09:30:15');
    expect(todayIso(new Date(2026, 10, 5))).toBe('2026-11-05');
  });
});