// El número sobre el icono de la app instalada (Badging API; el iPhone lo permite desde iOS 16.4 con la app en el inicio y las notificaciones permitidas).
// Lo pone el service worker al llegar un aviso (cuenta las notificaciones sin leer) y se quita en cuanto abres la app.
export function limpiarGloboAlAbrir(): void {
  const nav = navigator as Navigator & { clearAppBadge?: () => Promise<void> };
  if (typeof nav.clearAppBadge !== 'function') return;
  const limpiar = () => { if (!document.hidden) void nav.clearAppBadge!().catch(() => {}); };
  document.addEventListener('visibilitychange', limpiar);
  limpiar();
}
