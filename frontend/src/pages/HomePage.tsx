import { Link, Navigate } from 'react-router';
import { useAuth } from '../auth/useAuth';
import { useInstitution } from '../institution/useInstitution';

type NavLink = { to: string; label: string; visible: boolean };

export function HomePage() {
  const { user, logout } = useAuth();
  const { institution, error } = useInstitution();

  if (!user) {
    return null;
  }

  // A paired screen only shows the cafeteria QR.
  if (user.isDevice) {
    return <Navigate to="/access/screen" replace />;
  }

  const institutionName = institution?.settings?.displayName || institution?.name;

  // Hiding links is only a convenience: the server enforces every permission.
  const inInstitution = !user.isPlatform;
  const can = (permission: string) => inInstitution && user.permissions.includes(permission);

  const sections: { title: string; links: NavLink[] }[] = [
    {
      title: 'Mi cuenta',
      links: [
        { to: '/card', label: 'Mi carné', visible: inInstitution },
        { to: '/access/scan', label: 'Registrar mi comida (escanear QR)', visible: inInstitution },
        { to: '/settings/password', label: 'Cambiar mi contraseña', visible: true },
      ],
    },
    {
      title: 'Comedor',
      links: [
        { to: '/access/screen', label: 'Pantalla del comedor', visible: can('access.display') },
        { to: '/access/manual', label: 'Registro manual', visible: can('access.scan') },
        { to: '/access/events', label: 'Registros de acceso', visible: can('access.view') },
        { to: '/access/report', label: 'Reporte del comedor', visible: can('access.view') },
      ],
    },
    {
      title: 'Portería',
      links: [{ to: '/cards/scan', label: 'Verificar carné (escanear)', visible: can('cards.scan') }],
    },
    {
      title: 'Estudiantes',
      links: [
        { to: '/students', label: 'Estudiantes', visible: can('students.view') },
        { to: '/students/import', label: 'Importar desde Excel', visible: can('students.import') },
      ],
    },
    {
      title: 'Configuración',
      links: [
        { to: '/settings/branding', label: 'Identidad de la institución', visible: can('branding.manage') },
        { to: '/access/points', label: 'Puntos de acceso y horarios', visible: can('access.manage') },
        { to: '/users', label: 'Cuentas y contraseñas', visible: can('users.manage') },
        { to: '/devices', label: 'Pantallas del comedor (dispositivos)', visible: can('users.manage') },
      ],
    },
  ]
    .map((section) => ({ ...section, links: section.links.filter((link) => link.visible) }))
    .filter((section) => section.links.length > 0);

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

      {sections.map((section, index) => (
        <section key={section.title} className="card" aria-labelledby={`nav-${index}`}>
          <h2 id={`nav-${index}`}>{section.title}</h2>
          <ul className="link-list">
            {section.links.map((link) => (
              <li key={link.to}>
                <Link to={link.to} className="link">
                  {link.label}
                </Link>
              </li>
            ))}
          </ul>
        </section>
      ))}

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