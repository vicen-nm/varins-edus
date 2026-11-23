// What each action of the audit log means, in plain words.
const ACTIONS: Record<string, string> = {
  'auth.login.succeeded': 'Inicio de sesión',
  'auth.login.failed': 'Intento de acceso fallido',
  'auth.account.locked': 'Cuenta bloqueada por intentos fallidos',
  'auth.logout': 'Cierre de sesión',
  'auth.refresh.reuse': 'Sesión cerrada por reuso de una credencial',
  'auth.password.reset.succeeded': 'Contraseña restablecida con un código',
  'auth.password.reset.failed': 'Restablecimiento de contraseña fallido',
  'auth.activation.succeeded': 'Cuenta de estudiante activada',
  'auth.activation.failed': 'Activación de cuenta fallida',
  'auth.device.paired': 'Pantalla emparejada',
  'auth.device.pair.failed': 'Emparejamiento de pantalla fallido',
  'card.scan.reused': 'Código de carné reutilizado',
  'AccessReport.exported': 'Reporte del comedor descargado',
};

const ENTITIES: Record<string, string> = {
  User: 'Cuenta',
  UserPreference: 'Preferencias de pantalla',
  Vehicle: 'Vehículo',
  Visit: 'Visita',
  ExitPermit: 'Permiso de salida',
  ImportBatch: 'Importación de estudiantes',
  AccessPoint: 'Punto de acceso',
  AccessService: 'Horario del comedor',
  InstitutionSettings: 'Identidad del colegio',
  ActivationCode: 'Código de activación',
  PasswordResetCode: 'Código de contraseña',
  Institution: 'Colegio',
  Role: 'Rol',
  Group: 'Grupo',
  Person: 'Persona',
};

const VERBS: Record<string, string> = {
  created: 'creación',
  updated: 'cambio',
  deleted: 'eliminación',
};

export function describeAction(action: string): string {
  const known = ACTIONS[action];

  if (known) {
    return known;
  }

  // "Vehicle.updated" becomes "Vehículo: cambio".
  const match = /^([A-Za-z]+)\.(created|updated|deleted)$/.exec(action);

  if (match) {
    return `${ENTITIES[match[1]] ?? match[1]}: ${VERBS[match[2]]}`;
  }

  return action;
}

export function shortId(value: string | null): string {
  return value ? value.slice(0, 8) : '—';
}