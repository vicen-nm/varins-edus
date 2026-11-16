import { describe, expect, it } from 'vitest';
import { buildStaffUsername, isValidStaffName, normalizeStaffName } from './staffUsername';

describe('staff usernames', () => {
  it('normalizes to lower case without spaces around', () => {
    expect(normalizeStaffName('  Maria.Perez ')).toBe('maria.perez');
  });

  it('accepts letters, digits and single separators, and rejects the rest', () => {
    for (const valid of ['maria.perez', 'ana-lopez', 'pedro_2', 'abc']) {
      expect(isValidStaffName(valid)).toBe(true);
    }

    for (const invalid of ['', 'ab', 'a b', 'bad@school', '.maria', 'maria.', 'ma..ria', 'x'.repeat(41)]) {
      expect(isValidStaffName(invalid)).toBe(false);
    }
  });

  it('adds the school to the username', () => {
    expect(buildStaffUsername(' Demo-A ', 'maria.perez')).toBe('maria.perez@demo-a');
  });
});