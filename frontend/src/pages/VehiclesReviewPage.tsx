import { useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { Link } from 'react-router';
import { apiFetch } from '../api/client';
import { errorMessage } from '../api/errors';
import type { StaffVehicleItem, StaffVehicleList, VehicleItem } from '../api/types';
import { useAuth } from '../auth/useAuth';
import { MAX_NOTE_LENGTH, isValidPlate, normalizePlate, statusLabel } from '../vehicles/vehicleRules';

const PAGE_SIZE = 25;

type StatusFilter = 'all' | 'pending' | 'approved' | 'suspended' | 'rejected' | 'withdrawn';
type Decision = { vehicle: StaffVehicleItem; status: 'rejected' | 'suspended' };

export function VehiclesReviewPage() {
  const { user } = useAuth();

  // Hiding things is only a convenience: the server enforces these permissions.
  const canView = Boolean(!user?.isPlatform && user?.permissions.includes('vehicles.view'));
  const canManage = Boolean(!user?.isPlatform && user?.permissions.includes('vehicles.manage'));

  const [status, setStatus] = useState<StatusFilter>('pending');
  const [search, setSearch] = useState('');
  const [debounced, setDebounced] = useState('');
  const [skip, setSkip] = useState(0);
  const [reloadKey, setReloadKey] = useState(0);

  const [data, setData] = useState<StaffVehicleList | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [decision, setDecision] = useState<Decision | null>(null);
  const [registering, setRegistering] = useState(false);

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

    let cancelled = false;

    const params = new URLSearchParams({ take: String(PAGE_SIZE), skip: String(skip) });
    if (status !== 'all') {
      params.set('status', status);
    }
    if (debounced) {
      params.set('search', debounced);
    }

    apiFetch<StaffVehicleList>(`/api/vehicles?${params.toString()}`)
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
  }, [canView, status, debounced, skip, reloadKey]);

  async function changeStatus(
    vehicle: StaffVehicleItem,
    next: 'approved' | 'rejected' | 'suspended',
    note: string | null,
  ) {
    setBusyId(vehicle.id);
    setError(null);
    setMessage(null);

    try {
      await apiFetch<void>(`/api/vehicles/${vehicle.id}/status`, {
        method: 'PUT',
        body: JSON.stringify({ status: next, note }),
      });

      setDecision(null);
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
          <h1>Revisión de vehículos</h1>
        </div>
        <Link to="/" className="link">
          Volver al inicio
        </Link>
      </header>

      {!canView && (
        <p role="alert" className="error">
          No tienes permiso para ver los vehículos.
        </p>
      )}

      {canView && message && (
        <p role="status" className="success">
          {message}
        </p>
      )}

      {canView && error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}

      {canView && decision && (
        <NoteForm
          key={decision.vehicle.id}
          decision={decision}
          busy={busyId === decision.vehicle.id}
          onConfirm={(note) => changeStatus(decision.vehicle, decision.status, note)}
          onCancel={() => setDecision(null)}
        />
      )}

      {canView && canManage && registering && (
        <RegisterForm
          onRegistered={(text) => {
            setRegistering(false);
            setMessage(text);
            setReloadKey((k) => k + 1);
          }}
          onCancel={() => setRegistering(false)}
        />
      )}

      {canView && (
        <section className="card filters" aria-label="Filtros">
          <div>
            <label htmlFor="vehicles-status">Estado</label>
            <select
              id="vehicles-status"
              value={status}
              onChange={(e) => {
                setStatus(e.target.value as StatusFilter);
                setSkip(0);
              }}
            >
              <option value="pending">Pendientes</option>
              <option value="approved">Aprobados</option>
              <option value="suspended">Suspendidos</option>
              <option value="rejected">Rechazados</option>
              <option value="withdrawn">Retirados</option>
              <option value="all">Todos</option>
            </select>
          </div>
          <div>
            <label htmlFor="vehicles-search">Buscar</label>
            <input
              id="vehicles-search"
              autoComplete="off"
              placeholder="Placa o nombre"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
          </div>
          <div>
            <p className="hint">Para estudiantes sin teléfono, o cuando ya revisaste los documentos.</p>
            {canManage && !registering && (
              <div className="actions">
                <button type="button" onClick={() => setRegistering(true)}>
                  Registrar una moto
                </button>
              </div>
            )}
          </div>
        </section>
      )}

      {canView && (
        <section className="card" aria-labelledby="vehicles-title">
          <h2 id="vehicles-title">{data ? `${from}–${to} de ${total}` : 'Cargando…'}</h2>

          {data && data.items.length === 0 && <p>No hay vehículos con esos filtros.</p>}

          {data && data.items.length > 0 && (
            <div className="table-wrap">
              <table className="table">
                <thead>
                  <tr>
                    <th scope="col">Placa</th>
                    <th scope="col">Vehículo</th>
                    <th scope="col">Estudiante</th>
                    <th scope="col">Estado</th>
                    <th scope="col">Revisado por</th>
                    {canManage && <th scope="col">Acciones</th>}
                  </tr>
                </thead>
                <tbody>
                  {data.items.map((v) => (
                    <tr key={v.id}>
                      <td>
                        <code>{v.plate}</code>
                      </td>
                      <td>
                        {v.make} {v.model}, {v.color}
                      </td>
                      <td>
                        {v.firstName} {v.lastName}
                        <span className="hint"> {v.group ?? 'Sin grupo'}</span>
                      </td>
                      <td>
                        {statusLabel(v.status)}
                        {v.statusNote && <span className="hint"> Motivo: {v.statusNote}</span>}
                      </td>
                      <td>
                        {v.reviewedBy ?? '—'}
                        {v.reviewedAt && (
                          <span className="hint"> {new Date(v.reviewedAt).toLocaleDateString('es-CR')}</span>
                        )}
                      </td>
                      {canManage && (
                        <td>
                          <div className="actions">
                            {v.status === 'pending' && (
                              <>
                                <button
                                  type="button"
                                  disabled={busyId === v.id}
                                  onClick={() => changeStatus(v, 'approved', null)}
                                >
                                  Aprobar
                                </button>
                                <button
                                  type="button"
                                  className="secondary"
                                  disabled={busyId === v.id}
                                  onClick={() => setDecision({ vehicle: v, status: 'rejected' })}
                                >
                                  Rechazar
                                </button>
                              </>
                            )}
                            {v.status === 'approved' && (
                              <button
                                type="button"
                                className="secondary"
                                disabled={busyId === v.id}
                                onClick={() => setDecision({ vehicle: v, status: 'suspended' })}
                              >
                                Suspender
                              </button>
                            )}
                            {v.status === 'suspended' && (
                              <button
                                type="button"
                                disabled={busyId === v.id}
                                onClick={() => changeStatus(v, 'approved', null)}
                              >
                                Reactivar
                              </button>
                            )}
                          </div>
                        </td>
                      )}
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

function NoteForm({
  decision,
  busy,
  onConfirm,
  onCancel,
}: {
  decision: Decision;
  busy: boolean;
  onConfirm: (note: string) => void;
  onCancel: () => void;
}) {
  const [note, setNote] = useState('');

  const verb = decision.status === 'rejected' ? 'Rechazar' : 'Suspender';

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    onConfirm(note.trim());
  }

  return (
    <form className="card" onSubmit={handleSubmit} noValidate aria-labelledby="decision-title">
      <h2 id="decision-title">
        {verb} la moto {decision.vehicle.plate}
      </h2>

      <label htmlFor="decision-note">Motivo</label>
      <input
        id="decision-note"
        autoComplete="off"
        maxLength={MAX_NOTE_LENGTH}
        value={note}
        onChange={(e) => setNote(e.target.value)}
      />
      <p className="hint">
        El estudiante lo verá. No escribas datos personales ni de documentos: basta con decir qué falta o qué pasó.
      </p>

      <div className="actions">
        <button type="submit" disabled={busy || note.trim() === ''}>
          {busy ? 'Guardando…' : verb}
        </button>
        <button type="button" className="secondary" onClick={onCancel}>
          Cancelar
        </button>
      </div>
    </form>
  );
}

function RegisterForm({
  onRegistered,
  onCancel,
}: {
  onRegistered: (message: string) => void;
  onCancel: () => void;
}) {
  const [identification, setIdentification] = useState('');
  const [plate, setPlate] = useState('');
  const [make, setMake] = useState('');
  const [model, setModel] = useState('');
  const [color, setColor] = useState('');
  const [approveNow, setApproveNow] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const normalized = normalizePlate(plate);
  const ready =
    identification.trim() !== '' &&
    isValidPlate(normalized) &&
    make.trim() !== '' &&
    model.trim() !== '' &&
    color.trim() !== '';

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);
    setBusy(true);

    try {
      const created = await apiFetch<VehicleItem>('/api/vehicles', {
        method: 'POST',
        body: JSON.stringify({ identificationNumber: identification, plate, make, model, color, approveNow }),
      });

      onRegistered(`Moto ${created.plate} registrada: ${statusLabel(created.status).toLowerCase()}.`);
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="card" onSubmit={handleSubmit} noValidate aria-labelledby="register-title">
      <h2 id="register-title">Registrar una moto</h2>

      <label htmlFor="register-identification">Cédula del estudiante</label>
      <input
        id="register-identification"
        autoComplete="off"
        inputMode="numeric"
        value={identification}
        onChange={(e) => setIdentification(e.target.value)}
      />

      <label htmlFor="register-plate">Placa</label>
      <input
        id="register-plate"
        autoComplete="off"
        spellCheck={false}
        value={plate}
        onChange={(e) => setPlate(e.target.value)}
      />
      {normalized && (
        <p className="hint">
          Se guardará como <code>{normalized}</code>
          {!isValidPlate(normalized) && ' (debe tener de 3 a 10 letras o números)'}.
        </p>
      )}

      <label htmlFor="register-make">Marca</label>
      <input id="register-make" autoComplete="off" maxLength={60} value={make} onChange={(e) => setMake(e.target.value)} />

      <label htmlFor="register-model">Modelo</label>
      <input
        id="register-model"
        autoComplete="off"
        maxLength={60}
        value={model}
        onChange={(e) => setModel(e.target.value)}
      />

      <label htmlFor="register-color">Color</label>
      <input
        id="register-color"
        autoComplete="off"
        maxLength={40}
        value={color}
        onChange={(e) => setColor(e.target.value)}
      />

      <label className="check" htmlFor="register-approve">
        <input
          id="register-approve"
          type="checkbox"
          checked={approveNow}
          onChange={(e) => setApproveNow(e.target.checked)}
        />
        Aprobar de una vez (ya revisé los documentos)
      </label>

      {error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}

      <div className="actions">
        <button type="submit" disabled={busy || !ready}>
          {busy ? 'Registrando…' : 'Registrar'}
        </button>
        <button type="button" className="secondary" onClick={onCancel}>
          Cancelar
        </button>
      </div>
    </form>
  );
}