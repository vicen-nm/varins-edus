import type { ExitPermitStatus } from '../api/types';

const STATUS_LABELS: Record<ExitPermitStatus, string> = {
  active: 'Vigente',
  used: 'Usado',
  cancelled: 'Cancelado',
  expired: 'Vencido',
};

export function statusLabel(status: string): string {
  return STATUS_LABELS[status as ExitPermitStatus] ?? status;
}

// "09:00:00" becomes "09:00".
export function shortTime(value: string): string {
  return value.slice(0, 5);
}

export function formatWindow(from: string, to: string): string {
  return `${shortTime(from)} a ${shortTime(to)}`;
}

// <input type="time"> gives "HH:mm" and the API expects "HH:mm:ss".
export function toApiTime(value: string): string {
  return value.length === 5 ? `${value}:00` : value;
}

// Today in the browser's time zone as yyyy-mm-dd, for the date input.
export function todayIso(now: Date = new Date()): string {
  const month = String(now.getMonth() + 1).padStart(2, '0');
  const day = String(now.getDate()).padStart(2, '0');

  return `${now.getFullYear()}-${month}-${day}`;
}