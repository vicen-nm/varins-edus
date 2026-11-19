import { useEffect, useState } from 'react';
import { Link } from 'react-router';
import { QRCodeSVG } from 'qrcode.react';
import { apiDownload, apiFetch } from '../api/client';
import { errorMessage } from '../api/errors';
import type { VisitItem, VisitPage } from '../api/types';
import { useAuth } from '../auth/useAuth';
import { useInstitution } from '../institution/useInstitution';
import { statusLabel } from '../visits/visitRules';

const PAGE_SIZE = 25;

// The guard keeps this page open: new requests appear by themselves.
const REFRESH_MS = 15_000;

const STATUS_FILTERS = [
  { value: '', label: 'Todas' },
  { value: 'pending', label: 'Pendientes' },
  { value: 'inside', label: 'Dentro' },
  { value: 'exited', label: 'Salieron' },
  { value: 'rejected', label: 'Rechazadas' },
  { value: 'expired', label: 'Vencidas' },
  { value: 'no_exit', label: 'Sin salida' },
];

const clock = (iso: string) => new Date(iso).toLocaleTimeString('es-CR', { hour: '2-digit', minute: '2-digit' });

export function VisitsPage() {
  const { user } = useAuth();
  const { institution } = useInstitution();

  // Hiding things is only a convenience: the server enforces these permissions.
  const canView = Boolean(!user?.isPlatform && user?.permissions.includes('visits.view'));
  const canManage = Boolean(!user?.isPlatform && user?.permissions.includes('visits.manage'));

  const [status, setStatus] = useState('');
  // Empty means "today": the server knows the school's time zone.
  const [date, setDate] = useState('');
  const [search, setSearch] = useState('');
  const [debounced, setDebounced] = useState('');
  const [skip, setSkip] = useState(0);
  const [reloadKey, setReloadKey] = useState(0);

  const [data, setData] = useState<VisitPage | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [signature, setSignature] = useState<{ name: string; url: string } | null>(null);

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

    const timer = window.setInterval(() => {
      if (!document.hidden) {
        setReloadKey((k) => k + 1);
      }
    }, REFRESH_MS);

    return () => window.clearInterval(timer);
  }, [canView]);

  useEffect(() => {
    if (!canView) {
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

    apiFetch<VisitPage>(`/api/visits?${params.toString()}`)
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
  }, [canView, status, date, debounced, skip, reloadKey]);

  // The object URL of the signature is released when it is replaced or the page closes.
  useEffect(() => {
    return () => {
      if (signature) {
        URL.revokeObjectURL(signature.url);
      }
    };
  }, [signature]);

  async function act(visit: VisitItem, action: 'admit' | 'reject' | 'exit') {
    setBusyId(visit.id);
    setError(null);

    try {
      await apiFetch<void>(`/api/visits/${visit.id}/${action}`, { method: 'PUT' });
      setReloadKey((k) => k + 1);
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setBusyId(null);
    }
  }

  async function showSignature(visit: VisitItem) {
    setError(null);

    try {
      const blob = await apiDownload(`/api/visits/${visit.id}/signature`);

      setSignature({ name: visit.fullName, url: URL.createObjectURL(blob) });
    } catch (e) {
      setError(errorMessage(e));
    }
  }

  const link = institution ? `${window.location.origin}/visitas/${institution.slug}` : null;
  const total = data?.total ?? 0;
  const from = total === 0 ? 0 : skip + 1;
  const to = Math.min(skip + PAGE_SIZE, total);

  return (
    <main className="page">
      <header className="page__header">
        <div>
          <p className="eyebrow">VARINS · EDU</p>
          <h1>Visitas</h1>
        </div>
        <Link to="/" className="link">
          Volver al inicio
        </Link>
      </header>

      {!canView && (
        <p role="alert" className="error">
          No tienes permiso para ver las visitas.
        </p>
      )}

      {canView && error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}

      {canView && link && (
        <section className="card print-area" aria-labelledby="visit-qr-title">
          <h2 id="visit-qr-title">Código QR para los visitantes</h2>
          <p>Escanea el código para registrar tu visita.</p>
          <div className="qr-box" role="img" aria-label="Código QR del formulario de visitas">
            <QRCodeSVG value={link} size={200} level="M" bgColor="#ffffff" fgColor="#000000" />
          </div>
          <p className="hint">
            Enlace: <code>{link}</code>
          </p>
          <div className="actions no-print">
            <button type="button" className="secondary" onClick={() => window.print()}>
              Imprimir
            </button>
          </div>
        </section>
      )}

      {canView && signature && (
        <section className="card" aria-labelledby="signature-title">
          <h2 id="signature-title">Firma de {signature.name}</h2>
          <img src={signature.url} alt={`Firma de ${signature.name}`} className="signature-image" />
          <div className="actions">
            <button type="button" className="secondary" onClick={() => setSignature(null)}>
              Cerrar
            </button>
          </div>
        </section>
      )}

      {canView && (
        <section className="card filters" aria-label="Filtros">
          <div>
            <label htmlFor="visits-status">Estado</label>
            <select
              id="visits-status"
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
            <label htmlFor="visits-date">Fecha</label>
            <input
              id="visits-date"
              type="date"
              value={date}
              onChange={(e) => {
                setDate(e.target.value);
                setSkip(0);
              }}
            />
            <p className="hint">Sin fecha: hoy.</p>
          </div>
          <div>
            <label htmlFor="visits-search">Buscar</label>
            <input
              id="visits-search"
              autoComplete="off"
              placeholder="Nombre o identificación"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
          </div>
        </section>
      )}

      {canView && (
        <section className="card" aria-labelledby="visits-title">
          <h2 id="visits-title">{data ? `${from}–${to} de ${total}` : 'Cargando…'}</h2>

          {data && (
            <p>
              Pendientes hoy: <strong>{data.pending}</strong>. Dentro ahora: <strong>{data.inside}</strong>.
            </p>
          )}

          {data && data.items.length === 0 && <p>No hay visitas con esos filtros.</p>}

          {data && data.items.length > 0 && (
            <div className="table-wrap">
              <table className="table">
                <thead>
                  <tr>
                    <th scope="col">Visitante</th>
                    <th scope="col">Identificación</th>
                    <th scope="col">Visita a</th>
                    <th scope="col">Motivo</th>
                    <th scope="col">Estado</th>
                    <th scope="col">Acciones</th>
                  </tr>
                </thead>
                <tbody>
                  {data.items.map((v) => (
                    <tr key={v.id}>
                      <td>
                        {v.fullName}
                        {v.phone && <span className="hint"> {v.phone}</span>}
                      </td>
                      <td>
                        <code>{v.identificationNumber}</code>
                      </td>
                      <td>{v.host}</td>
                      <td>{v.reason}</td>
                      <td>
                        {statusLabel(v.status)}
                        <span className="hint">
                          {' '}
                          Llegó {clock(v.createdAt)}
                          {v.enteredAt && ` · entró ${clock(v.enteredAt)} (${v.handledBy})`}
                          {v.exitedAt && ` · salió ${clock(v.exitedAt)}`}
                        </span>
                      </td>
                      <td>
                        <div className="actions">
                          <button type="button" className="secondary" onClick={() => showSignature(v)}>
                            Firma
                          </button>
                          {canManage && v.status === 'pending' && (
                            <>
                              <button type="button" disabled={busyId === v.id} onClick={() => act(v, 'admit')}>
                                Admitir
                              </button>
                              <button
                                type="button"
                                className="secondary"
                                disabled={busyId === v.id}
                                onClick={() => act(v, 'reject')}
                              >
                                Rechazar
                              </button>
                            </>
                          )}
                          {canManage && v.status === 'inside' && (
                            <button type="button" disabled={busyId === v.id} onClick={() => act(v, 'exit')}>
                              Registrar salida
                            </button>
                          )}
                        </div>
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