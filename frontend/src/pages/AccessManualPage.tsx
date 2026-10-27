import { useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { Link } from 'react-router';
import { apiFetch } from '../api/client';
import { errorMessage } from '../api/errors';
import type { AccessPoint, RegistrationResult } from '../api/types';
import { useAuth } from '../auth/useAuth';

export function AccessManualPage() {
  const { user } = useAuth();

  // Hiding things is only a convenience: the server enforces this permission.
  const allowed = Boolean(!user?.isPlatform && user?.permissions.includes('access.scan'));

  const [points, setPoints] = useState<AccessPoint[]>([]);
  const [loaded, setLoaded] = useState(false);
  const [pointId, setPointId] = useState('');
  const [identification, setIdentification] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState<RegistrationResult | null>(null);

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

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);
    setDone(null);
    setBusy(true);

    try {
      const result = await apiFetch<RegistrationResult>('/api/access/manual', {
        method: 'POST',
        body: JSON.stringify({ pointId, identificationNumber: identification }),
      });

      setDone(result);
      setIdentification('');
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <main className="page">
      <header className="page__header">
        <div>
          <p className="eyebrow">VARINS · EDU</p>
          <h1>Registro manual</h1>
        </div>
        <Link to="/" className="link">
          Volver al inicio
        </Link>
      </header>

      {!allowed && (
        <p role="alert" className="error">
          No tienes permiso para registrar accesos.
        </p>
      )}

      {allowed && loaded && points.length === 0 && (
        <p role="alert" className="error">
          No hay puntos de acceso activos. Pide que configuren uno.
        </p>
      )}

      {allowed && points.length > 0 && (
        <form className="card login__form" onSubmit={handleSubmit} noValidate>
          <p className="hint">Para estudiantes sin teléfono o cuando falla la ubicación.</p>

          <label htmlFor="manual-point">Punto</label>
          <select id="manual-point" value={pointId} onChange={(e) => setPointId(e.target.value)}>
            {points.map((p) => (
              <option key={p.id} value={p.id}>
                {p.name}
              </option>
            ))}
          </select>

          <label htmlFor="manual-id">Cédula del estudiante</label>
          <input
            id="manual-id"
            autoComplete="off"
            inputMode="numeric"
            value={identification}
            onChange={(e) => setIdentification(e.target.value)}
          />

          {error && (
            <p role="alert" className="error">
              {error}
            </p>
          )}

          {done && (
            <p role="status" className="success">
              {done.firstName} {done.lastName} quedó registrado en {done.serviceName} ({done.pointName}).
            </p>
          )}

          <button type="submit" disabled={busy || !pointId || identification.trim() === ''}>
            {busy ? 'Registrando…' : 'Registrar'}
          </button>
        </form>
      )}
    </main>
  );
}