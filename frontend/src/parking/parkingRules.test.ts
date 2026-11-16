import { describe, expect, it } from 'vitest';
import { actionLabel, resultLabel, statusLabel } from './parkingRules';

describe('parking rules', () => {
  it('translates the status of a visit and keeps unknown ones as they are', () => {
    expect(statusLabel('inside')).toBe('Dentro');
    expect(statusLabel('exited')).toBe('Salió');
    expect(statusLabel('no_exit')).toBe('Sin salida registrada');
    expect(statusLabel('something-new')).toBe('something-new');
  });

  it('names the action the guard is about to register and the one that was registered', () => {
    expect(actionLabel(false)).toBe('Registrar ingreso');
    expect(actionLabel(true)).toBe('Registrar salida');
    expect(resultLabel('entry')).toBe('Ingreso registrado');
    expect(resultLabel('exit')).toBe('Salida registrada');
  });
});