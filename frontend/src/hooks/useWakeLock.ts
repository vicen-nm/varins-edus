import { useEffect } from 'react';

// Keeps the screen on while the page is visible: a display must not go to sleep.
// Not every browser supports it: then it does nothing, and the device settings decide.
export function useWakeLock(active: boolean) {
  useEffect(() => {
    if (!active || !('wakeLock' in navigator)) {
      return;
    }

    let cancelled = false;
    let sentinel: WakeLockSentinel | null = null;

    async function acquire() {
      try {
        const lock = await navigator.wakeLock.request('screen');

        if (cancelled) {
          void lock.release();
          return;
        }

        void sentinel?.release();
        sentinel = lock;
      } catch {
        // Refused (for example low battery): nothing else to do.
      }
    }

    // The browser releases the lock when the page is hidden: ask again when it comes back.
    function handleVisibility() {
      if (!document.hidden) {
        void acquire();
      }
    }

    document.addEventListener('visibilitychange', handleVisibility);
    void acquire();

    return () => {
      cancelled = true;
      document.removeEventListener('visibilitychange', handleVisibility);
      void sentinel?.release();
    };
  }, [active]);
}