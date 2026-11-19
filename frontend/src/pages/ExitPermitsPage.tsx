import { useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { Link } from 'react-router';
import { apiFetch } from '../api/client';
import { errorMessage } from '../api/errors';
import type { ExitPermitItem, ExitPermitPage } from '../api/types';
import { useAuth } from '../auth/useAuth';
import { formatWindow, statusLabel, toApiTime, todayIso } from '../exits/exitRules';

const PAGE_SIZE = 25;

const STATUS_FILTERS = [
  { value: '', label: 'Todos' },
  { value: 'active', label: 'Vigentes' },
  { value: 'used', label: 'Usados' },
  { value: 'cancelled', label: 'Cancelados' },
  { value: 'expired', label: 'Vencidos' },
];

const clock = (iso: string) => new Date(iso).toLocaleTimeString('es-CR', { hour: '2-digit', minute: '2-digit' });
const dayLabel = (isoDay: string) => new Date(`${isoDay}T00:00:00`).toLocaleDateString('es-CR');

export function ExitPermitsPage() {
  const { user } = useAuth();

  // Hiding things is only a convenience: the server enforces this permission.
  const allowed = Boolean(!user?.isPlatform && user?.permissions.includes('exits.manage'));

  const [status, setStatus] = useState('');
  // Empty means "today and the following days".
  const [date, setDate] = useState('');
  const [search, setSearch] = useState('');
  const [debounced, setDebounced] = useState('');
  const [skip, setSkip] = useState(0);
  const [reloadKey, setReloadKey] = useState(0);

  const [data, setData] = useState<ExitPermitPage | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);

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
    if (status) {
      params.set('status', status);
    }
    if (date) {
      params.set('date', date);
    }
    if (debounced) {
      params.set('search', debounced);
    }

    apiFetch<ExitPermitPage>(`/api/exit-permits?${params.toString()}`)
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
  }, [allowed, status, date, debounced, skip, reloadKey]);

  async function handleCancel(permit: ExitPermitItem) {
    if (!window.confirm(`¿Cancelar el permiso de ${permit.firstName} ${permit.lastName}?`)) {
      return;
    }

    setBusyId(permit.id);
    setError(null);
    setMessage(null);

    try {
      await apiFetch<void>(`/api/exit-permits/${permit.id}/cancel`, { method: 'PUT' });
      setReloadKey((k) => k + 1);
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setBusyId(null);
    }
  }

  const total = data?.total ?? 0;
  const from = total === 0 ? 0 : skip + 1;
  const to = Math.min(skip + PAGE_SIZE, total);

  return (
    <main className="page">
      <header className="page__header">
        <div>
          <p className="eyebrow">VARINS · EDU</p>
          <h1>Permisos de salida</h1>
        </div>
        <Link to="/" className="link">
          Volver al inicio
        </Link>
      </header>

      {!allowed && (
        <p role="alert" className="error">
          No tienes permiso para administrar los permisos de salida.
        </p>
      )}

      {allowed && message && (
        <p role="status" className="success">
          {message}
        </p>
      )}

      {allowed && error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}

      {allowed && creating && (
        <NewPermitForm
          onCreated={(text) => {
            setCreating(false);
            setMessage(text);
            setReloadKey((k) => k + 1);
          }}
          onCancel={() => setCreating(false)}
        />
      )}

      {allowed && (
        <section className="card filters" aria-label="Filtros">
          <div>
            <label htmlFor="exits-status">Estado</label>
            <select
              id="exits-status"
              value={status}
              onChange={(e) => {
                setStatus(e.target.value);
                setSkip(0);
              }}
            >
              {STATUS_FILTERS.map((f) => (
                <option key={f.value} value={f.value}>
                  {f.label}
                </option>
              ))}
            </select>
          </div>
          <div>
            <label htmlFor="exits-date">Fecha</label>
            <input
              id="exits-date"
              type="date"
              value={date}
              onChange={(e) => {
                setDate(e.target.value);
                setSkip(0);
              }}
            />
            <p className="hint">Sin fecha: hoy y los días siguientes.</p>
          </div>
          <div>
            <label htmlFor="exits-search">Buscar</label>
            <input
              id="exits-search"
              autoComplete="off"
              placeholder="Nombre"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
            {!creating && (
              <div className="actions">
                <button type="button" onClick={() => setCreating(true)}>
                  Nuevo permiso
                </button>
              </div>
            )}
          </div>
        </section>
      )}

      {allowed && (
        <section className="card" aria-labelledby="exits-title">
          <h2 id="exits-title">{data ? `${from}–${to} de ${total}` : 'Cargando…'}</h2>

          {data && data.items.length === 0 && <p>No hay permisos con esos filtros.</p>}

          {data && data.items.length > 0 && (
            <div className="table-wrap">
              <table className="table">
                <thead>
                  <tr>
                    <th scope="col">Estudiante</th>
                    <th scope="col">Fecha</th>
                    <th scope="col">Horario</th>
                    <th scope="col">Motivo</th>
                    <th scope="col">Estado</th>
                    <th scope="col">Creado por</th>
                    <th scope="col">Acción</th>
                  </tr>
                </thead>
                <tbody>
                  {data.items.map((p) => (
                    <tr key={p.id}>
                      <td>
                        {p.firstName} {p.lastName}
                        <span className="hint"> {p.group ?? 'Sin grupo'}</span>
                      </td>
                      <td>{dayLabel(p.date)}</td>
                      <td>{formatWindow(p.fromTime, p.toTime)}</td>
                      <td>{p.reason}</td>
                      <td>
                        {statusLabel(p.status)}
                        {p.status === 'used' && p.usedAt && (
                          <span className="hint">
                            {' '}
                            {clock(p.usedAt)} · {p.usedBy}
                          </span>
                        )}
                      </td>
                      <td>{p.createdBy ?? '—'}</td>
                      <td>
                        {p.status === 'active' && (
                          <button
                            type="button"
                            className="secondary"
                            disabled={busyId === p.id}
                            onClick={() => handleCancel(p)}
                          >
                            {busyId === p.id ? 'Cancelando…' : 'Cancelar'}
                          </button>
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
          </div>
        </section>
      )}
    </main>
  );
}

function NewPermitForm({ onCreated, onCancel }: { onCreated: (message: string) => void; onCancel: () => void }) {
  const [identification, setIdentification] = useState('');
  const [date, setDate] = useState(todayIso());
  const [start, setStart] = useState('10:00');
  const [end, setEnd] = useState('11:00');
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const ready = identification.trim() !== '' && date !== '' && start !== '' && end !== '' && start < end && reason.trim() !== '';

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);
    setBusy(true);

    try {
      await apiFetch<{ id: string }>('/api/exit-permits', {
        method: 'POST',
        body: JSON.stringify({
          identificationNumber: identification,
          date,
          fromTime: toApiTime(start),
          toTime: toApiTime(end),
          reason,
        }),
      });

      onCreated('Permiso creado.');
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="card" onSubmit={handleSubmit} noValidate aria-labelledby="new-permit-title">
      <h2 id="new-permit-title">Nuevo permiso de salida</h2>

      <label htmlFor="permit-identification">Cédula del estudiante</label>
      <input
        id="permit-identification"
        autoComplete="off"
        inputMode="numeric"
        value={identification}
        onChange={(e) => setIdentification(e.target.value)}
      />

      <label htmlFor="permit-date">Fecha</label>
      <input id="permit-date" type="date" value={date} onChange={(e) => setDate(e.target.value)} />

      <div className="filters">
        <div>
          <label htmlFor="permit-start">Desde</label>
          <input id="permit-start" type="time" value={start} onChange={(e) => setStart(e.target.value)} />
        </div>
        <div>
          <label htmlFor="permit-end">Hasta</label>
          <input id="permit-end" type="time" value={end} onChange={(e) => setEnd(e.target.value)} />
        </div>
      </div>

      <label htmlFor="permit-reason">Motivo</label>
      <input
        id="permit-reason"
        autoComplete="off"
        maxLength={200}
        value={reason}
        onChange={(e) => setReason(e.target.value)}
      />
      <p className="hint">
        El guardia no verá el motivo: solo que hay un permiso vigente y su horario. Escribe lo mínimo necesario.
      </p>

      {error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}

      <div className="actions">
        <button type="submit" disabled={busy || !ready}>
          {busy ? 'Creando…' : 'Crear permiso'}
        </button>
        <button type="button" className="secondary" onClick={onCancel}>
          Cancelar
        </button>
      </div>
    </form>
  );
}