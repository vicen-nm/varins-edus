import { useCallback, useEffect, useMemo, useState } from 'react';
import type { ReactNode } from 'react';
import { apiFetch } from '../api/client';
import { errorMessage } from '../api/errors';
import type { InstitutionDetails } from '../api/types';
import { useAuth } from '../auth/useAuth';
import { applyBranding, brandingOf } from '../theme/applyBranding';
import type { BrandingValues } from '../theme/applyBranding';
import { InstitutionContext } from './institutionContext';
import type { InstitutionContextValue } from './institutionContext';

// Each result remembers which user it belongs to, so data from a previous user is never shown.
type Loaded = { userId: string; data: InstitutionDetails };
type Failed = { userId: string; message: string };

export function InstitutionProvider({ children }: { children: ReactNode }) {
  const { user } = useAuth();
  const [loaded, setLoaded] = useState<Loaded | null>(null);
  const [failed, setFailed] = useState<Failed | null>(null);
  const [publicBranding, setPublicBranding] = useState<BrandingValues | null>(null);

  const reload = useCallback(async () => {
    if (!user || user.isPlatform) {
      return;
    }

    const data = await apiFetch<InstitutionDetails>('/api/institution');
    setLoaded({ userId: user.userId, data });
    setFailed(null);
  }, [user]);

  // Platform users belong to no institution, so there is nothing to load for them.
  useEffect(() => {
    if (!user || user.isPlatform) {
      return;
    }

    let cancelled = false;

    apiFetch<InstitutionDetails>('/api/institution')
      .then((data) => {
        if (!cancelled) {
          setLoaded({ userId: user.userId, data });
          setFailed(null);
        }
      })
      .catch((e) => {
        if (!cancelled) {
          setFailed({ userId: user.userId, message: errorMessage(e) });
        }
      });

    return () => {
      cancelled = true;
    };
  }, [user]);

  const institution = user && loaded?.userId === user.userId ? loaded.data : null;
  const error = user && failed?.userId === user.userId ? failed.message : null;

  // The user's institution wins; before login, the public identity of the school (if any) is used.
  useEffect(() => {
    applyBranding(brandingOf(institution) ?? publicBranding);
  }, [institution, publicBranding]);

  const value = useMemo<InstitutionContextValue>(
    () => ({ institution, error, reload, setPublicBranding }),
    [institution, error, reload],
  );

  return <InstitutionContext.Provider value={value}>{children}</InstitutionContext.Provider>;
}