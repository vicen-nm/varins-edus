import { useEffect, useState } from 'react';
import { Link } from 'react-router';
import { apiFetch } from '../api/client';
import { errorMessage } from '../api/errors';
import type { AccessEventItem, AccessEventPage } from '../api/types';
import { useAuth } from '../auth/useAuth';

const PAGE_SIZE = 25;

const METHOD_LABELS: Record<AccessEventItem['method'], string> = {
  qr: 'QR',
  manual: 'Manual',
};

export function AccessEventsPage() {
  const { user } = useAuth();

  // Hiding things is only a convenience: the server enforces this permission.
  const allowed = Boolean(!user?.isPlatform && user?.permissions.includes('access.view'));

  // Empty means "today at the school": the server knows the school's time zone.
  const [date, setDate] = useState('');
  const [skip, setSkip] = useState(0);
  const [reloadKey, setReloadKey] = useState(0);
  const [data, setData] = useState<AccessEventPage | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!allowed) {
      return;
    }

    let cancelled = false;

    const params = new URLSearchParams({ take: String(PAGE_SIZE), skip: String(skip) });
    if (date) {
      params.set('date', date);
    }

    apiFetch<AccessEventPage>(`/api/access/events?${params.toString()}`)
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
  }, [allowed, date, skip, reloadKey]);

  const total = data?.total ?? 0;
  const from = total === 0 ? 0 : skip + 1;
  const to = Math.min(skip + PAGE_SIZE, total);

  return (
    <main className="page">
      <header className="page__header">
        <div>
          <p className="eyebrow">VARINS · EDU</p>
          <h1>Registros de acceso</h1>
        </div>
        <Link to="/" className="link">
          Volver al inicio
        </Link>
      </header>

      {!allowed && (
        <p role="alert" className="error">
          No tienes permiso para ver los registros de acceso.
        </p>
      )}

      {allowed && (
        <section className="card filters" aria-label="Filtros">
          <div>
            <label htmlFor="events-date">Fecha</label>
            <input
              id="events-date"
              type="date"
              value={date}
              onChange={(e) => {
                setDate(e.target.value);
                setSkip(0);
              }}
            />
            <p className="hint">Si no eliges una fecha, se muestra hoy.</p>
          </div>
        </section>
      )}

      {allowed && error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}

      {allowed && (
        <section className="card" aria-labelledby="events-title">
          <h2 id="events-title">{data ? `${from}–${to} de ${total}` : 'Cargando…'}</h2>

          {data && data.items.length === 0 && <p>No hay registros para esta fecha.</p>}

          {data && data.items.length > 0 && (
            <div className="table-wrap">
              <table className="table">
                <thead>
                  <tr>
                    <th scope="col">Hora</th>
                    <th scope="col">Estudiante</th>
                    <th scope="col">Punto</th>
                    <th scope="col">Servicio</th>
                    <th scope="col">Método</th>
                  </tr>
                </thead>
                <tbody>
                  {data.items.map((e) => (
                    <tr key={e.id}>
                      <td>{new Date(e.occurredAt).toLocaleTimeString('es-CR')}</td>
                      <td>
                        {e.firstName} {e.lastName}
                      </td>
                      <td>{e.pointName}</td>
                      <td>{e.serviceName}</td>
                      <td>{METHOD_LABELS[e.method]}</td>
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