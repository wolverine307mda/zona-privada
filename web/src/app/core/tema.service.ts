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

export type FondoId = 'aura' | 'liquido' | 'isometrico' | 'comic' | 'pixel' | 'particulas' | 'ninguno';
/** dónde se ve el fondo: detrás de todo, solo de las páginas o solo del menú lateral (en el móvil siempre es todo) */
export type Ambito = 'ambos' | 'paginas' | 'menu';

export const FONDOS: { id: FondoId; nombre: string; nota: string }[] = [
  { id: 'aura', nombre: 'Aura', nota: 'Luz suave que respira' },
  { id: 'liquido', nombre: 'Líquido', nota: 'Gotas que se funden' },
  { id: 'isometrico', nombre: 'Isométrico', nota: 'Suelo de cubos en ondas' },
  { id: 'comic', nombre: 'Cómic', nota: 'Puntos de imprenta' },
  { id: 'pixel', nombre: 'Píxel art', nota: 'Estrellas y montañas a saltos' },
  { id: 'particulas', nombre: 'Partículas', nota: 'Red que huye del puntero' },
  { id: 'ninguno', nombre: 'Nada', nota: 'Solo el color liso' },
];

export const AMBITOS: { id: Ambito; nombre: string }[] = [
  { id: 'ambos', nombre: 'Todo' },
  { id: 'paginas', nombre: 'Páginas' },
  { id: 'menu', nombre: 'Menú' },
];

const leer = (k: string, d: string) => { try { return localStorage.getItem(k) ?? d; } catch { return d; } };
const guardar = (k: string, v: string) => { try { localStorage.setItem(k, v); } catch { /* sin almacenamiento */ } };

const entre = (n: number, min: number, max: number, defecto: number) => (Number.isFinite(n) ? Math.min(max, Math.max(min, Math.round(n))) : defecto);

// Tema como en la web pública: modo día / noche / sistema y color de marca, guardados en este navegador.
// cambio() avisa a quien pinta con los colores (gráficas, escena 3D) de que tiene que repintar.
@Injectable({ providedIn: 'root' })
export class TemaService {
  private readonly sistemaOscuro = matchMedia('(prefers-color-scheme: dark)');
  readonly modo = signal<Modo>(leer('mode', 'dark') as Modo);
  readonly color = signal<Color>(leer('color', 'naranja') as Color);
  readonly cambio = signal(0);

  // fondo animado de toda la app: cuál, cuánto se ve (0-100), cuánto se mueve (25-200 %) y dónde
  readonly fondo = signal<FondoId>(FONDOS.some((f) => f.id === leer('mda-fondo', 'aura')) ? (leer('mda-fondo', 'aura') as FondoId) : 'aura');
  readonly intensidad = signal(entre(+leer('mda-fondo-int', '60'), 5, 100, 60));
  readonly velocidad = signal(entre(+leer('mda-fondo-vel', '100'), 25, 200, 100));
  readonly ambito = signal<Ambito>(AMBITOS.some((a) => a.id === leer('mda-fondo-ambito', 'ambos')) ? (leer('mda-fondo-ambito', 'ambos') as Ambito) : 'ambos');

  constructor() {
    this.sistemaOscuro.addEventListener('change', () => { if (this.modo() === 'system') this.aplicar(); });
    this.aplicar();
  }

  ponerFondo(f: FondoId): void { this.fondo.set(f); guardar('mda-fondo', f); this.aplicar(); }
  ponerIntensidad(n: number): void { this.intensidad.set(entre(n, 5, 100, 60)); guardar('mda-fondo-int', String(this.intensidad())); }
  ponerVelocidad(n: number): void { this.velocidad.set(entre(n, 25, 200, 100)); guardar('mda-fondo-vel', String(this.velocidad())); }
  ponerAmbito(a: Ambito): void { this.ambito.set(a); guardar('mda-fondo-ambito', a); this.aplicar(); }

  ponerModo(m: Modo, origen?: HTMLElement): void { this.transicion(origen, () => { this.modo.set(m); guardar('mode', m); }); }
  ponerColor(c: Color, origen?: HTMLElement): void { this.transicion(origen, () => { this.color.set(c); guardar('color', c); }); }

  private aplicar(): void {
    const d = document.documentElement;
    const oscuro = this.modo() === 'dark' || (this.modo() === 'system' && this.sistemaOscuro.matches);
    d.dataset['mode'] = oscuro ? 'dark' : 'light';
    d.dataset['color'] = this.color();
    d.dataset['fondo'] = this.fondo();
    d.dataset['ambito'] = this.ambito();
    document.querySelector('meta[name="theme-color"]')?.setAttribute('content', oscuro ? '#070d12' : '#f4f2ee');
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
    if (doc.startViewTransition && !matchMedia('(prefers-reduced-motion: reduce)').matches) {
      const d = document.documentElement;
      d.classList.add('vt-tema');
      const t = doc.startViewTransition(run) as { finished?: Promise<unknown> };
      (t.finished ?? Promise.resolve()).finally(() => d.classList.remove('vt-tema'));
    } else run();
  }
}
