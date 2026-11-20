import { useContext } from 'react';
import { PreferencesContext } from './preferencesContext';

export function usePreferences() {
  const value = useContext(PreferencesContext);

  if (!value) {
    throw new Error('usePreferences must be used inside a PreferencesProvider');
  }

  return value;
}