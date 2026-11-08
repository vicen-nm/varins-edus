import { ApiError } from './client';

const messages: Record<string, string> = {
    invalid_credentials:
    'Usuario o contraseña incorrectos. Tras varios intentos fallidos la cuenta se bloquea 15 minutos.',
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
    'La contraseña es muy débil. Usa al menos 8 caracteres (10 si eres personal), sin tu usuario o cédula ni claves comunes como 12345678.',
  student_has_account: 'Este estudiante ya activó su cuenta y no necesita un código.',
  file_required: 'Elige un archivo de Excel.',
  file_too_large: 'El archivo no puede pesar más de 5 MB.',
  invalid_file: 'No se pudo leer el archivo. Debe ser un .xlsx válido.',
  invalid_mapping: 'La selección de columnas no es válida.',
  empty_file: 'El archivo está vacío.',
  too_many_columns: 'El archivo tiene demasiadas columnas.',
  too_many_rows: 'El archivo tiene demasiadas filas (máximo 5000).',
  missing_required_mapping: 'Falta indicar la columna de cédula, nombre o apellidos.',
  card_unavailable: 'Tu cuenta no tiene un carné digital activo. Consulta en tu colegio.',
  access_point_not_found: 'No se encontró el punto de acceso.',
  access_service_not_found: 'No se encontró el horario.',
  access_point_name_taken: 'Ya existe un punto con ese nombre.',
  access_service_name_taken: 'Ya existe un horario con ese nombre en este punto.',
  invalid_access_name: 'El nombre no es válido (entre 2 y 100 caracteres).',
  invalid_access_kind: 'El tipo de punto no es válido.',
  invalid_location: 'La ubicación no es válida. Revisa la latitud y la longitud.',
  invalid_radius: 'El radio debe estar entre 10 y 2000 metros.',
  invalid_accuracy: 'La precisión máxima debe estar entre 10 y 1000 metros.',
  invalid_schedule: 'La hora de inicio debe ser menor que la de fin.',
  student_not_found: 'No se encontró un estudiante activo con esa cédula.',
  already_registered: 'Este estudiante ya está registrado en este servicio hoy.',
  no_service_now: 'No hay un servicio abierto en este momento.',
  student_unavailable: 'Esta cuenta no puede registrarse en el comedor.',
  too_far: 'Estás muy lejos del punto de registro.',
  location_inaccurate: 'Tu ubicación es poco precisa. Sal a un lugar abierto e inténtalo de nuevo.',
  expired_qr: 'El código QR venció. Escanea el que está en pantalla.',
  invalid_qr: 'El código QR no es válido.',
  point_not_configured: 'El punto todavía no tiene ubicación configurada.',
  invalid_date_range: 'La fecha inicial no puede ser posterior a la final.',
  range_too_long: 'El rango no puede pasar de 92 días.',
  report_too_large: 'El reporte tiene demasiados registros. Elige un rango más corto.',
    reset_failed:
    'No pudimos cambiar la contraseña con esos datos. Revisa tu usuario y el código, o pide uno nuevo en tu colegio.',
  invalid_current_password: 'La contraseña actual no es correcta.',
  password_change_failed: 'No se pudo cambiar la contraseña. Inicia sesión de nuevo.',
  invalid_username: 'El usuario no es válido: usa de 3 a 40 letras minúsculas, números, punto, guion o guion bajo.',
  invalid_role: 'El rol no es válido.',
  username_taken: 'Ya existe una cuenta con ese usuario.',
  user_not_found: 'No se encontró la cuenta.',
  user_inactive: 'La cuenta está desactivada. Actívala para generar un código.',
  not_staff: 'Los roles solo se cambian en cuentas del personal.',
  last_admin: 'No puedes dejar al colegio sin administradores.',
  cannot_modify_self: 'No puedes cambiar tu propia cuenta desde aquí.',
  session_expired: 'Tu sesión venció. Inicia sesión de nuevo.',
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