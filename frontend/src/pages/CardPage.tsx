import { useEffect, useState } from 'react';
import { Link } from 'react-router';
import { QRCodeSVG } from 'qrcode.react';
import { apiFetch } from '../api/client';
import { errorMessage } from '../api/errors';
import type { StudentCard } from '../api/types';
import { useAuth } from '../auth/useAuth';

// The token lives 45 s on the server: renewing every 25 s always leaves time to scan it.
const REFRESH_MS = 25_000;

type Info = { firstName: string; lastName: string; group: string | null };

export function CardPage() {
  const { user } = useAuth();

  // Hiding things is only a convenience: the server decides who has a card.
  const allowed = Boolean(user && !user.isPlatform);

  const [info, setInfo] = useState<Info | null>(null);
  const [qr, setQr] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!allowed) {
      return;
    }

    let cancelled = false;
    let timer: number | undefined;

    function schedule() {
      if (!cancelled) {
        timer = window.setTimeout(load, REFRESH_MS);
      }
    }

    async function load() {
      if (document.hidden) {
        schedule();
        return;
      }

      try {
        const card = await apiFetch<StudentCard>('/api/me/card');

        if (cancelled) {
          return;
        }

        setInfo({ firstName: card.firstName, lastName: card.lastName, group: card.group });
        setQr(card.token);
        setError(null);
      } catch (e) {
        if (!cancelled) {
          // Never keep showing a code that may already be expired.
          setQr(null);
          setError(errorMessage(e));
        }
      }

      schedule();
    }

    function handleVisibility() {
      window.clearTimeout(timer);

      if (document.hidden) {
        setQr(null);
        return;
      }

      void load();
    }

    document.addEventListener('visibilitychange', handleVisibility);
    void load();

    return () => {
      cancelled = true;
      window.clearTimeout(timer);
      document.removeEventListener('visibilitychange', handleVisibility);
    };
  }, [allowed]);

  return (
    <main className="page">
      <header className="page__header">
        <div>
          <p className="eyebrow">VARINS · EDU</p>
          <h1>Mi carné</h1>
        </div>
        <Link to="/" className="link">
          Volver al inicio
        </Link>
      </header>

      {!allowed && (
        <p role="alert" className="error">
          Las cuentas de plataforma no tienen carné.
        </p>
      )}

      {allowed && error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}

      {allowed && !error && !qr && <p>Cargando…</p>}

      {allowed && info && (
        <section className="card id-card" aria-labelledby="card-name">
          <h2 id="card-name">
            {info.firstName} {info.lastName}
          </h2>
          <p>{info.group ?? 'Sin grupo'}</p>

          {qr && (
            <div className="qr-box" role="img" aria-label="Código QR de tu carné. Cambia cada pocos segundos.">
              <QRCodeSVG value={qr} size={256} level="M" bgColor="#ffffff" fgColor="#000000" />
            </div>
          )}

          <p className="hint">El código cambia solo. No lo compartas ni lo captures.</p>
        </section>
      )}
    </main>
  );
}