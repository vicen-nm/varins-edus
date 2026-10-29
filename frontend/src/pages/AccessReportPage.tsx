import { useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { Link } from 'react-router';
import { apiDownload, apiFetch } from '../api/client';
import { errorMessage } from '../api/errors';
import type { AccessReport } from '../api/types';
import { useAuth } from '../auth/useAuth';

type Range = { from: string; to: string };

function buildQuery(range: Range): string {
  const params = new URLSearchParams();

  if (range.from) {
    params.set('from', range.from);
  }
  if (range.to) {
    params.set('to', range.to);
  }

  const text = params.toString();
  return text ? `?${text}` : '';
}

export function AccessReportPage() {
  const { user } = useAuth();

  // Hiding things is only a convenience: the server enforces this permission.
  const allowed = Boolean(!user?.isPlatform && user?.permissions.includes('access.view'));

  // Empty dates mean "the last 7 days": the server knows the school's time zone.
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [applied, setApplied] = useState<Range>({ from: '', to: '' });
  const [data, setData] = useState<AccessReport | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [downloading, setDownloading] = useState(false);

  useEffect(() => {
    if (!allowed) {
      return;
    }

    let cancelled = false;

    apiFetch<AccessReport>(`/api/access/report${buildQuery(applied)}`)
      .then((result) => {
        if (cancelled) {
          return;
        }

        setData(result);
        setError(null);

        // Show the range the server really used.
        setFrom((current) => current || result.from);
        setTo((current) => current || result.to);
      })
      .catch((e) => {
        if (!cancelled) {
          setData(null);
          setError(errorMessage(e));
        }
      });

    return () => {
      cancelled = true;
    };
  }, [allowed, applied]);

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setApplied({ from, to });
  }

  async function handleDownload() {
    if (!data) {
      return;
    }

    setDownloading(true);
    setError(null);

    try {
      const blob = await apiDownload(`/api/access/report/export${buildQuery({ from: data.from, to: data.to })}`);
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');

      link.href = url;
      link.download = `comedor-${data.from}-${data.to}.xlsx`;
      link.click();
      URL.revokeObjectURL(url);
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setDownloading(false);
    }
  }

  return (
    <main className="page">
      <header className="page__header">
        <div>
          <p className="eyebrow">VARINS · EDU</p>
          <h1>Reporte del comedor</h1>
        </div>
        <Link to="/" className="link">
          Volver al inicio
        </Link>
      </header>

      {!allowed && (
        <p role="alert" className="error">
          No tienes permiso para ver el reporte del comedor.
        </p>
      )}

      {allowed && (
        <form className="card filters" onSubmit={handleSubmit} noValidate aria-label="Rango de fechas">
          <div>
            <label htmlFor="report-from">Desde</label>
            <input id="report-from" type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
          </div>
          <div>
            <label htmlFor="report-to">Hasta</label>
            <input id="report-to" type="date" value={to} onChange={(e) => setTo(e.target.value)} />
          </div>
          <div>
            <p className="hint">Máximo 92 días.</p>
            <div className="actions">
              <button type="submit">Consultar</button>
            </div>
          </div>
        </form>
      )}

      {allowed && error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}

      {allowed && data && (
        <>
          <section className="card" aria-labelledby="totals-title">
            <h2 id="totals-title">
              Del {data.from} al {data.to}
            </h2>
            <p>
              Total: <strong>{data.totals.total}</strong>. Por QR: {data.totals.qr}. Manual: {data.totals.manual}.
            </p>

            <p className="hint">
              El archivo de Excel incluye la cédula de cada estudiante. Guárdalo en un lugar seguro y bórralo cuando
              ya no lo necesites. Cada descarga queda registrada.
            </p>

            <div className="actions">
              <button type="button" disabled={downloading || data.totals.total === 0} onClick={handleDownload}>
                {downloading ? 'Preparando…' : 'Descargar Excel'}
              </button>
            </div>
          </section>

          <section className="card" aria-labelledby="byday-title">
            <h2 id="byday-title">Por día y servicio</h2>

            {data.byDay.length === 0 && <p>No hay registros en este rango.</p>}

            {data.byDay.length > 0 && (
              <div className="table-wrap">
                <table className="table">
                  <thead>
                    <tr>
                      <th scope="col">Fecha</th>
                      <th scope="col">Punto</th>
                      <th scope="col">Servicio</th>
                      <th scope="col">Total</th>
                      <th scope="col">QR</th>
                      <th scope="col">Manual</th>
                    </tr>
                  </thead>
                  <tbody>
                    {data.byDay.map((d) => (
                      <tr key={`${d.date}-${d.point}-${d.service}`}>
                        <td>{d.date}</td>
                        <td>{d.point}</td>
                        <td>{d.service}</td>
                        <td>{d.total}</td>
                        <td>{d.qr}</td>
                        <td>{d.manual}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>

          <section className="card" aria-labelledby="bygroup-title">
            <h2 id="bygroup-title">Por grupo</h2>

            {data.byGroup.length === 0 && <p>No hay registros en este rango.</p>}

            {data.byGroup.length > 0 && (
              <div className="table-wrap">
                <table className="table">
                  <thead>
                    <tr>
                      <th scope="col">Grupo</th>
                      <th scope="col">Total</th>
                    </tr>
                  </thead>
                  <tbody>
                    {data.byGroup.map((g) => (
                      <tr key={g.group ?? 'none'}>
                        <td>{g.group ?? 'Sin grupo'}</td>
                        <td>{g.total}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>
        </>
      )}
    </main>
  );
}