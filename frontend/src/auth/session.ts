import type { LoginResponse } from '../api/types';

// sessionStorage is cleared when the tab closes. See the notes on token storage.
const KEY = 'varinsedu.session';

export function loadSession(): LoginResponse | null {
  try {
    const raw = sessionStorage.getItem(KEY);
    if (!raw) {
      return null;
    }

    const session = JSON.parse(raw) as LoginResponse;

    if (new Date(session.expiresAt).getTime() <= Date.now()) {
      sessionStorage.removeItem(KEY);
      return null;
    }

    return session;
  } catch {
    return null;
  }
}

export function saveSession(session: LoginResponse): void {
  sessionStorage.setItem(KEY, JSON.stringify(session));
}

export function clearSession(): void {
  sessionStorage.removeItem(KEY);
}