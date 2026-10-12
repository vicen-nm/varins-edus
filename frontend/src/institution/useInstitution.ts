import { useContext } from 'react';
import { InstitutionContext } from './institutionContext';

export function useInstitution() {
  const value = useContext(InstitutionContext);

  if (!value) {
    throw new Error('useInstitution must be used inside an InstitutionProvider');
  }

  return value;
}