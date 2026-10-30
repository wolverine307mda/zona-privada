import { Injectable, signal } from '@angular/core';

export type Modo = 'light' | 'dark' | 'system';
export type Color = 'naranja' | 'lima' | 'cobalto' | 'rosa' | 'rojo';

export const COLORES: { id: Color; nombre: string; muestra: string }[] = [
  { id: 'naranja', nombre: 'Naranja', muestra: '#ff7a1f' },
  { id: 'lima', nombre: 'Lima', muestra: '#c8f53c' },
  { id: 'cobalto', nombre: 'Cobalto', muestra: '#3355ff' },
  { id: 'rosa', nombre: 'Rosa', muestra: '#ff5fa2' },
  { id: 'rojo', nombre: 'Rojo', muestra: '#e62429' },
];

export const MODOS: { id: Modo; nombre: string }[] = [
  { id: 'light', nombre: 'Día' },
  { id: 'dark', nombre: 'Noche' },
  { id: 'system', nombre: 'Sistema' },
];

const leer = (k: string, d: string) => { try { return localStorage.getItem(k) ?? d; } catch { return d; } };
const guardar = (k: string, v: string) => { try { localStorage.setItem(k, v); } catch { /* sin almacenamiento */ } };

// Tema como en la web pública: modo día / noche / sistema y color de marca, guardados en este navegador.
// cambio() avisa a quien pinta con los colores (gráficas, escena 3D) de que tiene que repintar.
@Injectable({ providedIn: 'root' })
export class TemaService {
  private readonly sistemaOscuro = matchMedia('(prefers-color-scheme: dark)');
  readonly modo = signal<Modo>(leer('mode', 'dark') as Modo);
  readonly color = signal<Color>(leer('color', 'naranja') as Color);
  readonly cambio = signal(0);

  constructor() {
    this.sistemaOscuro.addEventListener('change', () => { if (this.modo() === 'system') this.aplicar(); });
    this.aplicar();
  }

  ponerModo(m: Modo, origen?: HTMLElement): void { this.transicion(origen, () => { this.modo.set(m); guardar('mode', m); }); }
  ponerColor(c: Color, origen?: HTMLElement): void { this.transicion(origen, () => { this.color.set(c); guardar('color', c); }); }

  private aplicar(): void {
    const d = document.documentElement;
    const oscuro = this.modo() === 'dark' || (this.modo() === 'system' && this.sistemaOscuro.matches);
    d.dataset['mode'] = oscuro ? 'dark' : 'light';
    d.dataset['color'] = this.color();
    this.cambio.update((n) => n + 1);
  }

  // el tema nuevo se abre en círculo desde el botón pulsado (View Transitions), como en la web
  private transicion(origen: HTMLElement | undefined, cambiar: () => void): void {
    const run = () => { cambiar(); this.aplicar(); };
    const doc = document as Document & { startViewTransition?: (f: () => void) => unknown };
    if (origen) {
      const r = origen.getBoundingClientRect();
      document.documentElement.style.setProperty('--vt-x', `${r.left + r.width / 2}px`);
      document.documentElement.style.setProperty('--vt-y', `${r.top + r.height / 2}px`);
    }
    if (doc.startViewTransition && !matchMedia('(prefers-reduced-motion: reduce)').matches) doc.startViewTransition(run);
    else run();
  }
}
