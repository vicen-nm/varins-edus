import { createContext } from 'react';
import type { InstitutionDetails } from '../api/types';
import type { BrandingValues } from '../theme/applyBranding';

export type InstitutionContextValue = {
  institution: InstitutionDetails | null;
  error: string | null;
  reload: () => Promise<void>;
  // For pages shown before login (like activation) that should look like the school.
  setPublicBranding: (value: BrandingValues | null) => void;
};

export const InstitutionContext = createContext<InstitutionContextValue | null>(null);