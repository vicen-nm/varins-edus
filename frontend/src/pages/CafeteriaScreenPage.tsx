import { useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router';
import { QRCodeSVG } from 'qrcode.react';
import { apiFetch } from '../api/client';
import { errorMessage } from '../api/errors';
import type { AccessPoint, AccessQr } from '../api/types';
import { useAuth } from '../auth/useAuth';
import { useRefreshingValue } from '../hooks/useRefreshingValue';

// The token lives 30 s on the server: renewing every 10 s always leaves time to scan it.
const REFRESH_MS = 10_000;

export function CafeteriaScreenPage() {
  const { user } = useAuth();

  // Hiding things is only a convenience: the server enforces this permission.
  const allowed = Boolean(!user?.isPlatform && user?.permissions.includes('access.scan'));

  const [points, setPoints] = useState<AccessPoint[]>([]);
  const [loaded, setLoaded] = useState(false);
  const [pointId, setPointId] = useState('');
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!allowed) {
      return;
    }

    let cancelled = false;

    apiFetch<AccessPoint[]>('/api/access-points')
      .then((result) => {
        if (cancelled) {
          return;
        }

        const active = result.filter((p) => p.isActive);
        setPoints(active);
        setPointId((current) => current || (active[0]?.id ?? ''));
        setLoaded(true);
      })
      .catch((e) => {
        if (!cancelled) {
          setError(errorMessage(e));
        }
      });

    return () => {
      cancelled = true;
    };
  }, [allowed]);

  // A new function per point: the hook starts over (and drops the old QR) when the point changes.
  const loadQr = useMemo(
    () => (pointId ? () => apiFetch<AccessQr>(`/api/access-points/${pointId}/qr`) : null),
    [pointId],
  );

  const { value: qrData, error: qrError } = useRefreshingValue(loadQr, REFRESH_MS);

  const qr = qrData?.token ?? null;
  const shownError = error ?? (qrError !== null ? errorMessage(qrError) : null);
  const point = points.find((p) => p.id === pointId) ?? null;

  return (
    <main className="page">
      <header className="page__header">
        <div>
          <p className="eyebrow">VARINS · EDU</p>
          <h1>Pantalla del comedor</h1>
        </div>
        <Link to="/" className="link">
          Volver al inicio
        </Link>
      </header>

      {!allowed && (
        <p role="alert" className="error">
          No tienes permiso para mostrar la pantalla del comedor.
        </p>
      )}

      {allowed && loaded && points.length === 0 && (
        <p role="alert" className="error">
          No hay puntos de acceso activos. Pide que configuren uno.
        </p>
      )}

      {allowed && points.length > 1 && (
        <section className="card">
          <label htmlFor="screen-point">Punto</label>
          <select id="screen-point" value={pointId} onChange={(e) => setPointId(e.target.value)}>
            {points.map((p) => (
              <option key={p.id} value={p.id}>
                {p.name}
              </option>
            ))}
          </select>
        </section>
      )}

      {allowed && point && point.latitude === null && (
        <p role="alert" className="error">
          Este punto no tiene ubicación configurada: los estudiantes no podrán registrarse.
        </p>
      )}

      {allowed && shownError && (
        <p role="alert" className="error">
          {shownError}
        </p>
      )}

      {allowed && point && (
        <section className="card qr-screen" aria-labelledby="screen-title">
          <h2 id="screen-title">{point.name}</h2>
          <p>Escanea el código con tu teléfono para registrar tu comida.</p>

          {qr ? (
            <div className="qr-box" role="img" aria-label="Código QR del comedor. Cambia cada pocos segundos.">
              <QRCodeSVG value={qr} size={256} level="M" bgColor="#ffffff" fgColor="#000000" />
            </div>
          ) : (
            !shownError && <p>Cargando…</p>
          )}

          <p className="hint">El código cambia solo cada pocos segundos.</p>

          {import.meta.env.DEV && qr && (
            <details>
              <summary>Texto del QR (solo desarrollo)</summary>
              <code className="token-text">{qr}</code>
            </details>
          )}
        </section>
      )}
    </main>
  );
}