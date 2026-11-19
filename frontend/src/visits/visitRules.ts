import type { VisitStatus } from '../api/types';

// The same rules as the server (PublicVisitService): the server is who really enforces them.
// Letters and digits in upper case, so "a-123 45" and "A12345" are the same document.
export function normalizeIdentification(value: string): string {
  return value.replace(/[^a-zA-Z0-9]/g, '').toUpperCase();
}

export function isValidIdentification(normalized: string): boolean {
  return normalized.length >= 4 && normalized.length <= 20;
}

const STATUS_LABELS: Record<VisitStatus, string> = {
  pending: 'Pendiente',
  inside: 'Dentro',
  exited: 'Salió',
  rejected: 'Rechazada',
  expired: 'Vencida',
  no_exit: 'Sin salida registrada',
};

export function statusLabel(status: string): string {
  return STATUS_LABELS[status as VisitStatus] ?? status;
}