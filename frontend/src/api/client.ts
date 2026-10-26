import { loadSession } from '../auth/session';

// Empty means "same origin", which is what we will use in production.
const API_URL = (import.meta.env.VITE_API_URL as string | undefined) ?? '';

// Fired when the API rejects a token we sent, so the app can close the session.
export const UNAUTHORIZED_EVENT = 'varinsedu:unauthorized';

export class ApiError extends Error {
  status: number;
  code: string;
  traceId?: string;

  constructor(status: number, code: string, message: string, traceId?: string) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.code = code;
    this.traceId = traceId;
  }
}

type Problem = { title?: string; code?: string; traceId?: string };

async function readProblem(response: Response): Promise<Problem> {
  try {
    return (await response.json()) as Problem;
  } catch {
    return {};
  }
}

export async function apiFetch<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers);
  headers.set('Accept', 'application/json');

  // A multipart body (FormData) must keep the boundary the browser sets itself.
  if (init.body && !(init.body instanceof FormData)) {
    headers.set('Content-Type', 'application/json');
  }

  const token = loadSession()?.accessToken ?? null;
  if (token) {
    headers.set('Authorization', `Bearer ${token}`);
  }

  let response: Response;
  try {
    response = await fetch(`${API_URL}${path}`, { ...init, headers });
  } catch {
    throw new ApiError(0, 'network_error', 'Could not reach the server.');
  }

  if (response.status === 401 && token) {
    window.dispatchEvent(new Event(UNAUTHORIZED_EVENT));
  }

  if (!response.ok) {
    const problem = await readProblem(response);
    throw new ApiError(
      response.status,
      problem.code ?? `http_${response.status}`,
      problem.title ?? response.statusText,
      problem.traceId,
    );
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}