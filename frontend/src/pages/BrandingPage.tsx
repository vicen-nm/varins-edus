import { useState } from 'react';
import type { FormEvent } from 'react';
import { Link } from 'react-router';
import { apiFetch } from '../api/client';
import { errorMessage } from '../api/errors';
import type { InstitutionDetails } from '../api/types';
import { useAuth } from '../auth/useAuth';
import { useInstitution } from '../institution/useInstitution';
import { buildBrandVariables } from '../theme/brand';

const DEFAULT_COLOR = '#007690';

export function BrandingPage() {
  const { user } = useAuth();
  const { institution, reload } = useInstitution();

  // Hiding the page is only a convenience: the server is what enforces this permission.
  const allowed = user?.permissions.includes('branding.manage') ?? false;

  return (
    <main className="page">
      <header className="page__header">
        <div>
          <p className="eyebrow">VARINS · EDU</p>
          <h1>Identidad de la institución</h1>
        </div>
        <Link to="/" className="link">
          Volver al inicio
        </Link>
      </header>

      {!allowed && (
        <p role="alert" className="error">
          No tienes permiso para cambiar la identidad de la institución.
        </p>
      )}

      {allowed && !institution && <p>Cargando…</p>}

      {allowed && institution && <BrandingForm institution={institution} onSaved={reload} />}
    </main>
  );
}

function BrandingForm({
  institution,
  onSaved,
}: {
  institution: InstitutionDetails;
  onSaved: () => Promise<void>;
}) {
  const settings = institution.settings;

  const [displayName, setDisplayName] = useState(settings?.displayName ?? '');
  const [useDefaultColor, setUseDefaultColor] = useState(!settings?.primaryColor);
  const [color, setColor] = useState(settings?.primaryColor ?? DEFAULT_COLOR);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  // The colors the app will really use, adjusted so they always read well.
  const adjusted = useDefaultColor ? null : buildBrandVariables(color);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setMessage(null);
    setError(null);

    try {
      await apiFetch<void>('/api/institution/branding', {
        method: 'PUT',
        body: JSON.stringify({
          displayName: displayName.trim() || null,
          primaryColor: useDefaultColor ? null : color,
          accentColor: settings?.accentColor ?? null,
        }),
      });

      await onSaved();
      setMessage('Cambios guardados.');
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="card" onSubmit={handleSubmit} noValidate>
      <label htmlFor="displayName">Nombre para mostrar</label>
      <input
        id="displayName"
        name="displayName"
        maxLength={200}
        value={displayName}
        onChange={(e) => setDisplayName(e.target.value)}
      />
      <p className="hint">Si lo dejas vacío se usa «{institution.name}».</p>

      <label className="check" htmlFor="useDefaultColor">
        <input
          id="useDefaultColor"
          type="checkbox"
          checked={useDefaultColor}
          onChange={(e) => setUseDefaultColor(e.target.checked)}
        />
        Usar el color de VarinsEdu
      </label>

      <label htmlFor="primaryColor">Color principal</label>
      <input
        id="primaryColor"
        type="color"
        value={color}
        disabled={useDefaultColor}
        onChange={(e) => setColor(e.target.value)}
      />

      {adjusted && (
        <p className="hint">
          Para que siempre se lea bien, la aplicación usa{' '}
          <span className="swatch" style={{ background: adjusted['--brand-primary-light'] }} />{' '}
          <code>{adjusted['--brand-primary-light']}</code> en modo claro y{' '}
          <span className="swatch" style={{ background: adjusted['--brand-primary-dark'] }} />{' '}
          <code>{adjusted['--brand-primary-dark']}</code> en modo oscuro.
        </p>
      )}

      {error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}

      {message && (
        <p role="status" className="success">
          {message}
        </p>
      )}

      <div className="actions">
        <button type="submit" disabled={busy}>
          {busy ? 'Guardando…' : 'Guardar cambios'}
        </button>
      </div>
    </form>
  );
}