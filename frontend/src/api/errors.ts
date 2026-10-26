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
  file_required: 'Elige un archivo de Excel.',
  file_too_large: 'El archivo no puede pesar más de 5 MB.',
  invalid_file: 'No se pudo leer el archivo. Debe ser un .xlsx válido.',
  invalid_mapping: 'La selección de columnas no es válida.',
  empty_file: 'El archivo está vacío.',
  too_many_columns: 'El archivo tiene demasiadas columnas.',
  too_many_rows: 'El archivo tiene demasiadas filas (máximo 5000).',
  missing_required_mapping: 'Falta indicar la columna de cédula, nombre o apellidos.',
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

const rowMessages: Record<string, string> = {
  duplicate_in_file: 'La cédula está repetida en el archivo.',
  missing_identification_number: 'Falta la cédula.',
  identification_number_too_long: 'La cédula es demasiado larga.',
  invalid_identification_type: 'El tipo de identificación no es válido.',
  missing_first_name: 'Falta el nombre.',
  first_name_too_long: 'El nombre es demasiado largo.',
  missing_last_name: 'Faltan los apellidos.',
  last_name_too_long: 'Los apellidos son demasiado largos.',
  group_name_too_long: 'El nombre del grupo es demasiado largo.',
  phone_too_long: 'El teléfono es demasiado largo.',
  invalid_email: 'El correo no es válido.',
};

export function rowErrorMessage(code: string): string {
  return rowMessages[code] ?? 'La fila no es válida.';
}