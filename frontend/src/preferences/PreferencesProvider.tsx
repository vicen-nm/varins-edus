import { useCallback, useEffect, useMemo, useState } from 'react';
import type { ReactNode } from 'react';
import { apiFetch } from '../api/client';
import type { DisplayPreferences } from '../api/types';
import { useAuth } from '../auth/useAuth';
import { PreferencesContext } from './preferencesContext';
import type { PreferencesContextValue } from './preferencesContext';
import { DEFAULT_PREFERENCES, applyPreferences } from './preferencesRules';

// Each result remembers which user it belongs to, so the preferences of a previous user are never applied.
// They are not kept in the browser: on a shared computer, the next person starts with the normal look.
type Loaded = { userId: string; value: DisplayPreferences };

export function PreferencesProvider({ children }: { children: ReactNode }) {
  const { user } = useAuth();
  const userId = user?.userId ?? null;
  const [loaded, setLoaded] = useState<Loaded | null>(null);

  useEffect(() => {
    if (!userId) {
      return;
    }

    let cancelled = false;

    apiFetch<DisplayPreferences>('/api/me/preferences')
      .then((value) => {
        if (!cancelled) {
          setLoaded({ userId, value });
        }
      })
      .catch(() => {
        // The normal look stays: the preferences are a convenience, never a requirement.
      });

    return () => {
      cancelled = true;
    };
  }, [userId]);

  const preferences = userId && loaded?.userId === userId ? loaded.value : DEFAULT_PREFERENCES;

  useEffect(() => {
    applyPreferences(preferences);
  }, [preferences]);

  const save = useCallback(
    async (next: DisplayPreferences) => {
      const saved = await apiFetch<DisplayPreferences>('/api/me/preferences', {
        method: 'PUT',
        body: JSON.stringify(next),
      });

      if (userId) {
        setLoaded({ userId, value: saved });
      }

      return saved;
    },
    [userId],
  );

  const value = useMemo<PreferencesContextValue>(() => ({ preferences, save }), [preferences, save]);

  return <PreferencesContext.Provider value={value}>{children}</PreferencesContext.Provider>;
}