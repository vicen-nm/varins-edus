import { useState } from 'react';
import type { FormEvent } from 'react';
import { useNavigate, useSearchParams } from 'react-router';
import { errorMessage } from '../api/errors';
import { useAuth } from '../auth/useAuth';

export function PairDevicePage() {
  const { pairDevice } = useAuth();
  const navigate = useNavigate();
  const [params] = useSearchParams();

  const [username, setUsername] = useState(params.get('usuario') ?? '');
  const [code, setCode] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);
    setBusy(true);

    try {
      await pairDevice(username, code);
      navigate('/access/screen', { replace: true });
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <main className="login">
      <form className="card login__form" onSubmit={handleSubmit} noValidate>
        <p className="eyebrow">VARINS · EDU</p>
        <h1>Emparejar pantalla</h1>
        <p className="hint">
          Usa el código que generó el colegio para esta pantalla. Vale 1 hora y se usa una sola vez.
        </p>

        <label htmlFor="pair-username">Usuario de la pantalla</label>
        <input
          id="pair-username"
          name="username"
          autoComplete="off"
          spellCheck={false}
          value={username}
          onChange={(e) => setUsername(e.target.value)}
          required
        />

        <label htmlFor="pair-code">Código</label>
        <input
          id="pair-code"
          name="code"
          autoComplete="off"
          autoCapitalize="characters"
          spellCheck={false}
          value={code}
          onChange={(e) => setCode(e.target.value)}
          required
        />

        {error && (
          <p role="alert" className="error">
            {error}
          </p>
        )}

        <button type="submit" disabled={busy}>
          {busy ? 'Emparejando…' : 'Emparejar'}
        </button>
      </form>
    </main>
  );
}