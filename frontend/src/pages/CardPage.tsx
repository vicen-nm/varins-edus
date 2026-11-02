import { Link } from 'react-router';
import { QRCodeSVG } from 'qrcode.react';
import { apiFetch } from '../api/client';
import { errorMessage } from '../api/errors';
import type { StudentCard } from '../api/types';
import { useAuth } from '../auth/useAuth';
import { useRefreshingValue } from '../hooks/useRefreshingValue';

// The token lives 45 s on the server: renewing every 25 s always leaves time to scan it.
const REFRESH_MS = 25_000;

// Defined outside the component so it keeps its identity, which the hook relies on.
const loadCard = () => apiFetch<StudentCard>('/api/me/card');

export function CardPage() {
  const { user } = useAuth();

  // Hiding things is only a convenience: the server decides who has a card.
  const allowed = Boolean(user && !user.isPlatform);

  const { value: card, error } = useRefreshingValue(allowed ? loadCard : null, REFRESH_MS);

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

      {allowed && error !== null && (
        <p role="alert" className="error">
          {errorMessage(error)}
        </p>
      )}

      {allowed && error === null && !card && <p>Cargando…</p>}

      {allowed && card && (
        <section className="card id-card" aria-labelledby="card-name">
          <h2 id="card-name">
            {card.firstName} {card.lastName}
          </h2>
          <p>{card.group ?? 'Sin grupo'}</p>

          <div className="qr-box" role="img" aria-label="Código QR de tu carné. Cambia cada pocos segundos.">
            <QRCodeSVG value={card.token} size={256} level="M" bgColor="#ffffff" fgColor="#000000" />
          </div>

          <p className="hint">El código cambia solo. No lo compartas ni lo captures.</p>
        </section>
      )}
    </main>
  );
}