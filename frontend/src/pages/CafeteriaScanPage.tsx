import { useCallback, useEffect, useRef, useState } from 'react';
import type { FormEvent } from 'react';
import { Link } from 'react-router';
import jsQR from 'jsqr';
import { ApiError, apiFetch } from '../api/client';
import { errorMessage } from '../api/errors';
import type { RegistrationResult } from '../api/types';
import { useAuth } from '../auth/useAuth';

type Phase = 'idle' | 'scanning' | 'sending' | 'done';

// Frames are shrunk before reading them: a QR on a screen stays readable and the phone does less work.
const MAX_FRAME_WIDTH = 640;

function getPosition(): Promise<GeolocationPosition> {
  return new Promise((resolve, reject) => {
    if (!navigator.geolocation) {
      reject(new Error('no_geolocation'));
      return;
    }

    navigator.geolocation.getCurrentPosition(resolve, reject, {
      enableHighAccuracy: true,
      timeout: 15000,
      maximumAge: 0,
    });
  });
}

export function CafeteriaScanPage() {
  const { user } = useAuth();

  // Hiding things is only a convenience: the server decides who may register.
  const allowed = Boolean(user && !user.isPlatform);

  const [phase, setPhase] = useState<Phase>('idle');
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<RegistrationResult | null>(null);
  const [pasted, setPasted] = useState('');

  const videoRef = useRef<HTMLVideoElement>(null);
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const streamRef = useRef<MediaStream | null>(null);

  // The QR is read first and the location is asked right after: the phone sends both together.
  const submit = useCallback(async (token: string) => {
    setPhase('sending');
    setError(null);
    setResult(null);

    try {
      let position: GeolocationPosition;

      try {
        position = await getPosition();
      } catch {
        setError('No se pudo obtener tu ubicación. Revisa el permiso del navegador y el GPS.');
        return;
      }

      const registered = await apiFetch<RegistrationResult>('/api/access/checkin', {
        method: 'POST',
        body: JSON.stringify({
          token,
          latitude: position.coords.latitude,
          longitude: position.coords.longitude,
          accuracyMeters: position.coords.accuracy,
        }),
      });

      setResult(registered);
    } catch (e) {
      setError(e instanceof ApiError ? errorMessage(e) : 'Ocurrió un error inesperado. Inténtalo de nuevo.');
    } finally {
      setPhase('done');
    }
  }, []);

  // While scanning: show the camera and look for a QR in every frame.
  useEffect(() => {
    if (phase !== 'scanning') {
      return;
    }

    const video = videoRef.current;
    const canvas = canvasRef.current;
    const stream = streamRef.current;
    const context = canvas?.getContext('2d', { willReadFrequently: true });

    if (!video || !canvas || !stream || !context) {
      return;
    }

    let stopped = false;
    let frame = 0;

    video.srcObject = stream;
    void video.play().catch(() => {
      // Autoplay can fail; the person can tap the video.
    });

    function tick() {
      if (stopped || !video || !canvas || !context) {
        return;
      }

      if (video.readyState === video.HAVE_ENOUGH_DATA && video.videoWidth > 0) {
        const scale = Math.min(1, MAX_FRAME_WIDTH / video.videoWidth);
        canvas.width = Math.round(video.videoWidth * scale);
        canvas.height = Math.round(video.videoHeight * scale);
        context.drawImage(video, 0, 0, canvas.width, canvas.height);

        const image = context.getImageData(0, 0, canvas.width, canvas.height);
        const code = jsQR(image.data, image.width, image.height, { inversionAttempts: 'dontInvert' });

        if (code?.data) {
          stopped = true;
          void submit(code.data);
          return;
        }
      }

      frame = requestAnimationFrame(tick);
    }

    frame = requestAnimationFrame(tick);

    // Runs when scanning ends or the page closes: the camera must never stay on.
    return () => {
      stopped = true;
      cancelAnimationFrame(frame);
      stream.getTracks().forEach((track) => track.stop());
      streamRef.current = null;
    };
  }, [phase, submit]);

  async function startScan() {
    setError(null);
    setResult(null);

    if (!navigator.mediaDevices?.getUserMedia) {
      setError('Este navegador no permite usar la cámara.');
      return;
    }

    try {
      streamRef.current = await navigator.mediaDevices.getUserMedia({
        video: { facingMode: { ideal: 'environment' } },
        audio: false,
      });
      setPhase('scanning');
    } catch {
      setError('No se pudo abrir la cámara. Revisa el permiso del navegador.');
    }
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
          <h1>Registrar mi comida</h1>
        </div>
        <Link to="/" className="link">
          Volver al inicio
        </Link>
      </header>

      {!allowed && (
        <p role="alert" className="error">
          Las cuentas de plataforma no se registran en el comedor.
        </p>
      )}

      {allowed && (
        <section className="card id-card" aria-live="polite">
          {phase === 'idle' && (
            <>
              <p>Apunta la cámara al código QR de la pantalla del comedor.</p>
              <p className="hint">Se te pedirá permiso para usar la cámara y tu ubicación.</p>
            </>
          )}

          {phase === 'scanning' && (
            <>
              <video ref={videoRef} className="scan-video" playsInline muted />
              <canvas ref={canvasRef} hidden />
              <p className="hint">Buscando el código…</p>
            </>
          )}

          {phase === 'sending' && <p>Registrando…</p>}

          {phase === 'done' && result && (
            <p role="status" className="success">
              {result.firstName}, quedaste registrado en {result.serviceName} ({result.pointName}).
            </p>
          )}

          {error && (
            <p role="alert" className="error">
              {error}
            </p>
          )}

          {(phase === 'idle' || phase === 'done') && (
            <div className="actions">
              <button type="button" onClick={startScan}>
                {phase === 'done' ? 'Escanear de nuevo' : 'Escanear código'}
              </button>
            </div>
          )}

          {phase === 'scanning' && (
            <div className="actions">
              <button type="button" className="secondary" onClick={() => setPhase('idle')}>
                Cancelar
              </button>
            </div>
          )}
        </section>
      )}

      {allowed && import.meta.env.DEV && (
        <form className="card" onSubmit={handlePaste} noValidate>
          <h2>Pegar texto del QR (solo desarrollo)</h2>
          <label htmlFor="pasted">Texto del QR</label>
          <input id="pasted" autoComplete="off" value={pasted} onChange={(e) => setPasted(e.target.value)} />
          <div className="actions">
            <button type="submit" disabled={phase === 'sending' || pasted.trim() === ''}>
              Enviar
            </button>
          </div>
        </form>
      )}
    </main>
  );
}