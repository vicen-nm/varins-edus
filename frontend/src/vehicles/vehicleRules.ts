import type { VehicleStatus } from '../api/types';

// The same rules as the server (VehicleService): the server is who really enforces them.
export const MAX_LIVE_VEHICLES = 2;
export const MAX_NOTE_LENGTH = 200;

const PLATE_MIN = 3;
const PLATE_MAX = 10;

// Letters and digits in upper case, so "mot-123 456" and "MOT123456" are the same plate.
export function normalizePlate(value: string): string {
  return value.replace(/[^a-zA-Z0-9]/g, '').toUpperCase();
}

export function isValidPlate(normalized: string): boolean {
  return normalized.length >= PLATE_MIN && normalized.length <= PLATE_MAX;
}

const STATUS_LABELS: Record<VehicleStatus, string> = {
  pending: 'Pendiente de revisión',
  approved: 'Aprobado',
  rejected: 'Rechazado',
  suspended: 'Suspendido',
  withdrawn: 'Retirado',
};

export function statusLabel(status: string): string {
  return STATUS_LABELS[status as VehicleStatus] ?? status;
}

// Pending, approved and suspended vehicles keep their plate reserved and count toward the limit.
export function isLive(status: string): boolean {
  return status === 'pending' || status === 'approved' || status === 'suspended';
}