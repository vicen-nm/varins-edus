// There is one renewal cookie per browser. If a renewal belongs to somebody else (another person signed in from
// another tab), this tab must NOT become that person: it drops its own session and asks to sign in again.
export function canAcceptRenewal(currentUserId: string | null, renewedUserId: string): boolean {
  return currentUserId === null || currentUserId === renewedUserId;
}