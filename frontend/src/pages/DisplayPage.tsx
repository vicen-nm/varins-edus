import { useState } from 'react';
import { Link } from 'react-router';
import { errorMessage } from '../api/errors';
import type { DisplayPreferences } from '../api/types';
import { PreferencesForm } from '../components/PreferencesForm';
import { usePreferences } from '../preferences/usePreferences';

export function DisplayPage() {
  const { preferences, save } = usePreferences();

  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function handleSave(next: DisplayPreferences) {
    setBusy(true);
    setMessage(null);
    setError(null);

    try {
      await save(next);
      setMessage('Cambios guardados. Ya se aplican en toda la aplicación.');
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <main className="page">
      <header className="page__header">
        <div>
          <p className="eyebrow">VARINS · EDU</p>
          <h1>Mi pantalla</h1>
        </div>
        <Link to="/" className="link">
          Volver al inicio
        </Link>
      </header>

      <section className="card" aria-labelledby="display-title">
        <h2 id="display-title">Cómo quieres ver la aplicación</h2>

        <PreferencesForm initial={preferences} busy={busy} submitLabel="Guardar" onSave={handleSave} />

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

        <p className="hint">
          Solo se guarda cómo ver la pantalla. Nunca se guarda por qué lo necesitas ni ningún dato de salud.
        </p>
      </section>
    </main>
  );
}