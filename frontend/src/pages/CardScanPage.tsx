import { useCallback, useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { Link } from 'react-router';
import { apiFetch } from '../api/client';
import { errorMessage } from '../api/errors';
import type { ScannedStudent } from '../api/types';
import { useAuth } from '../auth/useAuth';
import { QrScanner } from '../components/QrScanner';
import { useWakeLock } from '../hooks/useWakeLock';

// The result stays on screen this long, and then the camera reads the next card by itself.
const RESUME_MS = 4000;

type Outcome = { ok: true; student: ScannedStudent } | { ok: false; message: string };

export function CardScanPage() {
  const { user } = useAuth();

  // Hiding things is only a convenience: the server enforces this permission.
  const allowed = Boolean(!user?.isPlatform && user?.permissions.includes('cards.scan'));

  // "running": the camera is open. "scanning": it is reading frames right now.
  const [running, setRunning] = useState(false);
  const [scanning, setScanning] = useState(false);
  const [sending, setSending] = useState(false);
  const [outcome, setOutcome] = useState<Outcome | null>(null);
  const [cameraError, setCameraError] = useState<string | null>(null);
  const [pasted, setPasted] = useState('');

  // A guard's phone that goes to sleep stops scanning.
  useWakeLock(running);

  const submit = useCallback(async (text: string) => {
    setScanning(false);
    setSending(true);
    setOutcome(null);

    try {
      const student = await apiFetch<ScannedStudent>('/api/card-scans', {
        method: 'POST',
        body: JSON.stringify({ token: text }),
      });

      navigator.vibrate?.(80);
      setOutcome({ ok: true, student });
    } catch (e) {
      navigator.vibrate?.([80, 60, 80]);
      setOutcome({ ok: false, message: errorMessage(e) });
    } finally {
      setSending(false);
    }
  }, []);

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
    setRunning(true);
    setScanning(true);
  }

  function stop() {
    setRunning(false);
    setScanning(false);
    setOutcome(null);
  }

  function next() {
    setOutcome(null);
    setScanning(true);
  }

  function handlePaste(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    if (pasted.trim()) {
      void submit(pasted.trim());
      setPasted('');
    }
  }

  return (
    <main className="page">
      <header className="page__header">
        <div>
          <p className="eyebrow">VARINS · EDU</p>
          <h1>Verificar carné</h1>
        </div>
        <Link to="/" className="link">
          Volver al inicio
        </Link>
      </header>

      {!allowed && (
        <p role="alert" className="error">
          No tienes permiso para verificar carnés.
        </p>
      )}

      {allowed && (
        <section className="card id-card" aria-live="polite">
          {!running && <p>Apunta la cámara al código QR del carné del estudiante.</p>}

          {running && <QrScanner active={scanning} onDetected={(text) => void submit(text)} onError={handleCameraError} />}

          {running && scanning && <p className="hint">Buscando el código…</p>}
          {sending && <p>Verificando…</p>}

          {outcome !== null && outcome.ok && (
            <div className="scan-result scan-result--ok" role="status">
              <strong>
                {outcome.student.firstName} {outcome.student.lastName}
              </strong>
              <span>{outcome.student.group ?? 'Sin grupo'}</span>
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
            {running && outcome !== null && (
              <button type="button" onClick={next}>
                Escanear siguiente
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
          <label htmlFor="pasted-card">Texto del QR del carné</label>
          <input
            id="pasted-card"
            autoComplete="off"
            value={pasted}
            onChange={(e) => setPasted(e.target.value)}
          />
          <div className="actions">
            <button type="submit" disabled={sending || pasted.trim() === ''}>
              Enviar
            </button>
          </div>
        </form>
      )}
    </main>
  );
}