import { isDevMode } from '@angular/core';
import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';

// tema guardado antes de pintar nada (la CSP no permite scripts en línea en index.html)
try {
  const d = document.documentElement;
  const modo = localStorage.getItem('mode') ?? 'dark';
  const oscuro = modo === 'dark' || (modo === 'system' && matchMedia('(prefers-color-scheme: dark)').matches);
  d.dataset['mode'] = oscuro ? 'dark' : 'light';
  d.dataset['color'] = localStorage.getItem('color') ?? 'naranja';
} catch { /* sin almacenamiento: tema por defecto */ }

bootstrapApplication(App, appConfig)
  .catch((err) => console.error(err));

// app instalable: el service worker solo en la versión compilada (en desarrollo estorbaría a la recarga en caliente)
if (!isDevMode() && 'serviceWorker' in navigator) {
  addEventListener('load', () => navigator.serviceWorker.register('/sw.js').catch(() => { /* sin service worker, la app funciona igual */ }));
}
