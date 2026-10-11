import { Navigate, Outlet } from 'react-router';
import { useAuth } from './useAuth';

// Wraps the pages that need a session: without one, go to the login.
export function RequireAuth() {
  const { user } = useAuth();

  return user ? <Outlet /> : <Navigate to="/login" replace />;
}