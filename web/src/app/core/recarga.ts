import { DestroyRef, Injectable, signal } from '@angular/core';

// Recargar los datos de la pantalla: con el botón de la barra, al volver a la app tras un rato fuera
// y, con la pestaña a la vista, cada dos minutos. Cada pantalla se apunta con alRecargar(fn).
const CADA = 120_000;
const TRAS_VOLVER = 30_000;

@Injectable({ providedIn: 'root' })
export class RecargaService {
  readonly girando = signal(false);
  private readonly oyentes = new Set<() => unknown>();
  private oculta = 0;

  constructor() {
    setInterval(() => { if (!document.hidden) this.recargar(); }, CADA);
    document.addEventListener('visibilitychange', () => {
      if (document.hidden) this.oculta = Date.now();
      else if (this.oculta && Date.now() - this.oculta > TRAS_VOLVER) this.recargar();
    });
  }

  // se da de baja sola al destruirse quien se apunta
  alRecargar(fn: () => unknown, destruir: DestroyRef): void {
    this.oyentes.add(fn);
    destruir.onDestroy(() => this.oyentes.delete(fn));
  }

  async recargar(): Promise<void> {
    if (this.girando()) return;
    this.girando.set(true);
    const inicio = Date.now();
    await Promise.allSettled([...this.oyentes].map(async (fn) => fn()));
    // que la vuelta del icono se vea aunque la respuesta sea instantánea
    setTimeout(() => this.girando.set(false), Math.max(0, 600 - (Date.now() - inicio)));
  }
}
