import { useCallback, useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { Link } from 'react-router';
import { apiFetch } from '../api/client';
import { errorMessage } from '../api/errors';
import type { ExitOption, ExitPreview, ExitResult } from '../api/types';
import { useAuth } from '../auth/useAuth';
import { QrScanner } from '../components/QrScanner';
import { formatWindow } from '../exits/exitRules';
import { useWakeLock } from '../hooks/useWakeLock';

// The result stays on screen this long, and then the camera reads the next card by itself.
const RESUME_MS = 4000;

// The card code lives 45 s on the server: the choice must be made before it expires.
const PREVIEW_MS = 40_000;

type Outcome = { ok: true; result: ExitResult } | { ok: false; message: string };

export function ExitScanPage() {
  const { user } = useAuth();

  // Hiding things is only a convenience: the server enforces this permission.
  const allowed = Boolean(!user?.isPlatform && user?.permissions.includes('exits.scan'));

  // "running": the camera is open. "scanning": it is reading frames right now.
  const [running, setRunning] = useState(false);
  const [scanning, setScanning] = useState(false);
  const [busy, setBusy] = useState(false);
  const [token, setToken] = useState<string | null>(null);
  const [preview, setPreview] = useState<ExitPreview | null>(null);
  const [outcome, setOutcome] = useState<Outcome | null>(null);
  const [cameraError, setCameraError] = useState<string | null>(null);
  const [pasted, setPasted] = useState('');

  // A guard's phone that goes to sleep stops scanning.
  useWakeLock(running);

  const check = useCallback(async (text: string) => {
    setScanning(false);
    setBusy(true);
    setOutcome(null);
    setPreview(null);

    try {
      const result = await apiFetch<ExitPreview>('/api/exits/preview', {
        method: 'POST',
        body: JSON.stringify({ token: text }),
      });

      navigator.vibrate?.(60);
      setToken(text);
      setPreview(result);
    } catch (e) {
      navigator.vibrate?.([80, 60, 80]);
      setOutcome({ ok: false, message: errorMessage(e) });
    } finally {
      setBusy(false);
    }
  }, []);

  async function register(permit: ExitOption) {
    if (!token) {
      return;
    }

    setBusy(true);

    try {
      const result = await apiFetch<ExitResult>('/api/exits/register', {
        method: 'POST',
        body: JSON.stringify({ token, permitId: permit.id }),
      });

      navigator.vibrate?.(80);
      setOutcome({ ok: true, result });
    } catch (e) {
      navigator.vibrate?.([80, 60, 80]);
      setOutcome({ ok: false, message: errorMessage(e) });
    } finally {
      setBusy(false);
      setPreview(null);
      setToken(null);
    }
  }

  // The code is about to expire: start over instead of failing on the guard's tap.
  useEffect(() => {
    if (!preview) {
      return;
    }

    const timer = window.setTimeout(() => {
      setPreview(null);
      setToken(null);
      setOutcome({ ok: false, message: 'El carné venció. Escanéalo de nuevo.' });
    }, PREVIEW_MS);

    return () => window.clearTimeout(timer);
  }, [preview]);

  // After showing the result, the camera goes back to reading.
  useEffect(() => {
    if (!outcome || !running) {
      return;
    }

    const timer = window.setTimeout(() => {
      setOutcome(null);
      setScanning(true);
    }, RESUME_MS);

    return () => window.clearTimeout(timer);
  }, [outcome, running]);

  const handleCameraError = useCallback((message: string) => {
    setCameraError(message);
    setRunning(false);
    setScanning(false);
  }, []);

  function start() {
    setCameraError(null);
    setOutcome(null);
    setPreview(null);
    setRunning(true);
    setScanning(true);
  }

  function stop() {
    setRunning(false);
    setScanning(false);
    setOutcome(null);
    setPreview(null);
    setToken(null);
  }

  function cancelPreview() {
    setPreview(null);
    setToken(null);
    setScanning(true);
  }

  function handlePaste(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    if (pasted.trim()) {
      void check(pasted.trim());
      setPasted('');
    }
  }

  return (
    <main className="page">
      <header className="page__header">
        <div>
          <p className="eyebrow">VARINS · EDU</p>
          <h1>Salida con permiso</h1>
        </div>
        <Link to="/" className="link">
          Volver al inicio
        </Link>
      </header>

      {!allowed && (
        <p role="alert" className="error">
          No tienes permiso para registrar salidas.
        </p>
      )}

      {allowed && (
        <section className="card id-card" aria-live="polite">
          {!running && <p>Apunta la cámara al código QR del carné del estudiante.</p>}

          {running && <QrScanner active={scanning} onDetected={(text) => void check(text)} onError={handleCameraError} />}

          {running && scanning && <p className="hint">Buscando el código…</p>}
          {busy && <p>Verificando…</p>}

          {preview && (
            <div className="scan-result scan-result--ok" role="status">
              <strong>
                {preview.student.firstName} {preview.student.lastName}
              </strong>
              <span>{preview.student.group ?? 'Sin grupo'}</span>
            </div>
          )}

          {preview && (
            <ul className="vehicle-list">
              {preview.permits.map((p) => (
                <li key={p.id}>
                  <p>Permiso de {formatWindow(p.fromTime, p.toTime)}</p>
                  <button type="button" disabled={busy} onClick={() => register(p)}>
                    Registrar salida
                  </button>
                </li>
              ))}
            </ul>
          )}

          {preview && <p className="hint">Confirma antes de que venza el código (40 segundos).</p>}

          {outcome !== null && outcome.ok && (
            <div className="scan-result scan-result--ok" role="status">
              <strong>Salida autorizada</strong>
              <span>
                {outcome.result.student.firstName} {outcome.result.student.lastName}
              </span>
            </div>
          )}

          {outcome !== null && !outcome.ok && (
            <div className="scan-result scan-result--error" role="alert">
              {outcome.message}
            </div>
          )}

          {cameraError && (
            <p role="alert" className="error">
              {cameraError}
            </p>
          )}

          <div className="actions">
            {!running && (
              <button type="button" onClick={start}>
                Iniciar cámara
              </button>
            )}
            {running && preview && (
              <button type="button" className="secondary" onClick={cancelPreview}>
                Cancelar
              </button>
            )}
            {running && (
              <button type="button" className="secondary" onClick={stop}>
                Detener
              </button>
            )}
          </div>
        </section>
      )}

      {allowed && import.meta.env.DEV && (
        <form className="card" onSubmit={handlePaste} noValidate>
          <h2>Pegar texto del QR (solo desarrollo)</h2>
          <label htmlFor="pasted-exit">Texto del QR del carné</label>
          <input id="pasted-exit" autoComplete="off" value={pasted} onChange={(e) => setPasted(e.target.value)} />
          <div className="actions">
            <button type="submit" disabled={busy || pasted.trim() === ''}>
              Enviar
            </button>
          </div>
        </form>
      )}
    </main>
  );
}