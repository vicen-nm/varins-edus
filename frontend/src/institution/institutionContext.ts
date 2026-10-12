import { createContext } from 'react';
import type { InstitutionDetails } from '../api/types';

export type InstitutionContextValue = {
  institution: InstitutionDetails | null;
  error: string | null;
  reload: () => Promise<void>;
};

export const InstitutionContext = createContext<InstitutionContextValue | null>(null);