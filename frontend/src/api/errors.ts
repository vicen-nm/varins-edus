import { ApiError } from './client';

const messages: Record<string, string> = {
  invalid_credentials: 'Usuario o contraseña incorrectos.',
  missing_credentials: 'Escribe tu usuario y tu contraseña.',
  tenant_violation: 'No tienes permiso para acceder a esta información.',
  network_error: 'No se pudo conectar con el servidor. Revisa tu conexión.',
  internal_error: 'Ocurrió un error inesperado. Inténtalo de nuevo.',
  institution_not_found: 'No se encontró tu institución.',
  invalid_color: 'Los colores deben tener el formato #RRGGBB.',
  invalid_display_name: 'El nombre debe tener entre 3 y 200 caracteres.',
  platform_user_has_no_institution: 'Los usuarios de plataforma no pertenecen a una institución.',
  http_401: 'Tu sesión venció. Inicia sesión de nuevo.',
  http_403: 'No tienes permiso para realizar esta acción.',
};

const fallback = 'Ocurrió un error inesperado. Inténtalo de nuevo.';

export function errorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    return messages[error.code] ?? fallback;
  }

  return fallback;
}