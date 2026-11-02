import { useEffect, useState } from 'react';

type State<T> = { source: unknown; value: T | null; error: unknown };

// Calls `load` right away and then every `intervalMs`, only while the page is visible.
// Hidden page: the value is cleared (an expired code must never wait on screen) and nothing is requested.
// Only one chain of requests runs, however often the page is hidden and shown again.
// `load` must keep its identity between renders (a module function or useMemo/useCallback):
// a different `load` starts over from an empty value. Pass null to do nothing.
export function useRefreshingValue<T>(load: (() => Promise<T>) | null, intervalMs: number) {
  const [state, setState] = useState<State<T>>({ source: null, value: null, error: null });

  useEffect(() => {
    if (!load) {
      return;
    }

    const fetchValue = load;
    let cancelled = false;
    let running = false;
    let timer: number | undefined;

    async function run() {
      window.clearTimeout(timer);

      if (cancelled || running || document.hidden) {
        return;
      }

      running = true;

      try {
        const value = await fetchValue();

        if (!cancelled && !document.hidden) {
          setState({ source: load, value, error: null });
        }
      } catch (e) {
        if (!cancelled && !document.hidden) {
          setState({ source: load, value: null, error: e });
        }
      } finally {
        running = false;
      }

      if (!cancelled && !document.hidden) {
        timer = window.setTimeout(run, intervalMs);
      }
    }

    function handleVisibility() {
      if (document.hidden) {
        window.clearTimeout(timer);
        setState((current) => ({ ...current, value: null }));
      } else {
        void run();
      }
    }

    document.addEventListener('visibilitychange', handleVisibility);
    void run();

    return () => {
      cancelled = true;
      window.clearTimeout(timer);
      document.removeEventListener('visibilitychange', handleVisibility);
    };
  }, [load, intervalMs]);

  // A value that came from another `load` is not shown.
  const current = state.source === load ? state : null;

  return { value: current?.value ?? null, error: current?.error ?? null };
}