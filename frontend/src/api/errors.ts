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
  activation_failed:
    'No pudimos activar tu cuenta con esos datos. Revisa tu cédula y tu código, o pide uno nuevo en tu colegio.',
  weak_password:
    'La contraseña es muy débil. Usa al menos 8 caracteres, sin tu cédula ni claves comunes como 12345678.',
      student_not_found: 'No se encontró al estudiante.',
  student_has_account: 'Este estudiante ya activó su cuenta y no necesita un código.',
  too_many_requests: 'Demasiados intentos. Espera un momento y vuelve a intentarlo.',
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