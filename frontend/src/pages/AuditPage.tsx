import { useEffect, useState } from 'react';
import { Link } from 'react-router';
import { apiFetch } from '../api/client';
import { errorMessage } from '../api/errors';
import type { AuditPage as AuditPageData } from '../api/types';
import { useAuth } from '../auth/useAuth';
import { describeAction, shortId } from '../audit/auditRules';
import { todayIso } from '../exits/exitRules';

const PAGE_SIZE = 25;

// Texts that help to find what matters: the actions that start with these words.
const SUGGESTIONS = ['auth.', 'login', 'locked', 'reuse', 'card.', 'Vehicle', 'Visit', 'ExitPermit', 'User', 'export'];

const stamp = (iso: string) =>
  new Date(iso).toLocaleString('es-CR', { dateStyle: 'short', timeStyle: 'medium' });

export function AuditPage() {
  const { user } = useAuth();

  // Hiding things is only a convenience: the server enforces this permission.
  const allowed = Boolean(user?.permissions.includes('audit.view'));

  const [search, setSearch] = useState('');
  const [debounced, setDebounced] = useState('');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [skip, setSkip] = useState(0);
  const [reloadKey, setReloadKey] = useState(0);

  const [data, setData] = useState<AuditPageData | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const timer = window.setTimeout(() => {
      setDebounced(search.trim());
      setSkip(0);
    }, 300);

    return () => window.clearTimeout(timer);
  }, [search]);

  useEffect(() => {
    if (!allowed) {
      return;
    }

    let cancelled = false;

    const params = new URLSearchParams({ take: String(PAGE_SIZE), skip: String(skip) });
    if (debounced) {
      params.set('search', debounced);
    }
    if (from) {
      params.set('from', from);
    }
    if (to) {
      params.set('to', to);
    }

    apiFetch<AuditPageData>(`/api/audit?${params.toString()}`)
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
  }, [allowed, debounced, from, to, skip, reloadKey]);

  function today() {
    const day = todayIso();

    setFrom(day);
    setTo(day);
    setSkip(0);
  }

  const total = data?.total ?? 0;
  const first = total === 0 ? 0 : skip + 1;
  const last = Math.min(skip + PAGE_SIZE, total);

  return (
    <main className="page">
      <header className="page__header">
        <div>
          <p className="eyebrow">VARINS · EDU</p>
          <h1>Bitácora</h1>
        </div>
        <Link to="/" className="link">
          Volver al inicio
        </Link>
      </header>

      {!allowed && (
        <p role="alert" className="error">
          No tienes permiso para ver la bitácora.
        </p>
      )}

      {allowed && (
        <p className="hint">
          Registra quién hizo qué y cuándo. Los datos personales no se guardan: aparecen como «[redacted]».
        </p>
      )}

      {allowed && (
        <section className="card filters" aria-label="Filtros">
          <div>
            <label htmlFor="audit-search">Buscar</label>
            <input
              id="audit-search"
              list="audit-suggestions"
              autoComplete="off"
              placeholder="Acción o tipo"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
            <datalist id="audit-suggestions">
              {SUGGESTIONS.map((s) => (
                <option key={s} value={s} />
              ))}
            </datalist>
          </div>
          <div>
            <label htmlFor="audit-from">Desde</label>
            <input
              id="audit-from"
              type="date"
              value={from}
              onChange={(e) => {
                setFrom(e.target.value);
                setSkip(0);
              }}
            />
          </div>
          <div>
            <label htmlFor="audit-to">Hasta</label>
            <input
              id="audit-to"
              type="date"
              value={to}
              onChange={(e) => {
                setTo(e.target.value);
                setSkip(0);
              }}
            />
            <div className="actions">
              <button type="button" className="secondary" onClick={today}>
                Solo hoy
              </button>
            </div>
          </div>
        </section>
      )}

      {allowed && error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}

      {allowed && (
        <section className="card" aria-labelledby="audit-title">
          <h2 id="audit-title">{data ? `${first}–${last} de ${total}` : 'Cargando…'}</h2>

          {data && data.items.length === 0 && <p>No hay registros con esos filtros.</p>}

          {data && data.items.length > 0 && (
            <div className="table-wrap">
              <table className="table">
                <thead>
                  <tr>
                    <th scope="col">Cuándo</th>
                    <th scope="col">Quién</th>
                    <th scope="col">Qué pasó</th>
                    <th scope="col">Registro</th>
                    <th scope="col">IP</th>
                    <th scope="col">Detalle</th>
                  </tr>
                </thead>
                <tbody>
                  {data.items.map((a) => (
                    <tr key={a.id}>
                      <td>{stamp(a.occurredAt)}</td>
                      <td>{a.username ?? '—'}</td>
                      <td>
                        {describeAction(a.action)}
                        <span className="hint"> {a.action}</span>
                      </td>
                      <td>
                        {a.entityType} <code>{shortId(a.entityId)}</code>
                      </td>
                      <td>{a.ipAddress ?? '—'}</td>
                      <td>
                        {a.metadata ? (
                          <details>
                            <summary>Ver</summary>
                            <pre className="audit-json">{JSON.stringify(a.metadata, null, 2)}</pre>
                          </details>
                        ) : (
                          '—'
                        )}
                      </td>
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
            <button type="button" className="secondary" onClick={() => setReloadKey((k) => k + 1)}>
              Actualizar
            </button>
          </div>
        </section>
      )}
    </main>
  );
}