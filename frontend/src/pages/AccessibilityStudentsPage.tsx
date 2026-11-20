import { useEffect, useState } from 'react';
import { Link } from 'react-router';
import { apiFetch } from '../api/client';
import { errorMessage } from '../api/errors';
import type { DisplayPreferences, StudentItem, StudentList } from '../api/types';
import { useAuth } from '../auth/useAuth';
import { PreferencesForm } from '../components/PreferencesForm';

export function AccessibilityStudentsPage() {
  const { user } = useAuth();

  // Hiding things is only a convenience: the server enforces these permissions.
  const allowed = Boolean(
    !user?.isPlatform &&
      user?.permissions.includes('accessibility.manage') &&
      user?.permissions.includes('students.view'),
  );

  const [search, setSearch] = useState('');
  const [debounced, setDebounced] = useState('');
  const [data, setData] = useState<StudentList | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [selected, setSelected] = useState<StudentItem | null>(null);

  useEffect(() => {
    const timer = window.setTimeout(() => setDebounced(search.trim()), 300);

    return () => window.clearTimeout(timer);
  }, [search]);

  useEffect(() => {
    if (!allowed) {
      return;
    }

    let cancelled = false;

    // Only students with an account have a screen to configure.
    const params = new URLSearchParams({ take: '25', hasAccount: 'true' });
    if (debounced) {
      params.set('search', debounced);
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
  }, [allowed, debounced]);

  return (
    <main className="page">
      <header className="page__header">
        <div>
          <p className="eyebrow">VARINS · EDU</p>
          <h1>Pantalla de los estudiantes</h1>
        </div>
        <Link to="/" className="link">
          Volver al inicio
        </Link>
      </header>

      {!allowed && (
        <p role="alert" className="error">
          No tienes permiso para configurar la pantalla de los estudiantes.
        </p>
      )}

      {allowed && (
        <p className="hint">
          Aquí solo se elige cómo ve la pantalla un estudiante. No escribas ni busques diagnósticos: el sistema no los
          guarda. El estudiante puede cambiar estas opciones después.
        </p>
      )}

      {allowed && error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}

      {allowed && selected && (
        <StudentPreferences key={selected.personId} student={selected} onClose={() => setSelected(null)} />
      )}

      {allowed && (
        <section className="card" aria-labelledby="students-pref-title">
          <h2 id="students-pref-title">Estudiantes con cuenta</h2>

          <label htmlFor="students-pref-search">Buscar</label>
          <input
            id="students-pref-search"
            autoComplete="off"
            placeholder="Nombre o cédula"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />

          {data && data.items.length === 0 && <p>No hay estudiantes con cuenta con esa búsqueda.</p>}

          {data && data.items.length > 0 && (
            <div className="table-wrap">
              <table className="table">
                <thead>
                  <tr>
                    <th scope="col">Estudiante</th>
                    <th scope="col">Grupo</th>
                    <th scope="col">Acción</th>
                  </tr>
                </thead>
                <tbody>
                  {data.items.map((s) => (
                    <tr key={s.personId}>
                      <td>
                        {s.firstName} {s.lastName}
                      </td>
                      <td>{s.group ?? '—'}</td>
                      <td>
                        <button type="button" className="secondary" onClick={() => setSelected(s)}>
                          Configurar pantalla
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>
      )}
    </main>
  );
}

function StudentPreferences({ student, onClose }: { student: StudentItem; onClose: () => void }) {
  const [initial, setInitial] = useState<DisplayPreferences | null>(null);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    apiFetch<DisplayPreferences>(`/api/students/${student.personId}/preferences`)
      .then((result) => {
        if (!cancelled) {
          setInitial(result);
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
  }, [student.personId]);

  async function handleSave(next: DisplayPreferences) {
    setBusy(true);
    setMessage(null);
    setError(null);

    try {
      setInitial(
        await apiFetch<DisplayPreferences>(`/api/students/${student.personId}/preferences`, {
          method: 'PUT',
          body: JSON.stringify(next),
        }),
      );
      setMessage('Cambios guardados. Se verán la próxima vez que el estudiante abra la aplicación.');
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="card" aria-labelledby="student-pref-title">
      <h2 id="student-pref-title">
        Pantalla de {student.firstName} {student.lastName}
      </h2>

      {!initial && !error && <p>Cargando…</p>}

      {initial && <PreferencesForm initial={initial} busy={busy} submitLabel="Guardar" onSave={handleSave} />}

      {error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}

      {message && (
        <p role="status" className="success">
          {message}
        </p>
      )}

      <div className="actions">
        <button type="button" className="secondary" onClick={onClose}>
          Cerrar
        </button>
      </div>
    </section>
  );
}