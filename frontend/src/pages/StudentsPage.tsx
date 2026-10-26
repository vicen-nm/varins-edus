import { useEffect, useState } from 'react';
import { Link } from 'react-router';
import { apiFetch } from '../api/client';
import { errorMessage } from '../api/errors';
import type { GroupItem, RegeneratedCode, StudentItem, StudentList } from '../api/types';
import { useAuth } from '../auth/useAuth';
import { useInstitution } from '../institution/useInstitution';

const PAGE_SIZE = 25;

const CODE_LABELS: Record<StudentItem['codeStatus'], string> = {
  none: 'Sin código',
  pending: 'Pendiente',
  expired: 'Vencido',
  locked: 'Bloqueado',
  used: 'Usado',
};

type Issued = { name: string; code: string; expiresAt: string };

export function StudentsPage() {
  const { user } = useAuth();
  const { institution } = useInstitution();

  // Hiding things is only a convenience: the server enforces these permissions.
  const canView = Boolean(!user?.isPlatform && user?.permissions.includes('students.view'));
  const canGenerate = Boolean(!user?.isPlatform && user?.permissions.includes('students.import'));

  const [search, setSearch] = useState('');
  const [debounced, setDebounced] = useState('');
  const [groupId, setGroupId] = useState('');
  const [account, setAccount] = useState<'all' | 'with' | 'without'>('all');
  const [skip, setSkip] = useState(0);
  const [reloadKey, setReloadKey] = useState(0);

  const [groups, setGroups] = useState<GroupItem[]>([]);
  const [data, setData] = useState<StudentList | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [issued, setIssued] = useState<Issued | null>(null);

  useEffect(() => {
    const timer = window.setTimeout(() => {
      setDebounced(search.trim());
      setSkip(0);
    }, 300);

    return () => window.clearTimeout(timer);
  }, [search]);

  useEffect(() => {
    if (!canView) {
      return;
    }

    let cancelled = false;

    apiFetch<GroupItem[]>('/api/students/groups')
      .then((result) => {
        if (!cancelled) {
          setGroups(result);
        }
      })
      .catch(() => {
        // The filter simply stays empty; the list still works.
      });

    return () => {
      cancelled = true;
    };
  }, [canView]);

  useEffect(() => {
    if (!canView) {
      return;
    }

    let cancelled = false;

    const params = new URLSearchParams({ take: String(PAGE_SIZE), skip: String(skip) });
    if (debounced) {
      params.set('search', debounced);
    }
    if (groupId) {
      params.set('groupId', groupId);
    }
    if (account !== 'all') {
      params.set('hasAccount', String(account === 'with'));
    }

    apiFetch<StudentList>(`/api/students?${params.toString()}`)
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
  }, [canView, debounced, groupId, account, skip, reloadKey]);

  async function handleGenerate(student: StudentItem) {
    setBusyId(student.personId);
    setError(null);

    try {
      const result = await apiFetch<RegeneratedCode>(
        `/api/students/${student.personId}/activation-code`,
        { method: 'POST' },
      );

      setIssued({
        name: `${student.firstName} ${student.lastName}`,
        code: result.code,
        expiresAt: result.expiresAt,
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

  const activationLink = institution ? `${window.location.origin}/activar/${institution.slug}` : null;
  const total = data?.total ?? 0;
  const from = total === 0 ? 0 : skip + 1;
  const to = Math.min(skip + PAGE_SIZE, total);

  return (
    <main className="page">
      <header className="page__header">
        <div>
          <p className="eyebrow">VARINS · EDU</p>
          <h1>Estudiantes</h1>
        </div>
        <Link to="/" className="link">
          Volver al inicio
        </Link>
      </header>

      {!canView && (
        <p role="alert" className="error">
          No tienes permiso para ver a los estudiantes.
        </p>
      )}
                {canGenerate && (
            <p>
              <Link to="/students/import" className="link">
                Importar desde Excel
              </Link>
            </p>
          )}

      {canView && (
        <>
          {issued && (
            <section className="card" role="status" aria-live="polite">
              <h2>Código para {issued.name}</h2>
              <p>
                <code>{issued.code}</code>
              </p>
              <p className="hint">
                Se muestra una sola vez y vence el{' '}
                {new Date(issued.expiresAt).toLocaleDateString('es-CR')}. El código anterior ya no sirve.
              </p>
              {activationLink && (
                <p className="hint">
                  Enlace de activación: <code>{activationLink}</code>
                </p>
              )}
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

          <section className="card filters" aria-label="Filtros">
            <div>
              <label htmlFor="search">Buscar</label>
              <input
                id="search"
                name="search"
                autoComplete="off"
                placeholder="Nombre o cédula"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
              />
            </div>
            <div>
              <label htmlFor="group">Grupo</label>
              <select
                id="group"
                value={groupId}
                onChange={(e) => {
                  setGroupId(e.target.value);
                  setSkip(0);
                }}
              >
                <option value="">Todos</option>
                {groups.map((g) => (
                  <option key={g.id} value={g.id}>
                    {g.name}
                  </option>
                ))}
              </select>
            </div>
            <div>
              <label htmlFor="account">Cuenta</label>
              <select
                id="account"
                value={account}
                onChange={(e) => {
                  setAccount(e.target.value as 'all' | 'with' | 'without');
                  setSkip(0);
                }}
              >
                <option value="all">Todas</option>
                <option value="with">Con cuenta</option>
                <option value="without">Sin cuenta</option>
              </select>
            </div>
          </section>

          {error && (
            <p role="alert" className="error">
              {error}
            </p>
          )}

          <section className="card" aria-labelledby="list-title">
            <h2 id="list-title">{data ? `${from}–${to} de ${total}` : 'Cargando…'}</h2>

            {data && data.items.length === 0 && <p>No hay estudiantes con esos filtros.</p>}

            {data && data.items.length > 0 && (
              <div className="table-wrap">
                <table className="table">
                  <thead>
                    <tr>
                      <th scope="col">Cédula</th>
                      <th scope="col">Nombre</th>
                      <th scope="col">Grupo</th>
                      <th scope="col">Cuenta</th>
                      <th scope="col">Código</th>
                      {canGenerate && <th scope="col">Acción</th>}
                    </tr>
                  </thead>
                  <tbody>
                    {data.items.map((s) => (
                      <tr key={s.personId}>
                        <td>
                          <code>{s.identificationNumber}</code>
                        </td>
                        <td>
                          {s.firstName} {s.lastName}
                        </td>
                        <td>{s.group ?? '—'}</td>
                        <td>{s.hasAccount ? 'Sí' : 'No'}</td>
                        <td>{CODE_LABELS[s.codeStatus]}</td>
                        {canGenerate && (
                          <td>
                            {!s.hasAccount && (
                              <button
                                type="button"
                                disabled={busyId === s.personId}
                                onClick={() => handleGenerate(s)}
                              >
                                {busyId === s.personId ? 'Generando…' : 'Nuevo código'}
                              </button>
                            )}
                          </td>
                        )}
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}

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
        </>
      )}
    </main>
  );
}