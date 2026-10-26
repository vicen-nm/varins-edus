import type { IssuedCode } from '../api/types';

// Excel in Spanish locales expects ";" as separator.
const SEPARATOR = ';';

function cell(value: string | null): string {
  let text = value ?? '';

  // Stop Excel from running a name as a formula.
  if (/^[=+\-@\t\r]/.test(text)) {
    text = `'${text}`;
  }

  if (text.includes(SEPARATOR) || text.includes('"') || text.includes('\n')) {
    return `"${text.replace(/"/g, '""')}"`;
  }

  return text;
}

export function buildCodesCsv(codes: IssuedCode[]): string {
  const header = ['Cédula', 'Nombre', 'Apellidos', 'Grupo', 'Código'].join(SEPARATOR);
  const rows = codes.map((c) =>
    [c.identificationNumber, c.firstName, c.lastName, c.group, c.code].map(cell).join(SEPARATOR),
  );

  return [header, ...rows].join('\r\n');
}