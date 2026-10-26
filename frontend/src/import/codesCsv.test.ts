import { describe, expect, it } from 'vitest';
import type { IssuedCode } from '../api/types';
import { buildCodesCsv } from './codesCsv';

const ana: IssuedCode = {
  identificationNumber: '111111111',
  firstName: 'Ana',
  lastName: 'Prueba Uno',
  group: '10-1',
  code: 'ABCD-2345',
};

describe('buildCodesCsv', () => {
  it('writes a header and one line per student', () => {
    const lines = buildCodesCsv([ana]).split('\r\n');

    expect(lines[0]).toBe('Cédula;Nombre;Apellidos;Grupo;Código');
    expect(lines[1]).toBe('111111111;Ana;Prueba Uno;10-1;ABCD-2345');
  });

  it('quotes values with separators or quotes and leaves a missing group empty', () => {
    const csv = buildCodesCsv([{ ...ana, lastName: 'Uno; "Dos"', group: null }]);

    expect(csv.split('\r\n')[1]).toBe('111111111;Ana;"Uno; ""Dos""";;ABCD-2345');
  });

  it('neutralizes values that Excel would run as formulas', () => {
    const csv = buildCodesCsv([{ ...ana, firstName: '=1+1' }]);

    expect(csv.split('\r\n')[1]).toContain(";'=1+1;");
  });
});