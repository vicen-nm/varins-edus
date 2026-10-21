import { useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { Navigate, useNavigate, useParams } from 'react-router';
import { ApiError, apiFetch } from '../api/client';
import { errorMessage } from '../api/errors';
import type { PublicInstitution } from '../api/types';
import { useAuth } from '../auth/useAuth';
import { useInstitution } from '../institution/useInstitution';

export function ActivatePage() {
  const { slug = '' } = useParams();
  const { user, activate } = useAuth();
  const { setPublicBranding } = useInstitution();
  const navigate = useNavigate();

  const [school, setSchool] = useState<PublicInstitution | null>(null);
  const [notFound, setNotFound] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);

  const [identification, setIdentification] = useState('');
  const [code, setCode] = useState('');
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [activated, setActivated] = useState(false);

  // Look like the school while the student is still logged out.
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

  if (activated && user) {
    return (
      <main className="login">
        <section className="card login__form" aria-live="polite">
          <p className="eyebrow">VARINS · EDU</p>
          <h1>¡Listo!</h1>
          <p>Tu cuenta está activa. Tu usuario para entrar es:</p>
          <p>
            <code>{user.username}</code>
          </p>
          <p className="hint">Anótalo o guárdalo en tu teléfono: lo necesitarás la próxima vez.</p>
          <button type="button" onClick={() => navigate('/', { replace: true })}>
            Entrar a la aplicación
          </button>
        </section>
      </main>
    );
  }

  if (user) {
    return <Navigate to="/" replace />;
  }

  if (notFound) {
    return (
      <main className="login">
        <section className="card login__form">
          <p className="eyebrow">VARINS · EDU</p>
          <h1>No encontramos este colegio</h1>
          <p>Revisa el enlace que te dieron o pregunta en tu colegio.</p>
        </section>
      </main>
    );
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);

    if (password !== confirm) {
      setError('Las contraseñas no coinciden.');
      return;
    }

    setBusy(true);

    try {
      await activate({
        institutionSlug: slug,
        identificationNumber: identification,
        code,
        password,
      });
      setActivated(true);
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
        <h1>Activa tu cuenta</h1>
        <p>{school ? school.name : (loadError ?? 'Cargando…')}</p>

        <label htmlFor="identification">Cédula</label>
        <input
          id="identification"
          name="identification"
          autoComplete="off"
          value={identification}
          onChange={(e) => setIdentification(e.target.value)}
          required
        />

        <label htmlFor="code">Código de activación</label>
        <input
          id="code"
          name="code"
          autoComplete="one-time-code"
          autoCapitalize="characters"
          spellCheck={false}
          value={code}
          onChange={(e) => setCode(e.target.value)}
          required
        />

        <label htmlFor="password">Contraseña nueva</label>
        <input
          id="password"
          name="password"
          type="password"
          autoComplete="new-password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          required
        />

        <label htmlFor="confirm">Repite la contraseña</label>
        <input
          id="confirm"
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

        <button type="submit" disabled={busy || !school}>
          {busy ? 'Activando…' : 'Activar mi cuenta'}
        </button>
      </form>
    </main>
  );
}