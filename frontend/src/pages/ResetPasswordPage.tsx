import { useState } from 'react';
import type { FormEvent } from 'react';
import { Link, useSearchParams } from 'react-router';
import { apiFetch } from '../api/client';
import { errorMessage } from '../api/errors';

export function ResetPasswordPage() {
  const [params] = useSearchParams();

  const [username, setUsername] = useState(params.get('usuario') ?? '');
  const [code, setCode] = useState('');
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [done, setDone] = useState(false);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);

    if (password !== confirm) {
      setError('Las contraseñas no coinciden.');
      return;
    }

    setBusy(true);

    try {
      await apiFetch<void>('/api/auth/reset-password', {
        method: 'POST',
        body: JSON.stringify({ username, code, newPassword: password }),
      });

      setDone(true);
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setBusy(false);
    }
  }

  if (done) {
    return (
      <main className="login">
        <section className="card login__form" aria-live="polite">
          <p className="eyebrow">VARINS · EDU</p>
          <h1>¡Listo!</h1>
          <p>Tu contraseña quedó cambiada. Ya puedes iniciar sesión con ella.</p>
          <p>
            <Link to="/login" className="link">
              Ir a iniciar sesión
            </Link>
          </p>
        </section>
      </main>
    );
  }

  return (
    <main className="login">
      <form className="card login__form" onSubmit={handleSubmit} noValidate>
        <p className="eyebrow">VARINS · EDU</p>
        <h1>Elegir una contraseña</h1>
        <p className="hint">Usa el código que te dieron en tu colegio. Si no lo tienes o ya no sirve, pide uno nuevo.</p>

        <label htmlFor="reset-username">Usuario</label>
        <input
          id="reset-username"
          name="username"
          autoComplete="username"
          spellCheck={false}
          value={username}
          onChange={(e) => setUsername(e.target.value)}
          required
        />

        <label htmlFor="reset-code">Código</label>
        <input
          id="reset-code"
          name="code"
          autoComplete="one-time-code"
          autoCapitalize="characters"
          spellCheck={false}
          value={code}
          onChange={(e) => setCode(e.target.value)}
          required
        />

        <label htmlFor="reset-password">Contraseña nueva</label>
        <input
          id="reset-password"
          name="password"
          type="password"
          autoComplete="new-password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          required
        />
        <p className="hint">Mínimo 8 caracteres (10 si eres personal del colegio).</p>

        <label htmlFor="reset-confirm">Repite la contraseña</label>
        <input
          id="reset-confirm"
          name="confirm"
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

        <button type="submit" disabled={busy}>
          {busy ? 'Guardando…' : 'Guardar contraseña'}
        </button>

        <p className="hint">
          <Link to="/login" className="link">
            Volver a iniciar sesión
          </Link>
        </p>
      </form>
    </main>
  );
}