// La distribución de las tarjetas de Inicio: el orden, cuáles se ven y de qué tamaño (S · M · L). Todo puro y sin Angular para poder probarlo:
// una distribución vieja o rara se migra, una tarjeta que desaparece se quita y una nueva se añade al final sin dejar hueco.
export type Tamano = 'S' | 'M' | 'L';
export interface Diseno { orden: string[]; ocultas: string[]; tamanos: Record<string, Tamano>; }
export interface TarjetaDef { id: string; titulo: string; }

// El orden de siempre (el del mosaico de Inicio) y los nombres que se ven al personalizar
export const TARJETAS: TarjetaDef[] = [
  { id: 'ropa', titulo: 'Ropa tendida' }, { id: 'cumples', titulo: 'Próximos cumpleaños' }, { id: 'dinero', titulo: 'El dinero del mes' }, { id: 'tiempo', titulo: 'El tiempo' },
  { id: 'musica', titulo: 'Lo que suena' }, { id: 'coche', titulo: 'El coche o la deuda' }, { id: 'agenda', titulo: 'Lo próximo de la agenda' }, { id: 'recordatorios', titulo: 'Recordatorios' },
  { id: 'notas', titulo: 'Notas fijadas' }, { id: 'habitos', titulo: 'Hábitos de hoy' }, { id: 'fotografia', titulo: 'Fotografía' }, { id: 'pc', titulo: 'Mi PC' },
  { id: 'casa', titulo: 'Aparatos de casa' }, { id: 'seguridad', titulo: 'Seguridad' }, { id: 'gasolina', titulo: 'Gasolina' }, { id: 'carpetas', titulo: 'Carpetas' },
];
export const IDS_POR_DEFECTO: string[] = TARJETAS.map((t) => t.id);
export const BASE_TAMANO: Record<Tamano, string> = { S: '180px', M: '300px', L: '100%' };

export const disenoPorDefecto = (): Diseno => ({ orden: [...IDS_POR_DEFECTO], ocultas: [], tamanos: {} });
const esTamano = (v: unknown): v is Tamano => v === 'S' || v === 'M' || v === 'L';

// Lo que llega del servidor (o de una versión vieja: nada, `{}`, una lista suelta…) → una distribución válida para las tarjetas que existen ahora.
export function normalizar(crudo: unknown, ids: readonly string[] = IDS_POR_DEFECTO): Diseno {
  const c = (crudo && typeof crudo === 'object' ? crudo : {}) as { orden?: unknown; ocultas?: unknown; tamanos?: unknown };
  const lista = (v: unknown): string[] => (Array.isArray(v) ? v.filter((x): x is string => typeof x === 'string') : []);
  const existe = new Set(ids);
  const orden: string[] = [];
  for (const id of lista(Array.isArray(crudo) ? crudo : c.orden)) if (existe.has(id) && !orden.includes(id)) orden.push(id);
  for (const id of ids) if (!orden.includes(id)) orden.push(id);                     // las nuevas, al final y en su orden de siempre
  const ocultas = [...new Set(lista(c.ocultas).filter((id) => existe.has(id)))];
  const tamanos: Record<string, Tamano> = {};
  if (c.tamanos && typeof c.tamanos === 'object') for (const [id, t] of Object.entries(c.tamanos as Record<string, unknown>)) if (existe.has(id) && esTamano(t)) tamanos[id] = t;
  return { orden, ocultas, tamanos };
}

export const mover = (d: Diseno, id: string, delta: -1 | 1): Diseno => {
  const i = d.orden.indexOf(id), j = i + delta;
  if (i < 0 || j < 0 || j >= d.orden.length) return d;
  const orden = [...d.orden]; [orden[i], orden[j]] = [orden[j], orden[i]];
  return { ...d, orden };
};
export const alternarOculta = (d: Diseno, id: string): Diseno => ({ ...d, ocultas: d.ocultas.includes(id) ? d.ocultas.filter((x) => x !== id) : [...d.ocultas, id] });
export const ponerTamano = (d: Diseno, id: string, t: Tamano | null): Diseno => {
  const tamanos = { ...d.tamanos };
  if (t === null) delete tamanos[id]; else tamanos[id] = t;
  return { ...d, tamanos };
};
export const esPorDefecto = (d: Diseno): boolean => d.ocultas.length === 0 && Object.keys(d.tamanos).length === 0 && d.orden.every((id, i) => id === IDS_POR_DEFECTO[i]) && d.orden.length === IDS_POR_DEFECTO.length;

// Qué tarjeta es un elemento de Inicio, por su etiqueta, sus clases y adónde lleva (`href`). El segundo enlace a /agenda son los recordatorios.
export interface DescripcionTarjeta { etiqueta: string; clases: string[]; href: string | null; repeticion: number; }
export function idDeTarjeta(d: DescripcionTarjeta): string | null {
  const e = d.etiqueta.toLowerCase();
  if (e === 'app-ropa-chip') return 'ropa';
  if (e === 'app-cumples-chip') return 'cumples';
  if (e === 'app-tiempo') return 'tiempo';
  const c = (n: string) => d.clases.includes(n);
  if (c('dinero')) return 'dinero';
  if (c('musica')) return 'musica';
  if (c('coche')) return 'coche';
  if (c('carpetas')) return 'carpetas';
  const h = (d.href ?? '').split('?')[0];
  if (h === '/agenda') return d.repeticion === 0 ? 'agenda' : 'recordatorios';
  if (h === '/notas') return 'notas';
  if (h === '/habitos') return 'habitos';
  if (h === '/fotografia') return 'fotografia';
  if (h === '/pc') return 'pc';
  if (h === '/casa') return 'casa';
  if (h === '/seguridad') return 'seguridad';
  if (h.startsWith('/gastos')) return 'gasolina';
  return null;
}
