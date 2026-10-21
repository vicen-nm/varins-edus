import { useCallback, useEffect, useMemo, useState } from 'react';
import type { ReactNode } from 'react';
import { UNAUTHORIZED_EVENT, apiFetch } from '../api/client';
import type { LoginResponse } from '../api/types';
import { AuthContext } from './authContext';
import type { ActivationInput, AuthContextValue } from './authContext';
import { clearSession, loadSession, saveSession } from './session';

export function AuthProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<LoginResponse | null>(() => loadSession());

  const logout = useCallback(() => {
    clearSession();
    setSession(null);
  }, []);

  const startSession = useCallback((result: LoginResponse) => {
    saveSession(result);
    setSession(result);
  }, []);

  const login = useCallback(
    async (username: string, password: string) => {
      const result = await apiFetch<LoginResponse>('/api/auth/login', {
        method: 'POST',
        body: JSON.stringify({ username, password }),
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

  // The API rejected our token: close the session.
  useEffect(() => {
    window.addEventListener(UNAUTHORIZED_EVENT, logout);
    return () => window.removeEventListener(UNAUTHORIZED_EVENT, logout);
  }, [logout]);

  // Close the session when the token expires.
  useEffect(() => {
    if (!session) {
      return;
    }

    const remaining = new Date(session.expiresAt).getTime() - Date.now();
    const timer = window.setTimeout(logout, Math.max(remaining, 0));

    return () => window.clearTimeout(timer);
  }, [session, logout]);

  const value = useMemo<AuthContextValue>(
    () => ({ user: session?.user ?? null, login, activate, logout }),
    [session, login, activate, logout],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}