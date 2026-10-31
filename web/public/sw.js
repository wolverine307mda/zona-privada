// Service worker de la app instalada: abre al instante con lo último que se cargó y,
// sin conexión, muestra la app (que avisa de que el equipo no responde) en vez de la página de error del navegador.
// La API (/api, /hubs) nunca se guarda: los datos siempre son los del equipo.
const CACHE = 'mda-v3';
const MAX_ARCHIVOS = 120; // cada despliegue trae archivos nuevos con otra huella: sin tope la caché crecería sin parar

self.addEventListener('install', (e) => {
  e.waitUntil(caches.open(CACHE).then((c) => c.addAll(['/', '/manifest.webmanifest', '/apple-touch-icon.png'])).then(() => self.skipWaiting()));
});

self.addEventListener('activate', (e) => {
  e.waitUntil(caches.keys().then((ks) => Promise.all(ks.filter((k) => k !== CACHE).map((k) => caches.delete(k)))).then(() => self.clients.claim()));
});

self.addEventListener('fetch', (e) => {
  const req = e.request;
  const url = new URL(req.url);
  if (req.method !== 'GET' || url.origin !== location.origin || url.pathname.startsWith('/api/') || url.pathname.startsWith('/hubs/')) return;

  // páginas: primero la red (siempre la versión nueva); sin red, la app guardada
  if (req.mode === 'navigate') {
    e.respondWith(fetch(req).then(async (r) => {
      // solo se guarda una página buena; si el proxy contesta 5xx (el equipo está apagado), se enseña la app guardada
      if (r.ok) { const copia = r.clone(); caches.open(CACHE).then((c) => c.put('/', copia)); return r; }
      return r.status >= 500 ? (await caches.match('/')) ?? r : r;
    }).catch(() => caches.match('/')));
    return;
  }

  // archivos de la app (llevan huella en el nombre): de la caché y, en segundo plano, se refrescan
  e.respondWith(caches.open(CACHE).then(async (c) => {
    const guardado = await c.match(req);
    const red = fetch(req).then((r) => { if (r.ok) { c.put(req, r.clone()); recortar(c); } return r; }).catch(() => guardado);
    return guardado ?? red;
  }));
});

// deja solo los últimos archivos guardados (las claves salen en orden de inserción)
async function recortar(c) {
  const claves = await c.keys();
  const sobran = claves.filter((k) => new URL(k.url).pathname !== '/').length - MAX_ARCHIVOS;
  for (const k of claves) {
    if (sobran <= 0) break;
    if (new URL(k.url).pathname !== '/') { await c.delete(k); break; }
  }
}

// notificaciones de la app (recordatorios, presupuestos, coche, resumen semanal…)
self.addEventListener('push', (e) => {
  let d = {};
  try { d = e.data ? e.data.json() : {}; } catch { d = { texto: e.data ? e.data.text() : '' }; }
  e.waitUntil(self.registration.showNotification(d.titulo || 'Zona privada', {
    body: d.texto || '', icon: '/img/icono-192.png', badge: '/img/icono-192.png', data: { url: d.url || '/inicio' }, tag: d.titulo || undefined,
  }));
});

// al tocarla, abre (o enfoca) la app en la sección que toca
self.addEventListener('notificationclick', (e) => {
  e.notification.close();
  const url = new URL(e.notification.data?.url || '/inicio', self.location.origin).href;
  e.waitUntil(self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then((ventanas) => {
    const abierta = ventanas.find((v) => v.url.startsWith(self.location.origin));
    if (abierta) { abierta.navigate(url).catch(() => {}); return abierta.focus(); }
    return self.clients.openWindow(url);
  }));
});
