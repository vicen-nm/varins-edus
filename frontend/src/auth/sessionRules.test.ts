import { describe, expect, it } from 'vitest';
import { canAcceptRenewal } from './sessionRules';

describe('canAcceptRenewal', () => {
  it('accepts the renewal of the same user, or when this tab kept no session', () => {
    expect(canAcceptRenewal('user-a', 'user-a')).toBe(true);
    expect(canAcceptRenewal(null, 'user-a')).toBe(true);
  });

  it('refuses a renewal that belongs to somebody else', () => {
    expect(canAcceptRenewal('user-a', 'user-b')).toBe(false);
  });
});