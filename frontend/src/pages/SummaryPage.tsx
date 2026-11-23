import { useEffect, useState } from 'react';
import type { ReactNode } from 'react';
import { Link } from 'react-router';
import { apiFetch } from '../api/client';
import { errorMessage } from '../api/errors';
import type { DailySummary } from '../api/types';
import { useAuth } from '../auth/useAuth';

// The panel stays open on a screen: the numbers refresh by themselves.
const REFRESH_MS = 30_000;

function Stat({ label, value, alert = false }: { label: string; value: number; alert?: boolean }) {
  return (
    <div className={alert && value > 0 ? 'stat stat--alert' : 'stat'}>
      <strong>{value}</strong>
      <span>{label}</span>
    </div>
  );
}

function Block({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="card" aria-label={title}>
      <h2>{title}</h2>
      <div className="stat-grid">{children}</div>
    </section>
  );
}

export function SummaryPage() {
  const { user } = useAuth();

  const [data, setData] = useState<DailySummary | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState(0);

  const canViewAudit = Boolean(!user?.isPlatform && user?.permissions.includes('audit.view'));

  useEffect(() => {
    const timer = window.setInterval(() => {
      if (!document.hidden) {
        setReloadKey((k) => k + 1);
      }
    }, REFRESH_MS);

    return () => window.clearInterval(timer);
  }, []);

  useEffect(() => {
    let cancelled = false;

    apiFetch<DailySummary>('/api/summary/today')
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
  }, [reloadKey]);

  const empty =
    data !== null &&
    !data.access &&
    !data.parking &&
    !data.visits &&
    !data.exits &&
    !data.vehicles &&
    !data.students &&
    !data.security;

  const maxService = Math.max(1, ...(data?.access?.byService.map((s) => s.total) ?? [0]));

  return (
    <main className="page">
      <header className="page__header">
        <div>
          <p className="eyebrow">VARINS · EDU</p>
          <h1>Resumen del día</h1>
          {data && <p>{new Date(`${data.date}T00:00:00`).toLocaleDateString('es-CR', { dateStyle: 'full' })}</p>}
        </div>
        <Link to="/" className="link">
          Volver al inicio
        </Link>
      </header>

      {error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}

      {empty && <p>No hay información para tu rol en este panel.</p>}

      {data?.access && (
        <section className="card" aria-label="Comedor">
          <h2>Comedor hoy</h2>
          <div className="stat-grid">
            <Stat label="Registros" value={data.access.total} />
            <Stat label="Por QR" value={data.access.qr} />
            <Stat label="Manuales" value={data.access.manual} />
          </div>
          {data.access.byService.map((s) => (
            <div key={s.service} className="bar-row">
              <span>{s.service}</span>
              <div className="bar">
                <div className="bar__fill" style={{ width: `${(s.total / maxService) * 100}%` }} />
              </div>
              <strong>{s.total}</strong>
            </div>
          ))}
        </section>
      )}

      {data?.parking && (
        <Block title="Parqueo">
          <Stat label="Motos dentro ahora" value={data.parking.inside} />
          <Stat label="Ingresos hoy" value={data.parking.entriesToday} />
          <Stat label="Salidas hoy" value={data.parking.exitsToday} />
          <Stat label="Sin salida (30 días)" value={data.parking.withoutExit} />
        </Block>
      )}

      {data?.visits && (
        <Block title="Visitas hoy">
          <Stat label="Solicitudes" value={data.visits.total} />
          <Stat label="Pendientes" value={data.visits.pending} />
          <Stat label="Dentro ahora" value={data.visits.inside} />
        </Block>
      )}

      {data?.exits && (
        <Block title="Permisos de salida hoy">
          <Stat label="Permisos" value={data.exits.total} />
          <Stat label="Vigentes ahora" value={data.exits.valid} />
          <Stat label="Usados" value={data.exits.used} />
        </Block>
      )}

      {data?.vehicles && (
        <Block title="Vehículos">
          <Stat label="Pendientes de revisión" value={data.vehicles.pending} />
          <Stat label="Aprobados" value={data.vehicles.approved} />
          <Stat label="Suspendidos" value={data.vehicles.suspended} />
        </Block>
      )}

      {data?.students && (
        <Block title="Estudiantes">
          <Stat label="Activos" value={data.students.active} />
          <Stat label="Con cuenta" value={data.students.withAccount} />
          <Stat label="Sin cuenta" value={data.students.withoutAccount} />
        </Block>
      )}

      {data?.security && (
        <section className="card" aria-label="Seguridad">
          <h2>Seguridad hoy</h2>
          <div className="stat-grid">
            <Stat label="Intentos de acceso fallidos" value={data.security.failedLogins} alert />
            <Stat label="Cuentas bloqueadas" value={data.security.lockedAccounts} alert />
            <Stat label="Carnés reutilizados" value={data.security.cardReuse} alert />
            <Stat label="Sesiones cerradas por reuso" value={data.security.sessionReuse} alert />
          </div>
          <p className="hint">Cuenta solo intentos contra cuentas de este colegio.</p>
          {canViewAudit && (
            <p>
              <Link to="/audit" className="link">
                Ver la bitácora
              </Link>
            </p>
          )}
        </section>
      )}
    </main>
  );
}