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
  const habiaControlador = !!navigator.serviceWorker.controller;
  addEventListener('load', () => navigator.serviceWorker.register('/sw.js').catch(() => { /* sin service worker, la app funciona igual */ }));
  // un service worker nuevo toma el control tras un despliegue: la página abierta sigue con el código viejo hasta recargar
  navigator.serviceWorker.addEventListener('controllerchange', () => {
    if (!habiaControlador || document.getElementById('aviso-version')) return;
    const aviso = document.createElement('div');
    aviso.id = 'aviso-version';
    aviso.setAttribute('role', 'status');
    aviso.style.cssText = 'position:fixed;left:50%;bottom:calc(env(safe-area-inset-bottom,0px) + 84px);transform:translateX(-50%);z-index:9999;display:flex;gap:12px;align-items:center;padding:10px 14px;border-radius:999px;background:var(--tarjeta,#111);color:var(--texto,#fff);border:1px solid var(--brand,#f60);box-shadow:0 8px 30px rgba(0,0,0,.4);font:500 14px/1 Switzer,system-ui,sans-serif';
    const texto = document.createElement('span');
    texto.textContent = 'Hay una versión nueva';
    const boton = document.createElement('button');
    boton.textContent = 'Recargar';
    boton.style.cssText = 'border:0;border-radius:999px;padding:8px 14px;background:var(--brand,#f60);color:var(--on-brand,#000);font:inherit;font-weight:700;cursor:pointer';
    boton.onclick = () => location.reload();
    aviso.append(texto, boton);
    document.body.append(aviso);
  });
}
