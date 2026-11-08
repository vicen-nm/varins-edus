// When to renew: at 80% of the token's life, but never later than its expiry. Already expired: now.
export function refreshDelayMs(expiresAt: string, nowMs: number): number {
  const remaining = new Date(expiresAt).getTime() - nowMs;

  if (remaining <= 0) {
    return 0;
  }

  return Math.min(remaining, Math.max(Math.floor(remaining * 0.8), 1000));
}