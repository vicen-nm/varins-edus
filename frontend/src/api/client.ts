import { saveSession, storedToken } from '../auth/session';
import type { LoginResponse } from './types';

// Empty means "same origin", which is what we use in production and in development (Vite proxy).
// The renewal cookie is only sent to the same origin.
const API_URL = (import.meta.env.VITE_API_URL as string | undefined) ?? '';

// Fired when the session cannot be renewed any more, so the app can close it.
export const UNAUTHORIZED_EVENT = 'varinsedu:unauthorized';

// Fired when the session was renewed, so the app can use the new token.
export const SESSION_RENEWED_EVENT = 'varinsedu:session-renewed';

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

async function toApiError(response: Response): Promise<ApiError> {
  const problem = await readProblem(response);

  return new ApiError(
    response.status,
    problem.code ?? `http_${response.status}`,
    problem.title ?? response.statusText,
    problem.traceId,
  );
}

// "ended": the server refused the cookie. "network": a transient problem, so the session is kept.
export type RenewResult = { ok: true; session: LoginResponse } | { ok: false; reason: 'ended' | 'network' };

const RENEW_LOCK = 'varinsedu-renew';
const CONFLICT_RETRY_MS = 1000;

// The cookie is shared by every tab: only one renewal at a time, so two tabs never use the same cookie together.
function withLock(task: () => Promise<RenewResult>): Promise<RenewResult> {
  return 'locks' in navigator ? navigator.locks.request(RENEW_LOCK, task) : task();
}

async function renewOnce(): Promise<RenewResult> {
  for (let attempt = 0; attempt < 2; attempt++) {
    let response: Response;

    try {
      response = await fetch(`${API_URL}/api/auth/refresh`, {
        method: 'POST',
        headers: { Accept: 'application/json' },
      });
    } catch {
      return { ok: false, reason: 'network' };
    }

    if (response.ok) {
      try {
        const session = (await response.json()) as LoginResponse;

        saveSession(session);
        window.dispatchEvent(new CustomEvent<LoginResponse>(SESSION_RENEWED_EVENT, { detail: session }));

        return { ok: true, session };
      } catch {
        return { ok: false, reason: 'network' };
      }
    }

    if (response.status === 401) {
      return { ok: false, reason: 'ended' };
    }

    // Two renewals crossed: wait a moment and try with the cookie the other one just set.
    if (response.status === 409 && attempt === 0) {
      await new Promise((resolve) => window.setTimeout(resolve, CONFLICT_RETRY_MS));
    }
  }

  return { ok: false, reason: 'network' };
}

let renewing: Promise<RenewResult> | null = null;

// Many requests can fail at once: they all share one renewal.
export function renewSession(): Promise<RenewResult> {
  renewing ??= withLock(renewOnce).finally(() => {
    renewing = null;
  });

  return renewing;
}

// Closes the session on the server (it also clears the cookie). The tab closes its own anyway.
export async function serverLogout(): Promise<void> {
  try {
    await fetch(`${API_URL}/api/auth/logout`, { method: 'POST' });
  } catch {
    // Nothing to do: the session of this tab is closed regardless.
  }
}

async function send(path: string, init: RequestInit, accept: string) {
  const headers = new Headers(init.headers);
  headers.set('Accept', accept);

  // A multipart body (FormData) must keep the boundary the browser sets itself.
  if (init.body && !(init.body instanceof FormData)) {
    headers.set('Content-Type', 'application/json');
  }

  const sentToken = storedToken();
  if (sentToken) {
    headers.set('Authorization', `Bearer ${sentToken}`);
  }

  try {
    return { response: await fetch(`${API_URL}${path}`, { ...init, headers }), sentToken };
  } catch {
    throw new ApiError(0, 'network_error', 'Could not reach the server.');
  }
}

// A 401 with a token: renew the session once and repeat the request.
async function sendWithRenewal(path: string, init: RequestInit, accept: string): Promise<Response> {
  const first = await send(path, init, accept);

  if (first.response.status !== 401 || !first.sentToken) {
    return first.response;
  }

  const renewed = await renewSession();

  if (renewed.ok) {
    const second = await send(path, init, accept);

    if (second.response.status === 401) {
      window.dispatchEvent(new Event(UNAUTHORIZED_EVENT));
    }

    return second.response;
  }

  if (renewed.reason === 'network') {
    throw new ApiError(0, 'network_error', 'Could not reach the server.');
  }

  window.dispatchEvent(new Event(UNAUTHORIZED_EVENT));

  return first.response;
}

export async function apiFetch<T>(path: string, init: RequestInit = {}): Promise<T> {
  const response = await sendWithRenewal(path, init, 'application/json');

  if (!response.ok) {
    throw await toApiError(response);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

// Downloads a file the API protects with the session token (a plain link would not send it).
export async function apiDownload(path: string): Promise<Blob> {
  const response = await sendWithRenewal(path, {}, '*/*');

  if (!response.ok) {
    throw await toApiError(response);
  }

  return response.blob();
}