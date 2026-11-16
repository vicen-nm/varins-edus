import { useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { Link } from 'react-router';
import { apiFetch } from '../api/client';
import { errorMessage } from '../api/errors';
import type { VehicleItem } from '../api/types';
import { useAuth } from '../auth/useAuth';
import { MAX_LIVE_VEHICLES, isLive, isValidPlate, normalizePlate, statusLabel } from '../vehicles/vehicleRules';

export function MyVehiclesPage() {
  const { user } = useAuth();

  // Hiding things is only a convenience: the server decides who is an active student.
  const allowed = Boolean(user && !user.isPlatform);

  const [items, setItems] = useState<VehicleItem[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState(0);
  const [busyId, setBusyId] = useState<string | null>(null);

  const [plate, setPlate] = useState('');
  const [make, setMake] = useState('');
  const [model, setModel] = useState('');
  const [color, setColor] = useState('');
  const [saving, setSaving] = useState(false);
  const [message, setMessage] = useState<string | null>(null);

  useEffect(() => {
    if (!allowed) {
      return;
    }

    let cancelled = false;

    apiFetch<VehicleItem[]>('/api/me/vehicles')
      .then((result) => {
        if (!cancelled) {
          setItems(result);
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

  const normalized = normalizePlate(plate);
  const liveCount = items?.filter((v) => isLive(v.status)).length ?? 0;
  const canRequest = liveCount < MAX_LIVE_VEHICLES;
  const ready = isValidPlate(normalized) && make.trim() !== '' && model.trim() !== '' && color.trim() !== '';

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);
    setMessage(null);
    setSaving(true);

    try {
      await apiFetch<VehicleItem>('/api/me/vehicles', {
        method: 'POST',
        body: JSON.stringify({ plate, make, model, color }),
      });

      setPlate('');
      setMake('');
      setModel('');
      setColor('');
      setMessage('Solicitud enviada. El personal del colegio la revisará.');
      setReloadKey((k) => k + 1);
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setSaving(false);
    }
  }

  async function handleWithdraw(vehicle: VehicleItem) {
    if (!window.confirm(`¿Retirar la moto ${vehicle.plate}? Dejará de estar registrada.`)) {
      return;
    }

    setBusyId(vehicle.id);
    setError(null);
    setMessage(null);

    try {
      await apiFetch<void>(`/api/me/vehicles/${vehicle.id}`, { method: 'DELETE' });
      setReloadKey((k) => k + 1);
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setBusyId(null);
    }
  }

  return (
    <main className="page">
      <header className="page__header">
        <div>
          <p className="eyebrow">VARINS · EDU</p>
          <h1>Mis vehículos</h1>
        </div>
        <Link to="/" className="link">
          Volver al inicio
        </Link>
      </header>

      {!allowed && (
        <p role="alert" className="error">
          Las cuentas de plataforma no tienen vehículos.
        </p>
      )}

      {allowed && error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}

      {allowed && message && (
        <p role="status" className="success">
          {message}
        </p>
      )}

      {allowed && items !== null && (
        <section className="card" aria-labelledby="my-vehicles-title">
          <h2 id="my-vehicles-title">Mis motos</h2>

          {items.length === 0 && <p>Todavía no has registrado ningún vehículo.</p>}

          {items.length > 0 && (
            <div className="table-wrap">
              <table className="table">
                <thead>
                  <tr>
                    <th scope="col">Placa</th>
                    <th scope="col">Vehículo</th>
                    <th scope="col">Estado</th>
                    <th scope="col">Acción</th>
                  </tr>
                </thead>
                <tbody>
                  {items.map((v) => (
                    <tr key={v.id}>
                      <td>
                        <code>{v.plate}</code>
                      </td>
                      <td>
                        {v.make} {v.model}, {v.color}
                      </td>
                      <td>
                        {statusLabel(v.status)}
                        {v.statusNote && <span className="hint"> Motivo: {v.statusNote}</span>}
                      </td>
                      <td>
                        {isLive(v.status) && (
                          <button
                            type="button"
                            className="secondary"
                            disabled={busyId === v.id}
                            onClick={() => handleWithdraw(v)}
                          >
                            {busyId === v.id ? 'Retirando…' : 'Retirar'}
                          </button>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>
      )}

      {allowed && items !== null && (
        <form className="card" onSubmit={handleSubmit} noValidate aria-labelledby="request-title">
          <h2 id="request-title">Solicitar el registro de una moto</h2>

          {!canRequest && (
            <p className="hint">
              Ya tienes el máximo de {MAX_LIVE_VEHICLES} vehículos activos. Retira uno para registrar otro.
            </p>
          )}

          <label htmlFor="vehicle-plate">Placa</label>
          <input
            id="vehicle-plate"
            autoComplete="off"
            spellCheck={false}
            value={plate}
            onChange={(e) => setPlate(e.target.value)}
            disabled={!canRequest}
          />
          {normalized && (
            <p className="hint">
              Se guardará como <code>{normalized}</code>
              {!isValidPlate(normalized) && ' (debe tener de 3 a 10 letras o números)'}.
            </p>
          )}

          <label htmlFor="vehicle-make">Marca</label>
          <input
            id="vehicle-make"
            autoComplete="off"
            maxLength={60}
            value={make}
            onChange={(e) => setMake(e.target.value)}
            disabled={!canRequest}
          />

          <label htmlFor="vehicle-model">Modelo</label>
          <input
            id="vehicle-model"
            autoComplete="off"
            maxLength={60}
            value={model}
            onChange={(e) => setModel(e.target.value)}
            disabled={!canRequest}
          />

          <label htmlFor="vehicle-color">Color</label>
          <input
            id="vehicle-color"
            autoComplete="off"
            maxLength={40}
            value={color}
            onChange={(e) => setColor(e.target.value)}
            disabled={!canRequest}
          />

          <div className="actions">
            <button type="submit" disabled={saving || !canRequest || !ready}>
              {saving ? 'Enviando…' : 'Solicitar registro'}
            </button>
          </div>
        </form>
      )}
    </main>
  );
}