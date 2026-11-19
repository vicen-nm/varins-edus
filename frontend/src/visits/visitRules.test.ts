import { describe, expect, it } from 'vitest';
import { isValidIdentification, normalizeIdentification, statusLabel } from './visitRules';

describe('visit rules', () => {
  it('normalizes a document to upper case letters and digits', () => {
    expect(normalizeIdentification(' a-123 45 ')).toBe('A12345');
    expect(normalizeIdentification('--')).toBe('');
  });

  it('accepts documents of 4 to 20 characters', () => {
    expect(isValidIdentification('123')).toBe(false);
    expect(isValidIdentification('1234')).toBe(true);
    expect(isValidIdentification('1'.repeat(20))).toBe(true);
    expect(isValidIdentification('1'.repeat(21))).toBe(false);
  });

  it('translates the status of a visit and keeps unknown ones as they are', () => {
    expect(statusLabel('pending')).toBe('Pendiente');
    expect(statusLabel('inside')).toBe('Dentro');
    expect(statusLabel('no_exit')).toBe('Sin salida registrada');
    expect(statusLabel('something-new')).toBe('something-new');
  });
});