import { Injectable, computed, signal } from '@angular/core';
import { TEMA_POR_DEFECTO, TemaId, VARIABLES_TEMAS, esTema, temaDe } from './temas';

export type Modo = 'light' | 'dark' | 'system';
export type Color = 'morado' | 'rojo' | 'naranja' | 'rosa' | 'cobalto' | 'turquesa' | 'lima' | 'ambar';

/** Una paleta: un color con su gama de cinco tonos (del más claro al más hondo; el central es el acento) y cómo tiñe las superficies en noche y en día, para enseñarla en Ajustes.
 *  Los valores reales viven en styles.css (`[data-color=…]`). */
export interface Paleta { id: Color; nombre: string; tonos: [string, string, string, string, string]; noche: string; dia: string; }

export const COLORES: Paleta[] = [
  { id: 'morado', nombre: 'Morado', tonos: ['#e9ddff', '#b794f6', '#8a4fd8', '#5b2fa8', '#2a1458'], noche: '#07050a', dia: '#f1ecf7' },
  { id: 'rojo', nombre: 'Rojo', tonos: ['#ffd9d6', '#ff8a80', '#e0242b', '#a1141b', '#4a0a0e'], noche: '#0a0505', dia: '#f7efee' },
  { id: 'naranja', nombre: 'Naranja', tonos: ['#ffe3cc', '#ffb27a', '#ff7a1f', '#c24f00', '#5c2500'], noche: '#090605', dia: '#f6f1ea' },
  { id: 'ambar', nombre: 'Ámbar', tonos: ['#fff0c2', '#ffd36a', '#ffb300', '#b57800', '#5a3b00'], noche: '#090705', dia: '#f7f2e6' },
  { id: 'lima', nombre: 'Lima', tonos: ['#f2ffc7', '#d9fb7c', '#c8f53c', '#7ea60f', '#2e4005'], noche: '#070805', dia: '#f3f4e9' },
  { id: 'turquesa', nombre: 'Turquesa', tonos: ['#d3fbf4', '#6fe8d3', '#17c3b2', '#0e857a', '#053f3a'], noche: '#050808', dia: '#ecf4f2' },
  { id: 'cobalto', nombre: 'Azul', tonos: ['#dbe4ff', '#8fa6ff', '#3355ff', '#1f37b8', '#0d1a5c'], noche: '#05060a', dia: '#edf0fa' },
  { id: 'rosa', nombre: 'Rosa', tonos: ['#ffdcea', '#ff9ec7', '#ff5fa2', '#c2316f', '#5e1536'], noche: '#0a0508', dia: '#f7eef2' },
];

/** Los nombres de las primeras paletas (seis morados distintos): ahora hay un solo Morado con su gama. */
const ALIAS: Record<string, Color> = { amatista: 'morado', ultravioleta: 'morado', lavanda: 'morado', ciruela: 'morado', orquidea: 'morado', indigo: 'morado' };

const COLOR_IDS = COLORES.map((c) => c.id);

export const MODOS: { id: Modo; nombre: string }[] = [
  { id: 'light', nombre: 'Día' },
  { id: 'dark', nombre: 'Noche' },
  { id: 'system', nombre: 'Sistema' },
];

export type FondoId = 'aura' | 'liquido' | 'isometrico' | 'comic' | 'pixel' | 'particulas' | 'boreal' | 'bokeh' | 'olas' | 'estrellas' | 'rejilla' | 'ninguno'
  | 'nebulosa' | 'plasma' | 'seda' | 'lluvia' | 'luciernagas' | 'hexagonos' | 'circuitos' | 'caleidoscopio' | 'sonar' | 'flujo' | 'cristales' | 'nevada' | 'cometas' | 'haces'
  | 'multiverso' | 'reactor' | 'gamma' | 'tormenta' | 'mistico' | 'vibranio' | 'cosmico';
/** dónde se ve el fondo: detrás de todo, solo de las páginas o solo del menú lateral (en el móvil siempre es todo) */
export type Ambito = 'ambos' | 'paginas' | 'menu';

export type GrupoFondo = 'clasicos' | 'nuevos' | 'marvel';
export const GRUPOS_FONDO: { id: GrupoFondo; nombre: string; nota?: string }[] = [
  { id: 'clasicos', nombre: 'Clásicos' },
  { id: 'nuevos', nombre: 'Nuevos' },
  { id: 'marvel', nombre: 'Universo de superhéroes', nota: 'Inspirados en el universo Marvel, con dibujos originales y colores propios (no siguen el color de acento).' },
];

export const FONDOS: { id: FondoId; nombre: string; nota: string; grupo: GrupoFondo }[] = [
  { id: 'aura', nombre: 'Aura', nota: 'Luz suave que respira', grupo: 'clasicos' },
  { id: 'liquido', nombre: 'Líquido', nota: 'Gotas que se funden', grupo: 'clasicos' },
  { id: 'isometrico', nombre: 'Isométrico', nota: 'Suelo de cubos en ondas', grupo: 'clasicos' },
  { id: 'comic', nombre: 'Cómic', nota: 'Puntos de imprenta', grupo: 'clasicos' },
  { id: 'pixel', nombre: 'Píxel art', nota: 'Estrellas y montañas a saltos', grupo: 'clasicos' },
  { id: 'particulas', nombre: 'Partículas', nota: 'Red que huye del puntero', grupo: 'clasicos' },
  { id: 'boreal', nombre: 'Boreal', nota: 'Cintas de luz como una aurora', grupo: 'clasicos' },
  { id: 'bokeh', nombre: 'Bokeh', nota: 'Luces desenfocadas que suben', grupo: 'clasicos' },
  { id: 'olas', nombre: 'Olas', nota: 'Capas de ondas que se mecen', grupo: 'clasicos' },
  { id: 'estrellas', nombre: 'Estrellas', nota: 'Cielo con Vía Láctea y fugaces', grupo: 'clasicos' },
  { id: 'rejilla', nombre: 'Rejilla', nota: 'Suelo en perspectiva con sol', grupo: 'clasicos' },
  { id: 'ninguno', nombre: 'Nada', nota: 'Solo el color liso', grupo: 'clasicos' },
  { id: 'nebulosa', nombre: 'Nebulosa', nota: 'Nubes de luz y polvo de estrellas', grupo: 'nuevos' },
  { id: 'plasma', nombre: 'Plasma', nota: 'Mármol de luz líquida', grupo: 'nuevos' },
  { id: 'seda', nombre: 'Seda', nota: 'Hilos de luz que ondulan', grupo: 'nuevos' },
  { id: 'lluvia', nombre: 'Lluvia digital', nota: 'Columnas de luz que caen', grupo: 'nuevos' },
  { id: 'luciernagas', nombre: 'Luciérnagas', nota: 'Luces cálidas que parpadean', grupo: 'nuevos' },
  { id: 'hexagonos', nombre: 'Hexágonos', nota: 'Panal con ondas de luz', grupo: 'nuevos' },
  { id: 'circuitos', nombre: 'Circuitos', nota: 'Pistas con pulsos de luz', grupo: 'nuevos' },
  { id: 'caleidoscopio', nombre: 'Caleidoscopio', nota: 'Pétalos de luz que giran', grupo: 'nuevos' },
  { id: 'sonar', nombre: 'Sonar', nota: 'Ondas que se cruzan', grupo: 'nuevos' },
  { id: 'flujo', nombre: 'Flujo', nota: 'Estelas en una corriente', grupo: 'nuevos' },
  { id: 'cristales', nombre: 'Cristales', nota: 'Malla de facetas brillantes', grupo: 'nuevos' },
  { id: 'nevada', nombre: 'Nevada', nota: 'Copos en tres capas', grupo: 'nuevos' },
  { id: 'cometas', nombre: 'Cometas', nota: 'Bólidos con larga cola', grupo: 'nuevos' },
  { id: 'haces', nombre: 'Haces', nota: 'Focos que barren el aire', grupo: 'nuevos' },
  { id: 'multiverso', nombre: 'Multiverso', nota: 'Telaraña partida en rojo y azul', grupo: 'marvel' },
  { id: 'reactor', nombre: 'Reactor', nota: 'Anillos de energía y mira de combate', grupo: 'marvel' },
  { id: 'gamma', nombre: 'Gamma', nota: 'Energía verde con venas violeta', grupo: 'marvel' },
  { id: 'tormenta', nombre: 'Tormenta', nota: 'Rayos que iluminan el cielo', grupo: 'marvel' },
  { id: 'mistico', nombre: 'Místico', nota: 'Portal de runas y chispas', grupo: 'marvel' },
  { id: 'vibranio', nombre: 'Vibranio', nota: 'Panal de metal con energía violeta', grupo: 'marvel' },
  { id: 'cosmico', nombre: 'Cósmico', nota: 'Seis gemas orbitando una nebulosa', grupo: 'marvel' },
];

export const AMBITOS: { id: Ambito; nombre: string }[] = [
  { id: 'ambos', nombre: 'Todo' },
  { id: 'paginas', nombre: 'Páginas' },
  { id: 'menu', nombre: 'Menú' },
];

const colorGuardado = (v: string): Color => { const c = ALIAS[v] ?? v; return COLOR_IDS.includes(c as Color) ? (c as Color) : 'naranja'; };
const leer = (k: string, d: string) => { try { return localStorage.getItem(k) ?? d; } catch { return d; } };
const guardar = (k: string, v: string) => { try { localStorage.setItem(k, v); } catch { /* sin almacenamiento */ } };

const entre = (n: number, min: number, max: number, defecto: number) => (Number.isFinite(n) ? Math.min(max, Math.max(min, Math.round(n))) : defecto);

// Tema como en la web pública: modo día / noche / sistema y color de marca, guardados en este navegador.
// cambio() avisa a quien pinta con los colores (gráficas, escena 3D) de que tiene que repintar.
@Injectable({ providedIn: 'root' })
export class TemaService {
  private readonly sistemaOscuro = matchMedia('(prefers-color-scheme: dark)');
  readonly modo = signal<Modo>(leer('mode', 'dark') as Modo);
  readonly color = signal<Color>(colorGuardado(leer('color', 'naranja')));
  readonly cambio = signal(0);
  // el tema completo (fondo, superficies, texto y fuente): «cristal» es el de siempre; los otros fijan su propio modo claro u oscuro
  readonly tema = signal<TemaId>(esTema(leer('mda-tema', TEMA_POR_DEFECTO)) ? (leer('mda-tema', TEMA_POR_DEFECTO) as TemaId) : TEMA_POR_DEFECTO);
  readonly modoFijo = computed(() => temaDe(this.tema()).modo);

  // fondo animado de toda la app: cuál, cuánto se ve (0-100), cuánto se mueve (25-200 %) y dónde
  readonly fondo = signal<FondoId>(FONDOS.some((f) => f.id === leer('mda-fondo', 'aura')) ? (leer('mda-fondo', 'aura') as FondoId) : 'aura');
  readonly intensidad = signal(entre(+leer('mda-fondo-int', '60'), 5, 100, 60));
  readonly velocidad = signal(entre(+leer('mda-fondo-vel', '100'), 25, 200, 100));
  // cuánta fuerza tiene el color del fondo (50-200 %): más saturado y más opaco
  readonly fuerza = signal(entre(+leer('mda-fondo-col', '100'), 50, 200, 100));
  readonly ambito = signal<Ambito>(AMBITOS.some((a) => a.id === leer('mda-fondo-ambito', 'ambos')) ? (leer('mda-fondo-ambito', 'ambos') as Ambito) : 'ambos');

  constructor() {
    this.sistemaOscuro.addEventListener('change', () => { if (this.modo() === 'system') this.aplicar(); });
    this.aplicar();
  }

  ponerFondo(f: FondoId): void { this.fondo.set(f); guardar('mda-fondo', f); this.aplicar(); }
  ponerIntensidad(n: number): void { this.intensidad.set(entre(n, 5, 100, 60)); guardar('mda-fondo-int', String(this.intensidad())); }
  ponerVelocidad(n: number): void { this.velocidad.set(entre(n, 25, 200, 100)); guardar('mda-fondo-vel', String(this.velocidad())); }
  ponerFuerza(n: number): void { this.fuerza.set(entre(n, 50, 200, 100)); guardar('mda-fondo-col', String(this.fuerza())); }
  ponerAmbito(a: Ambito): void { this.ambito.set(a); guardar('mda-fondo-ambito', a); this.aplicar(); }

  ponerTema(t: TemaId, origen?: HTMLElement): void { this.transicion(origen, () => { this.tema.set(t); guardar('mda-tema', t); }); }

  ponerModo(m: Modo, origen?: HTMLElement): void { this.transicion(origen, () => { this.modo.set(m); guardar('mode', m); }); }
  ponerColor(c: Color, origen?: HTMLElement): void { this.transicion(origen, () => { this.color.set(c); guardar('color', c); }); }

  private aplicar(): void {
    const d = document.documentElement;
    const t = temaDe(this.tema());
    const oscuro = t.modo ? t.modo === 'dark' : this.modo() === 'dark' || (this.modo() === 'system' && this.sistemaOscuro.matches);
    d.dataset['mode'] = oscuro ? 'dark' : 'light';
    d.dataset['tema'] = t.id;
    // las variables del tema van sobre <html>; al cambiar de tema se quitan las del anterior
    for (const v of VARIABLES_TEMAS) d.style.removeProperty(v);
    for (const [v, valor] of Object.entries(t.vars)) d.style.setProperty(v, valor);
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
