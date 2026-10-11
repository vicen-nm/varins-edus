import { useOnlineStatus } from './useOnlineStatus';

export function OfflineBanner() {
  const online = useOnlineStatus();

  if (online) {
    return null;
  }

  return (
    <div className="banner banner--warning" role="status">
      <p>Sin conexión. Puedes ver la aplicación, pero no cargar ni guardar datos.</p>
    </div>
  );
}