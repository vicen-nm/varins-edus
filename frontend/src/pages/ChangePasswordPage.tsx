import { useState } from 'react';
import type { FormEvent } from 'react';
import { Link } from 'react-router';
import { apiFetch } from '../api/client';
import { errorMessage } from '../api/errors';

export function ChangePasswordPage() {
  const [current, setCurrent] = useState('');
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);
    setMessage(null);

    if (password !== confirm) {
      setError('Las contraseñas no coinciden.');
      return;
    }

    setBusy(true);

    try {
      await apiFetch<void>('/api/auth/change-password', {
        method: 'POST',
        body: JSON.stringify({ currentPassword: current, newPassword: password }),
      });

      setCurrent('');
      setPassword('');
      setConfirm('');
      setMessage('Contraseña cambiada.');
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
          <h1>Cambiar mi contraseña</h1>
        </div>
        <Link to="/" className="link">
          Volver al inicio
        </Link>
      </header>

      <form className="card" onSubmit={handleSubmit} noValidate>
        <label htmlFor="current-password">Contraseña actual</label>
        <input
          id="current-password"
          name="current-password"
          type="password"
          autoComplete="current-password"
          value={current}
          onChange={(e) => setCurrent(e.target.value)}
          required
        />

        <label htmlFor="new-password">Contraseña nueva</label>
        <input
          id="new-password"
          name="new-password"
          type="password"
          autoComplete="new-password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          required
        />
        <p className="hint">Mínimo 8 caracteres (10 si eres personal del colegio), distinta de la actual.</p>

        <label htmlFor="confirm-password">Repite la contraseña nueva</label>
        <input
          id="confirm-password"
          name="confirm-password"
          type="password"
          autoComplete="new-password"
          value={confirm}
          onChange={(e) => setConfirm(e.target.value)}
          required
        />

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
            {busy ? 'Guardando…' : 'Cambiar contraseña'}
          </button>
        </div>
      </form>
    </main>
  );
}