// The same rules as the server (StaffUsernames): the server is who really enforces them.
const PATTERN = /^[a-z0-9]+([._-][a-z0-9]+)*$/;

export function normalizeStaffName(value: string): string {
  return value.trim().toLowerCase();
}

export function isValidStaffName(normalized: string): boolean {
  return normalized.length >= 3 && normalized.length <= 40 && PATTERN.test(normalized);
}

// "maria.perez@demo-a": the school is added by the system, nobody types it.
export function buildStaffUsername(slug: string, normalized: string): string {
  return `${normalized}@${slug.trim().toLowerCase()}`;
}