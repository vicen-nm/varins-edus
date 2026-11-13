import { useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { Link } from 'react-router';
import { apiFetch } from '../api/client';
import { errorMessage } from '../api/errors';
import type { IssuedResetCode, RoleItem, UserItem, UserList } from '../api/types';
import { useAuth } from '../auth/useAuth';
import { useInstitution } from '../institution/useInstitution';
import { buildStaffUsername, isValidStaffName, normalizeStaffName } from '../users/staffUsername';

const PAGE_SIZE = 25;

const ROLE_LABELS: Record<string, string> = {
  institution_admin: 'Administrador del colegio',
  teacher: 'Docente',
  cafeteria_staff: 'Personal de comedor',
  gate_staff: 'Personal de portería',
  student: 'Estudiante',
};

const roleLabel = (key: string) => ROLE_LABELS[key] ?? key;

type Kind = 'staff' | 'student' | 'all';
type Issued = { username: string; code: string; expiresAt: string; isNew: boolean };

// Students' usernames hold the cédula without hyphens, so "1-1111-1111" must find them.
const searchTerm = (text: string) => (/^[\d-]+$/.test(text) ? text.replace(/-/g, '') : text);

export function UsersPage() {
  const { user } = useAuth();
  const { institution } = useInstitution();

  // Hiding things is only a convenience: the server enforces this permission.
  const allowed = Boolean(!user?.isPlatform && user?.permissions.includes('users.manage'));

  const [kind, setKind] = useState<Kind>('staff');
  const [search, setSearch] = useState('');
  const [debounced, setDebounced] = useState('');
  const [skip, setSkip] = useState(0);
  const [reloadKey, setReloadKey] = useState(0);

  const [roles, setRoles] = useState<RoleItem[]>([]);
  const [data, setData] = useState<UserList | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [issued, setIssued] = useState<Issued | null>(null);
  const [creating, setCreating] = useState(false);
  const [editing, setEditing] = useState<UserItem | null>(null);

  useEffect(() => {
    const timer = window.setTimeout(() => {
      setDebounced(searchTerm(search.trim()));
      setSkip(0);
    }, 300);

    return () => window.clearTimeout(timer);
  }, [search]);

  useEffect(() => {
    if (!allowed) {
      return;
    }

    let cancelled = false;

    apiFetch<RoleItem[]>('/api/users/roles')
      .then((result) => {
        if (!cancelled) {
          setRoles(result);
        }
      })
      .catch(() => {
        // The role selectors simply stay empty; the list still works.
      });

    return () => {
      cancelled = true;
    };
  }, [allowed]);

  useEffect(() => {
    if (!allowed) {
      return;
    }

    let cancelled = false;

    const params = new URLSearchParams({ kind, take: String(PAGE_SIZE), skip: String(skip) });
    if (debounced) {
      params.set('search', debounced);
    }

    apiFetch<UserList>(`/api/users?${params.toString()}`)
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
  }, [allowed, kind, debounced, skip, reloadKey]);

  async function handleReset(target: UserItem) {
    setBusyId(target.id);
    setError(null);

    try {
      const result = await apiFetch<IssuedResetCode>(`/api/users/${target.id}/reset-code`, { method: 'POST' });

      setIssued({ username: result.username, code: result.code, expiresAt: result.expiresAt, isNew: false });
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setBusyId(null);
    }
  }

  async function handleToggleActive(target: UserItem) {
    if (target.isActive && !window.confirm(`¿Desactivar la cuenta ${target.username}? No podrá iniciar sesión.`)) {
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

  const resetLink = (username: string) =>
    `${window.location.origin}/reset-password?usuario=${encodeURIComponent(username)}`;

  const total = data?.total ?? 0;
  const from = total === 0 ? 0 : skip + 1;
  const to = Math.min(skip + PAGE_SIZE, total);

  return (
    <main className="page">
      <header className="page__header">
        <div>
          <p className="eyebrow">VARINS · EDU</p>
          <h1>Cuentas</h1>
        </div>
        <Link to="/" className="link">
          Volver al inicio
        </Link>
      </header>

      {!allowed && (
        <p role="alert" className="error">
          No tienes permiso para administrar cuentas.
        </p>
      )}

      {allowed && issued && (
        <section className="card" role="status" aria-live="polite">
          <h2>{issued.isNew ? 'Cuenta creada' : 'Código nuevo'}: {issued.username}</h2>
          <p>
            <code>{issued.code}</code>
          </p>
          <p className="hint">
            Se muestra una sola vez y vence el {new Date(issued.expiresAt).toLocaleString('es-CR')}. Entrégalo en
            persona. La persona entra al enlace de abajo con su usuario y este código, y elige su propia contraseña.
            Un código nuevo anula el anterior.
          </p>
          <p className="hint">
            Enlace: <code>{resetLink(issued.username)}</code>
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

      {allowed && creating && (
        <CreateForm
          roles={roles}
          slug={institution?.slug ?? ''}
          onCreated={(result) => {
            setIssued({ username: result.username, code: result.code, expiresAt: result.expiresAt, isNew: true });
            setCreating(false);
            setReloadKey((k) => k + 1);
          }}
          onCancel={() => setCreating(false)}
        />
      )}

      {allowed && editing && (
        <RolesForm
          key={editing.id}
          target={editing}
          roles={roles}
          onSaved={() => {
            setEditing(null);
            setReloadKey((k) => k + 1);
          }}
          onCancel={() => setEditing(null)}
        />
      )}

      {allowed && (
        <section className="card filters" aria-label="Filtros">
          <div>
            <label htmlFor="users-kind">Mostrar</label>
            <select
              id="users-kind"
              value={kind}
              onChange={(e) => {
                setKind(e.target.value as Kind);
                setSkip(0);
              }}
            >
              <option value="staff">Personal</option>
              <option value="student">Estudiantes con cuenta</option>
              <option value="all">Todas</option>
            </select>
          </div>
          <div>
            <label htmlFor="users-search">Buscar</label>
            <input
              id="users-search"
              autoComplete="off"
              placeholder="Usuario o cédula"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
          </div>
          <div>
            <p className="hint">Si alguien olvidó su contraseña, genera un código nuevo.</p>
            {!creating && (
              <div className="actions">
                <button type="button" onClick={() => setCreating(true)}>
                  Nueva cuenta de personal
                </button>
              </div>
            )}
          </div>
        </section>
      )}

      {allowed && error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}

      {allowed && (
        <section className="card" aria-labelledby="users-title">
          <h2 id="users-title">{data ? `${from}–${to} de ${total}` : 'Cargando…'}</h2>

          {data && data.items.length === 0 && <p>No hay cuentas con esos filtros.</p>}

          {data && data.items.length > 0 && (
            <div className="table-wrap">
              <table className="table">
                <thead>
                  <tr>
                    <th scope="col">Usuario</th>
                    <th scope="col">Tipo</th>
                    <th scope="col">Roles</th>
                    <th scope="col">Estado</th>
                    <th scope="col">Último ingreso</th>
                    <th scope="col">Acciones</th>
                  </tr>
                </thead>
                <tbody>
                  {data.items.map((u) => (
                    <tr key={u.id}>
                      <td>
                        <code>{u.username}</code>
                      </td>
                      <td>{u.isStudent ? 'Estudiante' : u.isDevice ? 'Pantalla' : 'Personal'}</td>
                      <td>{u.roles.map(roleLabel).join(', ') || '—'}</td>
                      <td>{!u.isActive ? 'Inactivo' : u.isLocked ? 'Bloqueado (temporal)' : 'Activo'}</td>
                      <td>{u.lastLoginAt ? new Date(u.lastLoginAt).toLocaleString('es-CR') : 'Nunca'}</td>
                      <td>
                        {u.id === user?.userId ? (
                          <span className="hint">Tu cuenta</span>
                        ) : (
                          <div className="actions">
                            {!u.isStudent && !u.isDevice && (
                              <button type="button" className="secondary" onClick={() => setEditing(u)}>
                                Roles
                              </button>
                            )}
                            <button
                              type="button"
                              disabled={busyId === u.id || !u.isActive || u.isDevice}
                              onClick={() => handleReset(u)}
                            >
                              {busyId === u.id ? 'Generando…' : 'Nuevo código'}
                            </button>
                            <button
                              type="button"
                              className="secondary"
                              disabled={busyId === u.id}
                              onClick={() => handleToggleActive(u)}
                            >
                              {u.isActive ? 'Desactivar' : 'Activar'}
                            </button>
                          </div>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          <p className="hint">
            «Bloqueado» se libera solo a los 15 minutos, o cuando la persona restablece su contraseña con un código.
          </p>

          <div className="actions">
            <button
              type="button"
              className="secondary"
              disabled={skip === 0}
              onClick={() => setSkip(Math.max(skip - PAGE_SIZE, 0))}
            >
              Anterior
            </button>
            <button
              type="button"
              className="secondary"
              disabled={skip + PAGE_SIZE >= total}
              onClick={() => setSkip(skip + PAGE_SIZE)}
            >
              Siguiente
            </button>
          </div>
        </section>
      )}
    </main>
  );
}

function CreateForm({
  roles,
  slug,
  onCreated,
  onCancel,
}: {
  roles: RoleItem[];
  slug: string;
  onCreated: (result: IssuedResetCode) => void;
  onCancel: () => void;
}) {
  const [name, setName] = useState('');
  const [roleKey, setRoleKey] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const normalized = normalizeStaffName(name);
  const valid = isValidStaffName(normalized);
  const selected = roleKey || roles[0]?.key || '';

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);
    setBusy(true);

    try {
      const result = await apiFetch<IssuedResetCode>('/api/users', {
        method: 'POST',
        body: JSON.stringify({ username: normalized, roleKey: selected }),
      });

      onCreated(result);
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="card" onSubmit={handleSubmit} noValidate aria-labelledby="create-title">
      <h2 id="create-title">Nueva cuenta de personal</h2>

      <label htmlFor="create-name">Nombre de usuario</label>
      <input
        id="create-name"
        autoComplete="off"
        maxLength={40}
        value={name}
        onChange={(e) => setName(e.target.value)}
      />
      <p className="hint">
        Minúsculas, números, punto, guion o guion bajo (3 a 40 caracteres).
        {normalized && (
          <>
            {' '}El usuario será <code>{slug ? buildStaffUsername(slug, normalized) : normalized}</code>.
          </>
        )}
      </p>

      <label htmlFor="create-role">Rol</label>
      <select id="create-role" value={selected} onChange={(e) => setRoleKey(e.target.value)}>
        {roles.map((r) => (
          <option key={r.key} value={r.key}>
            {roleLabel(r.key)}
          </option>
        ))}
      </select>

      {error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}

      <div className="actions">
        <button type="submit" disabled={busy || !valid || !selected}>
          {busy ? 'Creando…' : 'Crear cuenta'}
        </button>
        <button type="button" className="secondary" onClick={onCancel}>
          Cancelar
        </button>
      </div>
    </form>
  );
}

function RolesForm({
  target,
  roles,
  onSaved,
  onCancel,
}: {
  target: UserItem;
  roles: RoleItem[];
  onSaved: () => void;
  onCancel: () => void;
}) {
  const [selected, setSelected] = useState<Set<string>>(() => new Set(target.roles));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  function toggle(key: string, checked: boolean) {
    // A new Set: React only redraws when the state is a new object.
    const next = new Set(selected);

    if (checked) {
      next.add(key);
    } else {
      next.delete(key);
    }

    setSelected(next);
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);
    setBusy(true);

    try {
      await apiFetch<void>(`/api/users/${target.id}/roles`, {
        method: 'PUT',
        body: JSON.stringify({ roleKeys: [...selected] }),
      });

      onSaved();
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="card" onSubmit={handleSubmit} noValidate aria-labelledby="roles-title">
      <h2 id="roles-title">Roles de {target.username}</h2>
      <p className="hint">Los cambios se aplican la próxima vez que la persona inicie sesión.</p>

      {roles.map((r) => (
        <label key={r.key} className="check" htmlFor={`role-${r.key}`}>
          <input
            id={`role-${r.key}`}
            type="checkbox"
            checked={selected.has(r.key)}
            onChange={(e) => toggle(r.key, e.target.checked)}
          />
          {roleLabel(r.key)}
        </label>
      ))}

      {error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}

      <div className="actions">
        <button type="submit" disabled={busy || selected.size === 0}>
          {busy ? 'Guardando…' : 'Guardar roles'}
        </button>
        <button type="button" className="secondary" onClick={onCancel}>
          Cancelar
        </button>
      </div>
    </form>
  );
}