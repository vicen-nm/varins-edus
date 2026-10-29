import { Link } from 'react-router';
import { useAuth } from '../auth/useAuth';
import { useInstitution } from '../institution/useInstitution';

export function HomePage() {
  const { user, logout } = useAuth();
  const { institution, error } = useInstitution();
  

  if (!user) {
    return null;
  }

  const institutionName = institution?.settings?.displayName || institution?.name;
  const canEditBranding = !user.isPlatform && user.permissions.includes('branding.manage');
  const canViewStudents = !user.isPlatform && user.permissions.includes('students.view');
  const canShowCard = !user.isPlatform;
  const canManageAccess = !user.isPlatform && user.permissions.includes('access.manage');
  const canScanAccess = !user.isPlatform && user.permissions.includes('access.scan');
  const canViewAccess = !user.isPlatform && user.permissions.includes('access.view');
  

  return (
    <main className="page">
      <header className="page__header">
        <div>
          <p className="eyebrow">VARINS · EDU</p>
          <h1>Hola, {user.username}</h1>
          <p>
            {user.isPlatform
              ? 'Administrador de plataforma'
              : (institutionName ?? 'Cargando tu institución…')}
          </p>
        </div>
        <button type="button" className="secondary" onClick={logout}>
          Cerrar sesión
        </button>
      </header>

      {error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}

      {canEditBranding && (
        <p>
          <Link to="/settings/branding" className="link">
            Identidad de la institución
          </Link>
        </p>
      )}

            {canViewStudents && (
        <p>
          <Link to="/students" className="link">
            Estudiantes
          </Link>
        </p>
      )}

            {canShowCard && (
        <p>
          <Link to="/card" className="link">
            Mi carné
          </Link>
        </p>
      )}

            {canManageAccess && (
        <p>
          <Link to="/access/points" className="link">
            Puntos de acceso
          </Link>
        </p>
      )}

      {canScanAccess && (
        <p>
          <Link to="/access/manual" className="link">
            Registro manual del comedor
          </Link>
        </p>
      )}

            {canScanAccess && (
        <p>
          <Link to="/access/screen" className="link">
            Pantalla del comedor
          </Link>
        </p>
      )}

      {canViewAccess && (
        <p>
          <Link to="/access/events" className="link">
            Registros de acceso
          </Link>
        </p>
      )}

      {!user.isPlatform && (
        <p>
          <Link to="/access/scan" className="link">
            Registrar mi comida (escanear QR)
          </Link>
        </p>
      )}

            {canViewAccess && (
        <p>
          <Link to="/access/report" className="link">
            Reporte del comedor
          </Link>
        </p>
      )}

      <section className="card" aria-labelledby="permissions-title">
        <h2 id="permissions-title">Tus permisos</h2>
        {user.permissions.length === 0 ? (
          <p>No tienes permisos especiales.</p>
        ) : (
          <ul className="chips">
            {user.permissions.map((permission) => (
              <li key={permission}>
                <code>{permission}</code>
              </li>
            ))}
          </ul>
        )}
      </section>

      {institution && (
        <section className="card" aria-labelledby="modules-title">
          <h2 id="modules-title">Módulos de tu institución</h2>
          <ul>
            {institution.modules.map((module) => (
              <li key={module.moduleKey}>
                <code>{module.moduleKey}</code>: {module.isEnabled ? 'activo' : 'apagado'}
              </li>
            ))}
          </ul>
        </section>
      )}
    </main>
  );
}