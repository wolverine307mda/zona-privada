import { Injectable, signal } from '@angular/core';

interface PeticionInstalar extends Event { prompt(): Promise<void>; userChoice: Promise<{ outcome: 'accepted' | 'dismissed' }>; }

// Chrome y Edge (Android y PC) avisan de que la app se puede instalar con «beforeinstallprompt»; puede llegar
// antes de que arranque Angular, así que se escucha nada más cargar el código y se guarda para cuando se pulse «Instalar».
let guardada: PeticionInstalar | null = null;
const avisar = new Set<() => void>();
addEventListener('beforeinstallprompt', (e) => { e.preventDefault(); guardada = e as PeticionInstalar; avisar.forEach((f) => f()); });
addEventListener('appinstalled', () => { guardada = null; avisar.forEach((f) => f()); });

// Instalar la app en la pantalla de inicio: en Android y en el PC con el diálogo del navegador; en el iPhone
// no hay diálogo (Safari no lo permite), así que se explican los dos toques de «Compartir › Añadir a pantalla de inicio».
@Injectable({ providedIn: 'root' })
export class Instalar {
  readonly instalada = signal(matchMedia('(display-mode: standalone)').matches || (navigator as Navigator & { standalone?: boolean }).standalone === true);
  readonly puede = signal(guardada != null);
  readonly ios = /iphone|ipad|ipod/i.test(navigator.userAgent) || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);
  readonly pasosIos = signal(false);

  constructor() {
    avisar.add(() => { this.puede.set(guardada != null); if (!guardada && matchMedia('(display-mode: standalone)').matches) this.instalada.set(true); });
  }

  /** Hay algo que ofrecer: el diálogo del navegador o los pasos del iPhone. */
  disponible(): boolean { return !this.instalada() && (this.puede() || this.ios); }

  async instalar(): Promise<void> {
    if (guardada) {
      const p = guardada;
      await p.prompt();
      const { outcome } = await p.userChoice;
      if (outcome === 'accepted') { guardada = null; this.puede.set(false); this.instalada.set(true); }
    } else this.pasosIos.set(true);
  }
}
