// Temas completos: un conjunto de variables (fondo, superficies, texto, líneas, fuente y radios) aparte del color de acento.
// «Cristal» es el de siempre y no toca nada. Los demás ponen sus variables sobre <html> (TemaService) y algunos fijan el modo claro u oscuro para que
// las escenas 3D y los fondos animados sigan funcionando. Las paletas viven aquí (y no en el CSS) para poder comprobar su contraste en las pruebas.
export type TemaId = 'cristal' | 'papel' | 'terminal' | 'sepia' | 'contraste' | 'oled';
export type ModoTema = 'dark' | 'light' | null;

export interface Tema {
  id: TemaId; nombre: string; nota: string;
  modo: ModoTema;                        // null = el que elijas tú (claro, oscuro o el del sistema)
  vars: Record<string, string>;          // variables CSS que se ponen sobre <html>
  minimo: number;                        // razón de contraste que exige el texto normal (WCAG AA = 4,5; alto contraste = 7)
}

export const TEMAS: Tema[] = [
  { id: 'cristal', nombre: 'Cristal', nota: 'el de siempre', modo: null, vars: {}, minimo: 4.5 },
  { id: 'papel', nombre: 'Papel', nota: 'claro, con letra de libro', modo: 'light', minimo: 4.5, vars: {
    '--fondo': '#f6f1e7', '--tarjeta': '#fffdf7', '--tarjeta-2': '#efe8d8', '--linea': '#d9cfb8', '--texto': '#2b2118', '--soft': '#6a5d4a',
    '--font-text': "Georgia, 'Iowan Old Style', 'Times New Roman', serif", '--font-display': "Georgia, 'Times New Roman', serif",
  } },
  { id: 'terminal', nombre: 'Terminal', nota: 'oscuro, mono y verde', modo: 'dark', minimo: 4.5, vars: {
    '--fondo': '#050805', '--tarjeta': '#0a120a', '--tarjeta-2': '#101c10', '--linea': '#1f3a1f', '--texto': '#b6ffb6', '--soft': '#6fbf6f',
    '--font-text': "'JetBrains Mono', ui-monospace, Menlo, Consolas, monospace", '--font-display': "'JetBrains Mono', ui-monospace, Menlo, Consolas, monospace",
  } },
  { id: 'sepia', nombre: 'Sepia', nota: 'cálido, descansa la vista', modo: 'light', minimo: 4.5, vars: {
    '--fondo': '#f1e4c8', '--tarjeta': '#f8eed8', '--tarjeta-2': '#eadbb8', '--linea': '#d6c39a', '--texto': '#3b2f1e', '--soft': '#65553a',
  } },
  { id: 'contraste', nombre: 'Alto contraste', nota: 'negro y blanco, bordes gruesos', modo: 'dark', minimo: 7, vars: {
    '--fondo': '#000000', '--tarjeta': '#000000', '--tarjeta-2': '#141414', '--linea': '#ffffff', '--texto': '#ffffff', '--soft': '#f0f0f0',
  } },
  { id: 'oled', nombre: 'Medianoche OLED', nota: 'negro puro, ahorra batería', modo: 'dark', minimo: 4.5, vars: {
    '--fondo': '#000000', '--tarjeta': '#070707', '--tarjeta-2': '#0e0e0e', '--linea': '#1c1c1c', '--texto': '#e8edf0', '--soft': '#9aa4ab',
  } },
];

export const TEMA_POR_DEFECTO: TemaId = 'cristal';
export const esTema = (v: unknown): v is TemaId => TEMAS.some((t) => t.id === v);
export const temaDe = (id: TemaId): Tema => TEMAS.find((t) => t.id === id) ?? TEMAS[0];
// todas las variables que algún tema toca: para limpiarlas al cambiar
export const VARIABLES_TEMAS: string[] = [...new Set(TEMAS.flatMap((t) => Object.keys(t.vars)))];

// ---- contraste WCAG ----
const canal = (c: number): number => { const s = c / 255; return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4; };
export function luminancia(hex: string): number {
  const h = hex.replace('#', '');
  const n = h.length === 3 ? h.split('').map((x) => x + x).join('') : h;
  const r = parseInt(n.slice(0, 2), 16), g = parseInt(n.slice(2, 4), 16), b = parseInt(n.slice(4, 6), 16);
  return 0.2126 * canal(r) + 0.7152 * canal(g) + 0.0722 * canal(b);
}
// la razón de contraste entre dos colores (1 a 21)
export function razonContraste(a: string, b: string): number {
  const [x, y] = [luminancia(a), luminancia(b)].sort((p, q) => q - p);
  return (x + 0.05) / (y + 0.05);
}
// Los pares de cada tema que tienen que leerse bien
export const PARES: [texto: string, fondo: string][] = [['--texto', '--fondo'], ['--texto', '--tarjeta'], ['--texto', '--tarjeta-2'], ['--soft', '--fondo'], ['--soft', '--tarjeta'], ['--soft', '--tarjeta-2']];
