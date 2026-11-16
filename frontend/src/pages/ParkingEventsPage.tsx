import { useEffect, useState } from 'react';
import { Link } from 'react-router';
import { apiFetch } from '../api/client';
import { errorMessage } from '../api/errors';
import type { ParkingEventPage } from '../api/types';
import { useAuth } from '../auth/useAuth';
import { statusLabel } from '../parking/parkingRules';

const PAGE_SIZE = 25;

const clock = (iso: string) => new Date(iso).toLocaleTimeString('es-CR', { hour: '2-digit', minute: '2-digit' });

export function ParkingEventsPage() {
  const { user } = useAuth();

  // Hiding things is only a convenience: the server enforces this permission.
  const allowed = Boolean(!user?.isPlatform && user?.permissions.includes('parking.view'));

  // Empty means "today at the school": the server knows the school's time zone.
  const [date, setDate] = useState('');
  const [skip, setSkip] = useState(0);
  const [reloadKey, setReloadKey] = useState(0);
  const [data, setData] = useState<ParkingEventPage | null>(null);
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

    apiFetch<ParkingEventPage>(`/api/parking/events?${params.toString()}`)
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
          <h1>Movimientos del parqueo</h1>
        </div>
        <Link to="/" className="link">
          Volver al inicio
        </Link>
      </header>

      {!allowed && (
        <p role="alert" className="error">
          No tienes permiso para ver los movimientos del parqueo.
        </p>
      )}

      {allowed && (
        <section className="card filters" aria-label="Filtros">
          <div>
            <label htmlFor="parking-date">Fecha</label>
            <input
              id="parking-date"
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
        <section className="card" aria-labelledby="parking-title">
          <h2 id="parking-title">{data ? `${from}–${to} de ${total}` : 'Cargando…'}</h2>

          {data && <p>Dentro ahora: <strong>{data.inside}</strong></p>}

          {data && data.items.length === 0 && <p>No hay movimientos para esta fecha.</p>}

          {data && data.items.length > 0 && (
            <div className="table-wrap">
              <table className="table">
                <thead>
                  <tr>
                    <th scope="col">Placa</th>
                    <th scope="col">Estudiante</th>
                    <th scope="col">Entrada</th>
                    <th scope="col">Salida</th>
                    <th scope="col">Estado</th>
                  </tr>
                </thead>
                <tbody>
                  {data.items.map((e) => (
                    <tr key={e.id}>
                      <td>
                        <code>{e.plate}</code>
                      </td>
                      <td>
                        {e.firstName} {e.lastName}
                        <span className="hint"> {e.group ?? 'Sin grupo'}</span>
                      </td>
                      <td>{clock(e.enteredAt)}</td>
                      <td>{e.exitedAt ? clock(e.exitedAt) : '—'}</td>
                      <td>{statusLabel(e.status)}</td>
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