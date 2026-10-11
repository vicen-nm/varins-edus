import { useEffect, useState } from 'react';
import { apiFetch } from '../api/client';
import { errorMessage } from '../api/errors';
import type { InstitutionDetails } from '../api/types';
import { useAuth } from '../auth/useAuth';

export function HomePage() {
  const { user, logout } = useAuth();
  const [institution, setInstitution] = useState<InstitutionDetails | null>(null);
  const [error, setError] = useState<string | null>(null);

  // Platform users belong to no institution, so there is nothing to load for them.
  useEffect(() => {
    if (!user || user.isPlatform) {
      return;
    }

    let cancelled = false;

    apiFetch<InstitutionDetails>('/api/institution')
      .then((data) => {
        if (!cancelled) {
          setInstitution(data);
        }
      })
      .catch((e) => {
        if (!cancelled) {
          setError(errorMessage(e));
        }
      });

    return () => {
      cancelled = true;
    };
  }, [user]);

  if (!user) {
    return null;
  }

  return (
    <main className="page">
      <header className="page__header">
        <div>
          <p className="eyebrow">VARINS · EDU</p>
          <h1>Hola, {user.username}</h1>
          <p>
            {user.isPlatform
              ? 'Administrador de plataforma'
              : (institution?.name ?? 'Cargando tu institución…')}
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