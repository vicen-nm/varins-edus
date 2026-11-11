import { useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { Link } from 'react-router';
import { apiFetch } from '../api/client';
import { errorMessage } from '../api/errors';
import type { IssuedResetCode, UserItem, UserList } from '../api/types';
import { useAuth } from '../auth/useAuth';
import { useInstitution } from '../institution/useInstitution';
import { buildStaffUsername, isValidStaffName, normalizeStaffName } from '../users/staffUsername';

export function DevicesPage() {
  const { user } = useAuth();
  const { institution } = useInstitution();

  // Hiding things is only a convenience: the server enforces this permission.
  const allowed = Boolean(!user?.isPlatform && user?.permissions.includes('users.manage'));

  const [data, setData] = useState<UserList | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState(0);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [issued, setIssued] = useState<IssuedResetCode | null>(null);

  const [name, setName] = useState('');
  const [creating, setCreating] = useState(false);

  useEffect(() => {
    if (!allowed) {
      return;
    }

    let cancelled = false;

    apiFetch<UserList>('/api/users?kind=device&take=200')
      .then((result) => {
        if (!cancelled) {
          setData(result);
          setError(null);
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
  }, [allowed, reloadKey]);

  const normalized = normalizeStaffName(name);
  const valid = isValidStaffName(normalized);

  async function handleCreate(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);
    setCreating(true);

    try {
      const result = await apiFetch<IssuedResetCode>('/api/users/devices', {
        method: 'POST',
        body: JSON.stringify({ name: normalized }),
      });

      setIssued(result);
      setName('');
      setReloadKey((k) => k + 1);
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setCreating(false);
    }
  }

  async function handleCode(target: UserItem) {
    setBusyId(target.id);
    setError(null);

    try {
      setIssued(await apiFetch<IssuedResetCode>(`/api/users/${target.id}/reset-code`, { method: 'POST' }));
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setBusyId(null);
    }
  }

  async function handleToggleActive(target: UserItem) {
    if (target.isActive && !window.confirm(`¿Desactivar ${target.username}? Su sesión se cerrará.`)) {
      return;
    }

    setBusyId(target.id);
    setError(null);

    try {
      await apiFetch<void>(`/api/users/${target.id}/active`, {
        method: 'PUT',
        body: JSON.stringify({ isActive: !target.isActive }),
      });

      setReloadKey((k) => k + 1);
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setBusyId(null);
    }
  }

  async function copy(text: string) {
    try {
      await navigator.clipboard.writeText(text);
    } catch {
      // Copying is optional: the code is visible on screen.
    }
  }

  const pairLink = (username: string) =>
    `${window.location.origin}/emparejar?usuario=${encodeURIComponent(username)}`;

  return (
    <main className="page">
      <header className="page__header">
        <div>
          <p className="eyebrow">VARINS · EDU</p>
          <h1>Pantallas del comedor</h1>
        </div>
        <Link to="/" className="link">
          Volver al inicio
        </Link>
      </header>

      {!allowed && (
        <p role="alert" className="error">
          No tienes permiso para administrar pantallas.
        </p>
      )}

      {allowed && issued && (
        <section className="card" role="status" aria-live="polite">
          <h2>Código para emparejar: {issued.username}</h2>
          <p>
            <code>{issued.code}</code>
          </p>
          <p className="hint">
            Se muestra una sola vez y vence el {new Date(issued.expiresAt).toLocaleString('es-CR')} (1 hora). En la
            pantalla, abre el enlace de abajo y escribe el código. Emparejar de nuevo cierra la sesión de la pantalla
            anterior.
          </p>
          <p className="hint">
            Enlace: <code>{pairLink(issued.username)}</code>
          </p>
          <div className="actions">
            <button type="button" onClick={() => copy(issued.code)}>
              Copiar código
            </button>
            <button type="button" className="secondary" onClick={() => setIssued(null)}>
              Cerrar
            </button>
          </div>
        </section>
      )}

      {allowed && (
        <form className="card" onSubmit={handleCreate} noValidate aria-labelledby="device-create-title">
          <h2 id="device-create-title">Nueva pantalla</h2>

          <label htmlFor="device-name">Nombre</label>
          <input
            id="device-name"
            autoComplete="off"
            maxLength={40}
            value={name}
            onChange={(e) => setName(e.target.value)}
          />
          <p className="hint">
            Por ejemplo «comedor-1» (3 a 40 caracteres).
            {normalized && (
              <>
                {' '}
                El usuario será <code>{institution?.slug ? buildStaffUsername(institution.slug, normalized) : normalized}</code>.
              </>
            )}
          </p>

          <div className="actions">
            <button type="submit" disabled={creating || !valid}>
              {creating ? 'Creando…' : 'Crear pantalla'}
            </button>
          </div>
        </form>
      )}

      {allowed && error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}

      {allowed && (
        <section className="card" aria-labelledby="devices-title">
          <h2 id="devices-title">Pantallas</h2>

          {data && data.items.length === 0 && <p>Todavía no hay pantallas.</p>}

          {data && data.items.length > 0 && (
            <div className="table-wrap">
              <table className="table">
                <thead>
                  <tr>
                    <th scope="col">Usuario</th>
                    <th scope="col">Estado</th>
                    <th scope="col">Última conexión</th>
                    <th scope="col">Acciones</th>
                  </tr>
                </thead>
                <tbody>
                  {data.items.map((d) => (
                    <tr key={d.id}>
                      <td>
                        <code>{d.username}</code>
                      </td>
                      <td>{d.isActive ? 'Activa' : 'Desactivada'}</td>
                      <td>{d.lastLoginAt ? new Date(d.lastLoginAt).toLocaleString('es-CR') : 'Nunca emparejada'}</td>
                      <td>
                        <div className="actions">
                          <button
                            type="button"
                            disabled={busyId === d.id || !d.isActive}
                            onClick={() => handleCode(d)}
                          >
                            {busyId === d.id ? 'Generando…' : 'Código de emparejamiento'}
                          </button>
                          <button
                            type="button"
                            className="secondary"
                            disabled={busyId === d.id}
                            onClick={() => handleToggleActive(d)}
                          >
                            {d.isActive ? 'Desactivar' : 'Activar'}
                          </button>
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          <p className="hint">
            La «última conexión» se llena cuando alguien inicia sesión con la cuenta; una pantalla emparejada no la
            cambia.
          </p>
        </section>
      )}
    </main>
  );
}