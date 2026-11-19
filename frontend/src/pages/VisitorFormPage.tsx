import { useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { useParams } from 'react-router';
import { ApiError, apiFetch } from '../api/client';
import { errorMessage } from '../api/errors';
import type { PublicInstitution } from '../api/types';
import { SignaturePad } from '../components/SignaturePad';
import { useInstitution } from '../institution/useInstitution';
import { isValidIdentification, normalizeIdentification } from '../visits/visitRules';

// The visitor's form: public, reached with the QR. Nobody is signed in.
export function VisitorFormPage() {
  const { slug = '' } = useParams();
  const { setPublicBranding } = useInstitution();

  const [school, setSchool] = useState<PublicInstitution | null>(null);
  const [notFound, setNotFound] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);

  const [fullName, setFullName] = useState('');
  const [identification, setIdentification] = useState('');
  const [phone, setPhone] = useState('');
  const [host, setHost] = useState('');
  const [reason, setReason] = useState('');
  const [signature, setSignature] = useState<string | null>(null);

  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState(false);

  // Look like the school while the visitor is not signed in.
  useEffect(() => {
    let cancelled = false;

    apiFetch<PublicInstitution>(`/api/public/institutions/${encodeURIComponent(slug)}`)
      .then((data) => {
        if (!cancelled) {
          setSchool(data);
          setPublicBranding({ name: data.name, primaryColor: data.primaryColor });
        }
      })
      .catch((e) => {
        if (cancelled) {
          return;
        }

        if (e instanceof ApiError && e.status === 404) {
          setNotFound(true);
        } else {
          setLoadError(errorMessage(e));
        }
      });

    return () => {
      cancelled = true;
      setPublicBranding(null);
    };
  }, [slug, setPublicBranding]);

  const ready =
    fullName.trim().length >= 3 &&
    isValidIdentification(normalizeIdentification(identification)) &&
    host.trim().length >= 2 &&
    reason.trim().length >= 2 &&
    signature !== null;

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);
    setBusy(true);

    try {
      await apiFetch<boolean>(`/api/public/visits/${encodeURIComponent(slug)}`, {
        method: 'POST',
        body: JSON.stringify({ fullName, identificationNumber: identification, phone, host, reason, signature }),
      });

      setDone(true);
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setBusy(false);
    }
  }

  if (notFound) {
    return (
      <main className="login">
        <section className="card login__form">
          <p className="eyebrow">VARINS · EDU</p>
          <h1>No encontramos este colegio</h1>
          <p>Revisa el código QR o pregunta en la entrada.</p>
        </section>
      </main>
    );
  }

  if (done) {
    return (
      <main className="login">
        <section className="card login__form" aria-live="polite">
          <p className="eyebrow">VARINS · EDU</p>
          <h1>¡Listo!</h1>
          <p>Recibimos tu solicitud. Acércate al guardia y muestra tu documento de identidad.</p>
        </section>
      </main>
    );
  }

  return (
    <main className="login">
      <form className="card login__form" onSubmit={handleSubmit} noValidate>
        <p className="eyebrow">VARINS · EDU</p>
        <h1>Registro de visitas</h1>
        <p>{school ? school.name : (loadError ?? 'Cargando…')}</p>

        <label htmlFor="visitor-name">Nombre completo</label>
        <input
          id="visitor-name"
          name="name"
          autoComplete="name"
          maxLength={120}
          value={fullName}
          onChange={(e) => setFullName(e.target.value)}
          required
        />

        <label htmlFor="visitor-id">Número de identificación</label>
        <input
          id="visitor-id"
          name="identification"
          autoComplete="off"
          maxLength={30}
          value={identification}
          onChange={(e) => setIdentification(e.target.value)}
          required
        />

        <label htmlFor="visitor-phone">Teléfono (opcional)</label>
        <input
          id="visitor-phone"
          name="phone"
          type="tel"
          autoComplete="tel"
          maxLength={30}
          value={phone}
          onChange={(e) => setPhone(e.target.value)}
        />

        <label htmlFor="visitor-host">¿A quién visitas?</label>
        <input
          id="visitor-host"
          name="host"
          autoComplete="off"
          maxLength={100}
          value={host}
          onChange={(e) => setHost(e.target.value)}
          required
        />

        <label htmlFor="visitor-reason">Motivo de la visita</label>
        <input
          id="visitor-reason"
          name="reason"
          autoComplete="off"
          maxLength={100}
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          required
        />

        <p className="visitor-signature-label">Firma</p>
        <SignaturePad onChange={setSignature} />

        <p className="hint">
          Tus datos se usan solo para registrar tu visita al colegio. El guardia revisará tu documento antes de dejarte
          pasar.
        </p>

        {error && (
          <p role="alert" className="error">
            {error}
          </p>
        )}

        <button type="submit" disabled={busy || !school || !ready}>
          {busy ? 'Enviando…' : 'Registrar visita'}
        </button>
      </form>
    </main>
  );
}