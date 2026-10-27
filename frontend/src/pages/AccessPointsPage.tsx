import { useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { Link } from 'react-router';
import { apiFetch } from '../api/client';
import { errorMessage } from '../api/errors';
import type { AccessPoint, AccessService } from '../api/types';
import { useAuth } from '../auth/useAuth';

const KIND_LABELS: Record<AccessPoint['kind'], string> = {
  cafeteria: 'Comedor',
  gate: 'Portería',
};

// <input type="time"> gives "HH:mm" and the API expects "HH:mm:ss".
const toApiTime = (value: string) => (value.length === 5 ? `${value}:00` : value);
const toInputTime = (value: string) => value.slice(0, 5);

export function AccessPointsPage() {
  const { user } = useAuth();

  // Hiding things is only a convenience: the server enforces these permissions.
  const allowed = Boolean(!user?.isPlatform && user?.permissions.includes('access.manage'));

  const [points, setPoints] = useState<AccessPoint[]>([]);
  const [loaded, setLoaded] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [editing, setEditing] = useState<AccessPoint | 'new' | null>(null);
  const [servicesOf, setServicesOf] = useState<AccessPoint | null>(null);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    if (!allowed) {
      return;
    }

    let cancelled = false;

    apiFetch<AccessPoint[]>('/api/access-points')
      .then((result) => {
        if (!cancelled) {
          setPoints(result);
          setLoaded(true);
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
  }, [allowed, reloadKey]);

  return (
    <main className="page">
      <header className="page__header">
        <div>
          <p className="eyebrow">VARINS · EDU</p>
          <h1>Puntos de acceso</h1>
        </div>
        <Link to="/" className="link">
          Volver al inicio
        </Link>
      </header>

      {!allowed && (
        <p role="alert" className="error">
          No tienes permiso para configurar los puntos de acceso.
        </p>
      )}

      {allowed && error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}

      {allowed && (
        <section className="card" aria-labelledby="points-title">
          <h2 id="points-title">Puntos</h2>

          {loaded && points.length === 0 && <p>Todavía no hay puntos. Crea el comedor para empezar.</p>}

          {points.length > 0 && (
            <div className="table-wrap">
              <table className="table">
                <thead>
                  <tr>
                    <th scope="col">Nombre</th>
                    <th scope="col">Tipo</th>
                    <th scope="col">Ubicación</th>
                    <th scope="col">Radio</th>
                    <th scope="col">Estado</th>
                    <th scope="col">Acciones</th>
                  </tr>
                </thead>
                <tbody>
                  {points.map((p) => (
                    <tr key={p.id}>
                      <td>{p.name}</td>
                      <td>{KIND_LABELS[p.kind]}</td>
                      <td>{p.latitude === null ? 'Sin ubicación' : 'Configurada'}</td>
                      <td>{p.radiusMeters} m</td>
                      <td>{p.isActive ? 'Activo' : 'Apagado'}</td>
                      <td>
                        <div className="actions">
                          <button type="button" className="secondary" onClick={() => setEditing(p)}>
                            Editar
                          </button>
                          <button type="button" className="secondary" onClick={() => setServicesOf(p)}>
                            Horarios
                          </button>
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          {editing === null && (
            <div className="actions">
              <button type="button" onClick={() => setEditing('new')}>
                Nuevo punto
              </button>
            </div>
          )}
        </section>
      )}

      {allowed && editing !== null && (
        <PointForm
          key={editing === 'new' ? 'new' : editing.id}
          point={editing === 'new' ? null : editing}
          onSaved={() => {
            setEditing(null);
            setReloadKey((k) => k + 1);
          }}
          onCancel={() => setEditing(null)}
        />
      )}

      {allowed && servicesOf && (
        <ServicesPanel key={servicesOf.id} point={servicesOf} onClose={() => setServicesOf(null)} />
      )}
    </main>
  );
}

function PointForm({
  point,
  onSaved,
  onCancel,
}: {
  point: AccessPoint | null;
  onSaved: () => void;
  onCancel: () => void;
}) {
  const [name, setName] = useState(point?.name ?? '');
  const [kind, setKind] = useState<AccessPoint['kind']>(point?.kind ?? 'cafeteria');
  const [latitude, setLatitude] = useState(point?.latitude?.toString() ?? '');
  const [longitude, setLongitude] = useState(point?.longitude?.toString() ?? '');
  const [radius, setRadius] = useState(String(point?.radiusMeters ?? 150));
  const [accuracy, setAccuracy] = useState(String(point?.maxAccuracyMeters ?? 100));
  const [active, setActive] = useState(point?.isActive ?? true);

  const [busy, setBusy] = useState(false);
  const [locating, setLocating] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [hint, setHint] = useState<string | null>(null);

  function locate() {
    if (!navigator.geolocation) {
      setError('Este navegador no permite obtener la ubicación.');
      return;
    }

    setLocating(true);
    setError(null);
    setHint(null);

    navigator.geolocation.getCurrentPosition(
      (position) => {
        setLatitude(position.coords.latitude.toFixed(6));
        setLongitude(position.coords.longitude.toFixed(6));
        setHint(`Ubicación tomada con una precisión de ${Math.round(position.coords.accuracy)} m.`);
        setLocating(false);
      },
      () => {
        setError('No se pudo obtener la ubicación. Revisa el permiso del navegador y el GPS.');
        setLocating(false);
      },
      { enableHighAccuracy: true, timeout: 15000, maximumAge: 0 },
    );
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);

    const radiusNumber = Number(radius);
    const accuracyNumber = Number(accuracy);

    if (!Number.isInteger(radiusNumber) || !Number.isInteger(accuracyNumber)) {
      setError('El radio y la precisión máxima deben ser números enteros.');
      return;
    }

    const lat = latitude.trim() === '' ? null : Number(latitude);
    const lon = longitude.trim() === '' ? null : Number(longitude);

    if (Number.isNaN(lat) || Number.isNaN(lon)) {
      setError('La latitud y la longitud deben ser números.');
      return;
    }

    setBusy(true);

    try {
      await apiFetch<AccessPoint>(point ? `/api/access-points/${point.id}` : '/api/access-points', {
        method: point ? 'PUT' : 'POST',
        body: JSON.stringify({
          name,
          kind,
          latitude: lat,
          longitude: lon,
          radiusMeters: radiusNumber,
          maxAccuracyMeters: accuracyNumber,
          isActive: active,
        }),
      });

      onSaved();
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="card" onSubmit={handleSubmit} noValidate aria-labelledby="point-form-title">
      <h2 id="point-form-title">{point ? `Editar ${point.name}` : 'Nuevo punto'}</h2>

      <label htmlFor="point-name">Nombre</label>
      <input id="point-name" maxLength={100} value={name} onChange={(e) => setName(e.target.value)} />

      <label htmlFor="point-kind">Tipo</label>
      <select
        id="point-kind"
        value={kind}
        onChange={(e) => setKind(e.target.value as AccessPoint['kind'])}
      >
        <option value="cafeteria">Comedor</option>
        <option value="gate">Portería</option>
      </select>

      <div className="filters">
        <div>
          <label htmlFor="point-lat">Latitud</label>
          <input
            id="point-lat"
            inputMode="decimal"
            value={latitude}
            onChange={(e) => setLatitude(e.target.value)}
          />
        </div>
        <div>
          <label htmlFor="point-lon">Longitud</label>
          <input
            id="point-lon"
            inputMode="decimal"
            value={longitude}
            onChange={(e) => setLongitude(e.target.value)}
          />
        </div>
      </div>

      <div className="actions">
        <button type="button" className="secondary" disabled={locating} onClick={locate}>
          {locating ? 'Buscando ubicación…' : 'Usar mi ubicación actual'}
        </button>
      </div>
      {hint && <p className="hint">{hint}</p>}
      <p className="hint">Hazlo parado en el comedor. Sin ubicación, los estudiantes no pueden registrarse.</p>

      <div className="filters">
        <div>
          <label htmlFor="point-radius">Radio permitido (m)</label>
          <input
            id="point-radius"
            inputMode="numeric"
            value={radius}
            onChange={(e) => setRadius(e.target.value)}
          />
        </div>
        <div>
          <label htmlFor="point-accuracy">Precisión máxima (m)</label>
          <input
            id="point-accuracy"
            inputMode="numeric"
            value={accuracy}
            onChange={(e) => setAccuracy(e.target.value)}
          />
        </div>
      </div>

      <label className="check" htmlFor="point-active">
        <input
          id="point-active"
          type="checkbox"
          checked={active}
          onChange={(e) => setActive(e.target.checked)}
        />
        Punto activo
      </label>

      {error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}

      <div className="actions">
        <button type="submit" disabled={busy}>
          {busy ? 'Guardando…' : 'Guardar'}
        </button>
        <button type="button" className="secondary" onClick={onCancel}>
          Cancelar
        </button>
      </div>
    </form>
  );
}

function ServicesPanel({ point, onClose }: { point: AccessPoint; onClose: () => void }) {
  const [services, setServices] = useState<AccessService[]>([]);
  const [loaded, setLoaded] = useState(false);
  const [reloadKey, setReloadKey] = useState(0);

  const [editing, setEditing] = useState<AccessService | null>(null);
  const [name, setName] = useState('');
  const [start, setStart] = useState('11:00');
  const [end, setEnd] = useState('13:00');
  const [active, setActive] = useState(true);

  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    apiFetch<AccessService[]>(`/api/access-points/${point.id}/services`)
      .then((result) => {
        if (!cancelled) {
          setServices(result);
          setLoaded(true);
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
  }, [point.id, reloadKey]);

  function startEdit(service: AccessService) {
    setEditing(service);
    setName(service.name);
    setStart(toInputTime(service.startTime));
    setEnd(toInputTime(service.endTime));
    setActive(service.isActive);
    setError(null);
  }

  function clearForm() {
    setEditing(null);
    setName('');
    setStart('11:00');
    setEnd('13:00');
    setActive(true);
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);
    setBusy(true);

    try {
      await apiFetch<AccessService>(
        editing
          ? `/api/access-points/${point.id}/services/${editing.id}`
          : `/api/access-points/${point.id}/services`,
        {
          method: editing ? 'PUT' : 'POST',
          body: JSON.stringify({
            name,
            startTime: toApiTime(start),
            endTime: toApiTime(end),
            isActive: active,
          }),
        },
      );

      clearForm();
      setReloadKey((k) => k + 1);
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="card" aria-labelledby="services-title">
      <h2 id="services-title">Horarios de {point.name}</h2>
      <p className="hint">Hora local del colegio. Cada estudiante se registra una vez por servicio y por día.</p>

      {loaded && services.length === 0 && <p>Este punto todavía no tiene horarios.</p>}

      {services.length > 0 && (
        <div className="table-wrap">
          <table className="table">
            <thead>
              <tr>
                <th scope="col">Servicio</th>
                <th scope="col">Desde</th>
                <th scope="col">Hasta</th>
                <th scope="col">Estado</th>
                <th scope="col">Acción</th>
              </tr>
            </thead>
            <tbody>
              {services.map((s) => (
                <tr key={s.id}>
                  <td>{s.name}</td>
                  <td>{toInputTime(s.startTime)}</td>
                  <td>{toInputTime(s.endTime)}</td>
                  <td>{s.isActive ? 'Activo' : 'Apagado'}</td>
                  <td>
                    <button type="button" className="secondary" onClick={() => startEdit(s)}>
                      Editar
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <form onSubmit={handleSubmit} noValidate>
        <h3>{editing ? `Editar ${editing.name}` : 'Nuevo horario'}</h3>

        <label htmlFor="service-name">Nombre</label>
        <input id="service-name" maxLength={50} value={name} onChange={(e) => setName(e.target.value)} />

        <div className="filters">
          <div>
            <label htmlFor="service-start">Desde</label>
            <input id="service-start" type="time" value={start} onChange={(e) => setStart(e.target.value)} />
          </div>
          <div>
            <label htmlFor="service-end">Hasta</label>
            <input id="service-end" type="time" value={end} onChange={(e) => setEnd(e.target.value)} />
          </div>
        </div>

        <label className="check" htmlFor="service-active">
          <input
            id="service-active"
            type="checkbox"
            checked={active}
            onChange={(e) => setActive(e.target.checked)}
          />
          Horario activo
        </label>

        {error && (
          <p role="alert" className="error">
            {error}
          </p>
        )}

        <div className="actions">
          <button type="submit" disabled={busy}>
            {busy ? 'Guardando…' : editing ? 'Guardar cambios' : 'Agregar horario'}
          </button>
          {editing && (
            <button type="button" className="secondary" onClick={clearForm}>
              Cancelar edición
            </button>
          )}
          <button type="button" className="secondary" onClick={onClose}>
            Cerrar
          </button>
        </div>
      </form>
    </section>
  );
}