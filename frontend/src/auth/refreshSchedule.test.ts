import { describe, expect, it } from 'vitest';
import { refreshDelayMs } from './refreshSchedule';

const now = new Date('2026-11-06T12:00:00Z').getTime();

describe('refreshDelayMs', () => {
  it('renews at 80% of the remaining life', () => {
    expect(refreshDelayMs('2026-11-06T12:10:00Z', now)).toBe(480_000);
  });

  it('never waits longer than what remains', () => {
    expect(refreshDelayMs('2026-11-06T12:00:00.500Z', now)).toBe(500);
  });

  it('renews immediately when the token already expired', () => {
    expect(refreshDelayMs('2026-11-06T11:59:00Z', now)).toBe(0);
  });
});