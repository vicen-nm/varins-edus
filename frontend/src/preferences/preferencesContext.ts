import { createContext } from 'react';
import type { DisplayPreferences } from '../api/types';

export type PreferencesContextValue = {
  preferences: DisplayPreferences;
  save: (next: DisplayPreferences) => Promise<DisplayPreferences>;
};

export const PreferencesContext = createContext<PreferencesContextValue | null>(null);