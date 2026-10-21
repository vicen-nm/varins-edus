import type { InstitutionDetails } from '../api/types';
import { BRAND_VARIABLE_NAMES, buildBrandVariables } from './brand';

const DEFAULT_TITLE = 'VarinsEdu';

export type BrandingValues = { name: string | null; primaryColor: string | null };

export function brandingOf(institution: InstitutionDetails | null): BrandingValues | null {
  if (!institution) {
    return null;
  }

  return {
    name: institution.settings?.displayName || institution.name,
    primaryColor: institution.settings?.primaryColor ?? null,
  };
}

// Writes an identity into the page. Passing null restores the platform defaults.
export function applyBranding(branding: BrandingValues | null): void {
  const root = document.documentElement;

  for (const name of BRAND_VARIABLE_NAMES) {
    root.style.removeProperty(name);
  }

  const variables = branding?.primaryColor ? buildBrandVariables(branding.primaryColor) : null;

  if (variables) {
    for (const [name, value] of Object.entries(variables)) {
      root.style.setProperty(name, value);
    }
  }

  document.title = branding?.name ? `${branding.name} · VarinsEdu` : DEFAULT_TITLE;
}