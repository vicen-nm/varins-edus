import { Navigate, Outlet } from 'react-router';
import { useAuth } from './useAuth';

// Wraps the pages that need a session: without one, go to the login.
export function RequireAuth() {
  const { user, ready } = useAuth();

  if (!ready) {
    return (
      <main className="login">
        <p role="status">Cargando…</p>
      </main>
    );
  }

  return user ? <Outlet /> : <Navigate to="/login" replace />;
}