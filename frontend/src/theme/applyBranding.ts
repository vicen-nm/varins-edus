import type { InstitutionDetails } from '../api/types';
import { BRAND_VARIABLE_NAMES, buildBrandVariables } from './brand';

const DEFAULT_TITLE = 'VarinsEdu';

// Writes the institution's identity into the page. Passing null restores the platform defaults.
export function applyBranding(institution: InstitutionDetails | null): void {
  const root = document.documentElement;

  for (const name of BRAND_VARIABLE_NAMES) {
    root.style.removeProperty(name);
  }

  const primaryColor = institution?.settings?.primaryColor;
  const variables = primaryColor ? buildBrandVariables(primaryColor) : null;

  if (variables) {
    for (const [name, value] of Object.entries(variables)) {
      root.style.setProperty(name, value);
    }
  }

  const name = institution?.settings?.displayName || institution?.name;
  document.title = name ? `${name} · VarinsEdu` : DEFAULT_TITLE;
}