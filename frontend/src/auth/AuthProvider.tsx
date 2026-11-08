import { useCallback, useEffect, useMemo, useState } from 'react';
import type { ReactNode } from 'react';
import {
  SESSION_RENEWED_EVENT,
  UNAUTHORIZED_EVENT,
  apiFetch,
  renewSession,
  serverLogout,
} from '../api/client';
import type { LoginResponse } from '../api/types';
import { AuthContext } from './authContext';
import type { ActivationInput, AuthContextValue } from './authContext';
import { refreshDelayMs } from './refreshSchedule';
import { clearSession, loadSession, saveSession } from './session';

const RETRY_AFTER_NETWORK_ERROR_MS = 30_000;
const WAKE_UP_MARGIN_MS = 60_000;

export function AuthProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<LoginResponse | null>(() => loadSession());

  // With no session in this tab, the browser may still hold a renewal cookie: ask before showing the login.
  const [ready, setReady] = useState(() => session !== null);

  const logout = useCallback(() => {
    void serverLogout();
    clearSession();
    setSession(null);
  }, []);

  const startSession = useCallback((result: LoginResponse) => {
    saveSession(result);
    setSession(result);
  }, []);

  const login = useCallback(
    async (username: string, password: string, remember = false) => {
      const result = await apiFetch<LoginResponse>('/api/auth/login', {
        method: 'POST',
        body: JSON.stringify({ username, password, remember }),
      });

      startSession(result);
    },
    [startSession],
  );

  // Creating the account also starts the session.
  const activate = useCallback(
    async (input: ActivationInput) => {
      const result = await apiFetch<LoginResponse>('/api/auth/activate', {
        method: 'POST',
        body: JSON.stringify(input),
      });

      startSession(result);
    },
    [startSession],
  );

  // The session was renewed (here or by a request), or it cannot be renewed any more.
  useEffect(() => {
    const handleRenewed = (event: Event) => setSession((event as CustomEvent<LoginResponse>).detail);

    window.addEventListener(SESSION_RENEWED_EVENT, handleRenewed);
    window.addEventListener(UNAUTHORIZED_EVENT, logout);

    return () => {
      window.removeEventListener(SESSION_RENEWED_EVENT, handleRenewed);
      window.removeEventListener(UNAUTHORIZED_EVENT, logout);
    };
  }, [logout]);

  // First load without a session in this tab: restore it from the cookie, if there is one.
  useEffect(() => {
    if (ready) {
      return;
    }

    let cancelled = false;

    void renewSession().then((result) => {
      if (cancelled) {
        return;
      }

      if (result.ok) {
        setSession(result.session);
      }

      setReady(true);
    });

    return () => {
      cancelled = true;
    };
  }, [ready]);

  // Renew before the token expires. A renewed session restarts this effect with the new expiry.
  useEffect(() => {
    if (!session) {
      return;
    }

    const expiresAt = new Date(session.expiresAt).getTime();
    let cancelled = false;
    let timer: number | undefined;

    async function renew() {
      const result = await renewSession();

      if (cancelled || result.ok) {
        return;
      }

      if (result.reason === 'ended') {
        logout();
      } else {
        timer = window.setTimeout(renew, RETRY_AFTER_NETWORK_ERROR_MS);
      }
    }

    timer = window.setTimeout(renew, refreshDelayMs(session.expiresAt, Date.now()));

    // A sleeping computer freezes the timers: renew right away when the page wakes up with an old token.
    function handleVisibility() {
      if (!document.hidden && expiresAt - Date.now() < WAKE_UP_MARGIN_MS) {
        window.clearTimeout(timer);
        void renew();
      }
    }

    document.addEventListener('visibilitychange', handleVisibility);

    return () => {
      cancelled = true;
      window.clearTimeout(timer);
      document.removeEventListener('visibilitychange', handleVisibility);
    };
  }, [session, logout]);

  const value = useMemo<AuthContextValue>(
    () => ({ user: session?.user ?? null, ready, login, activate, logout }),
    [session, ready, login, activate, logout],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}