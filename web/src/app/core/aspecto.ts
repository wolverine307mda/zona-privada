// Tipografía y densidad, por dispositivo: el tamaño de la letra (90–130 %), cuánto aire hay entre las cosas (compacta · cómoda · amplia) y la fuente.
// Las cuentas son puras (`variablesAspecto`) para poder probarlas; TemaService las pone sobre <html> y guarda los tres ajustes en este navegador.
export type Densidad = 'compacta' | 'comoda' | 'amplia';
export type Fuente = 'siempre' | 'sistema' | 'lectura';

export const ESCALA_MIN = 90, ESCALA_MAX = 130, ESCALA_POR_DEFECTO = 100;
export const DENSIDADES: { id: Densidad; nombre: string; factor: number }[] = [
  { id: 'compacta', nombre: 'Compacta', factor: 0.8 }, { id: 'comoda', nombre: 'Cómoda', factor: 1 }, { id: 'amplia', nombre: 'Amplia', factor: 1.2 },
];
export const FUENTES: { id: Fuente; nombre: string; nota: string; texto: string | null }[] = [
  { id: 'siempre', nombre: 'La de siempre', nota: 'Switzer, la de la app', texto: null },
  { id: 'sistema', nombre: 'Del sistema', nota: 'la de tu móvil u ordenador', texto: "system-ui, -apple-system, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif" },
  { id: 'lectura', nombre: 'De lectura', nota: 'con serifas, cómoda para textos largos', texto: "Charter, 'Bitstream Charter', 'Sitka Text', Cambria, Georgia, serif" },
];
// los espacios de la escala (--esp-1…7) en píxeles a densidad cómoda
const ESPACIOS = [4, 8, 12, 16, 24, 32, 48];

export const esDensidad = (v: unknown): v is Densidad => DENSIDADES.some((d) => d.id === v);
export const esFuente = (v: unknown): v is Fuente => FUENTES.some((f) => f.id === v);
export const limitarEscala = (n: unknown): number => { const x = Number(n); return Number.isFinite(x) ? Math.min(ESCALA_MAX, Math.max(ESCALA_MIN, Math.round(x / 5) * 5)) : ESCALA_POR_DEFECTO; };

export interface Aspecto { escala: number; densidad: Densidad; fuente: Fuente; }
export interface VariablesAspecto { tamanoRaiz: string; vars: Record<string, string>; }

// Pura: lo que hay que poner sobre <html> para ese aspecto. La fuente «de siempre» no toca nada (queda la del tema).
export function variablesAspecto(a: Aspecto): VariablesAspecto {
  const factor = DENSIDADES.find((d) => d.id === a.densidad)?.factor ?? 1;
  const vars: Record<string, string> = {};
  ESPACIOS.forEach((px, i) => { vars[`--esp-${i + 1}`] = `${Math.round(px * factor * 10) / 10}px`; });
  vars['--hueco'] = `${Math.round(16 * factor)}px`;
  const f = FUENTES.find((x) => x.id === a.fuente)?.texto;
  if (f) { vars['--font-text'] = f; vars['--font-display'] = f; }
  return { tamanoRaiz: `${limitarEscala(a.escala)}%`, vars };
}

// Todas las variables que puede tocar: para limpiarlas al cambiar
export const VARIABLES_ASPECTO: string[] = [...ESPACIOS.map((_, i) => `--esp-${i + 1}`), '--hueco', '--font-text', '--font-display'];
