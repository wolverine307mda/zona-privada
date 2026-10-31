import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export type EstadoPush = 'cargando' | 'activas' | 'inactivas' | 'denegadas' | 'instalar' | 'no-soportadas';

// Notificaciones de la app en este dispositivo (Web Push): pedir permiso, suscribirse y darse de baja.
// En el iPhone solo funcionan con la app añadida a la pantalla de inicio (lo exige Apple).
@Injectable({ providedIn: 'root' })
export class Notificaciones {
  private readonly http = inject(HttpClient);
  readonly estado = signal<EstadoPush>('cargando');

  private get ios(): boolean { return /iphone|ipad|ipod/i.test(navigator.userAgent) || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1); }
  private get instalada(): boolean { return matchMedia('(display-mode: standalone)').matches || (navigator as Navigator & { standalone?: boolean }).standalone === true; }

  async comprobar(): Promise<void> {
    if (!('serviceWorker' in navigator) || !('PushManager' in window) || !('Notification' in window)) {
      this.estado.set(this.ios && !this.instalada ? 'instalar' : 'no-soportadas');
      return;
    }
    if (Notification.permission === 'denied') { this.estado.set('denegadas'); return; }
    const reg = await navigator.serviceWorker.getRegistration();
    const sub = await reg?.pushManager.getSubscription();
    this.estado.set(sub ? 'activas' : 'inactivas');
  }

  async activar(): Promise<boolean> {
    const permiso = await Notification.requestPermission();
    if (permiso !== 'granted') { this.estado.set(permiso === 'denied' ? 'denegadas' : 'inactivas'); return false; }
    const reg = await navigator.serviceWorker.ready;
    const { clave } = await firstValueFrom(this.http.get<{ clave: string }>('/api/push/clave'));
    const sub = await reg.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: aBytes(clave) });
    const j = sub.toJSON();
    await firstValueFrom(this.http.post('/api/push', { endpoint: j.endpoint, p256dh: j.keys?.['p256dh'], auth: j.keys?.['auth'] }));
    this.estado.set('activas');
    return true;
  }

  async desactivar(): Promise<void> {
    const reg = await navigator.serviceWorker.getRegistration();
    const sub = await reg?.pushManager.getSubscription();
    if (sub) {
      await firstValueFrom(this.http.delete('/api/push', { params: { endpoint: sub.endpoint } })).catch(() => {});
      await sub.unsubscribe();
    }
    this.estado.set('inactivas');
  }
}

// la clave pública VAPID viene en base64url
function aBytes(b64: string): Uint8Array<ArrayBuffer> {
  const relleno = '='.repeat((4 - (b64.length % 4)) % 4);
  const bin = atob((b64 + relleno).replace(/-/g, '+').replace(/_/g, '/'));
  const out = new Uint8Array(new ArrayBuffer(bin.length));
  for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
  return out;
}
