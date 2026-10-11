import { useRegisterSW } from 'virtual:pwa-register/react';

export function UpdatePrompt() {
  const {
    needRefresh: [needRefresh, setNeedRefresh],
    updateServiceWorker,
  } = useRegisterSW();

  if (!needRefresh) {
    return null;
  }

  return (
    <div className="banner" role="status">
      <p>Hay una versión nueva de la aplicación.</p>
      <button type="button" onClick={() => updateServiceWorker(true)}>
        Actualizar
      </button>
      <button type="button" className="secondary" onClick={() => setNeedRefresh(false)}>
        Más tarde
      </button>
    </div>
  );
}