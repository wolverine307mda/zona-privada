import { Injectable, signal } from '@angular/core';

export type Modo = 'light' | 'dark' | 'system';
export type Color = 'naranja' | 'lima' | 'cobalto' | 'rosa' | 'rojo' | 'amatista' | 'ultravioleta' | 'lavanda' | 'ciruela' | 'orquidea' | 'indigo';

/** Una paleta: el acento (`muestra`), su segundo tono y el color de las superficies en noche y en día, para enseñarla en Ajustes. Los valores reales viven en styles.css. */
export interface Paleta { id: Color; nombre: string; grupo: 'Morados' | 'Clásicos'; muestra: string; segundo: string; noche: string; dia: string; }

export const COLORES: Paleta[] = [
  { id: 'amatista', nombre: 'Amatista', grupo: 'Morados', muestra: '#8a4fd8', segundo: '#c9a6f5', noche: '#150d24', dia: '#f1ecf7' },
  { id: 'ultravioleta', nombre: 'Ultravioleta', grupo: 'Morados', muestra: '#5b3df5', segundo: '#a597ff', noche: '#120b2e', dia: '#eeeafc' },
  { id: 'lavanda', nombre: 'Lavanda', grupo: 'Morados', muestra: '#b79cff', segundo: '#e2d7ff', noche: '#14101f', dia: '#f3f0fa' },
  { id: 'ciruela', nombre: 'Ciruela', grupo: 'Morados', muestra: '#a23fc4', segundo: '#e0a4f0', noche: '#190b22', dia: '#f4ecf7' },
  { id: 'orquidea', nombre: 'Orquídea', grupo: 'Morados', muestra: '#e06cf5', segundo: '#f6bffc', noche: '#170e1d', dia: '#f7edf9' },
  { id: 'indigo', nombre: 'Índigo', grupo: 'Morados', muestra: '#5558e6', segundo: '#a9b2ff', noche: '#0e0f28', dia: '#ecedfb' },
  { id: 'naranja', nombre: 'Naranja', grupo: 'Clásicos', muestra: '#ff7a1f', segundo: '#ffbc88', noche: '#070d12', dia: '#f4f2ee' },
  { id: 'lima', nombre: 'Lima', grupo: 'Clásicos', muestra: '#c8f53c', segundo: '#e4fa9d', noche: '#070d12', dia: '#f4f2ee' },
  { id: 'cobalto', nombre: 'Cobalto', grupo: 'Clásicos', muestra: '#3355ff', segundo: '#99aaff', noche: '#070d12', dia: '#f4f2ee' },
  { id: 'rosa', nombre: 'Rosa', grupo: 'Clásicos', muestra: '#ff5fa2', segundo: '#ffafd0', noche: '#070d12', dia: '#f4f2ee' },
  { id: 'rojo', nombre: 'Rojo', grupo: 'Clásicos', muestra: '#e62429', segundo: '#f39194', noche: '#070d12', dia: '#f4f2ee' },
];

const COLOR_IDS = COLORES.map((c) => c.id);

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
  readonly color = signal<Color>(COLOR_IDS.includes(leer('color', 'naranja') as Color) ? (leer('color', 'naranja') as Color) : 'naranja');
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
    // la barra del navegador del móvil toma el color de fondo de la paleta (los morados lo tiñen)
    document.querySelector('meta[name="theme-color"]')?.setAttribute('content', getComputedStyle(d).backgroundColor || (oscuro ? '#070d12' : '#f4f2ee'));
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
