export function statusLabel(status: string): string {
  switch (status) {
    case 'inside':
      return 'Dentro';
    case 'exited':
      return 'Salió';
    case 'no_exit':
      return 'Sin salida registrada';
    default:
      return status;
  }
}

// The server decides which one it registers; this is what the guard expects to happen.
export function actionLabel(inside: boolean): string {
  return inside ? 'Registrar salida' : 'Registrar ingreso';
}

export function resultLabel(action: string): string {
  return action === 'exit' ? 'Salida registrada' : 'Ingreso registrado';
}