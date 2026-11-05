import { COLORES, FONDOS, Modo, TemaService } from '../core/tema.service';

// Las acciones del buscador (Ctrl K): se escriben con «>» («> color rojo», «> bloquear pc») o salen al buscar por su nombre.
// Lo que manda órdenes al PC pide «confirmar que eres tú» igual que en su pantalla (lo resuelve quien pasa `orden`).
export interface Accion { id: string; titulo: string; detalle: string; icono: string; hacer: () => void | Promise<void>; /** se queda abierto el buscador para enseñar el resultado */ mantener?: boolean; }

const quitarTildes = (t: string) => t.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();

/** «> bloquear» o «>bloquear» es un comando; el resto, una búsqueda normal */
export const esComando = (q: string): boolean => q.trimStart().startsWith('>');
export const textoComando = (q: string): string => q.trimStart().replace(/^>\s*/, '');

/** las que contienen todas las palabras del texto, sin mirar mayúsculas ni tildes; con el texto vacío, todas */
export function filtrarAcciones<T extends { titulo: string; detalle: string }>(lista: T[], texto: string): T[] {
  const palabras = quitarTildes(texto).split(/\s+/).filter(Boolean);
  if (palabras.length === 0) return lista;
  return lista.filter((a) => { const s = quitarTildes(`${a.titulo} ${a.detalle}`); return palabras.every((p) => s.includes(p)); });
}

export interface Dependencias {
  tema: TemaService;
  /** manda una orden al PC (ya con la confirmación de identidad resuelta) y devuelve el texto del resultado */
  orden: (tipo: string, parametro?: string) => Promise<string>;
  /** escenas del PC ya cargadas */
  escenas: () => { id: number; nombre: string }[];
  aplicarEscena: (id: number) => Promise<string>;
  avisar: (texto: string) => void;
  /** hábitos que tocan hoy y aún no están hechos, para marcarlos desde el buscador */
  habitos?: () => { id: number; nombre: string }[];
  marcarHabito?: (id: number) => Promise<string>;
}

const SOL = 'M12 4V2M12 22v-2M4 12H2M22 12h-2M12 17a5 5 0 1 0 0-10 5 5 0 0 0 0 10z';
const LUNA = 'M20 14.5A8 8 0 0 1 9.5 4 8 8 0 1 0 20 14.5z';
const PALETA = 'M12 3a9 9 0 1 0 0 18c1.2 0 2-.8 2-1.8 0-.5-.2-.9-.5-1.3-.3-.4-.5-.8-.5-1.3 0-1 .8-1.8 1.8-1.8H17a4 4 0 0 0 4-4c0-4-4-7.8-9-7.8zM7.5 11h.01M10 7.5h.01M14 7.5h.01';
const PC = 'M4 5h16v10H4zM9 19h6M12 15v4';
const ESCENA = 'M13 2 4 14h7l-1 8 9-12h-7z';

export function crearAcciones(d: Dependencias): Accion[] {
  const modos: [Modo, string][] = [['light', 'Modo día'], ['dark', 'Modo noche'], ['system', 'Modo del sistema']];
  return [
    ...modos.map(([m, t]) => ({ id: `a-modo-${m}`, titulo: t, detalle: 'Tema', icono: m === 'dark' ? LUNA : SOL, hacer: () => d.tema.ponerModo(m) })),
    ...COLORES.map((c) => ({ id: `a-color-${c.id}`, titulo: `Color ${c.nombre.toLowerCase()}`, detalle: 'Paleta de la app', icono: PALETA, hacer: () => d.tema.ponerColor(c.id) })),
    ...FONDOS.map((f) => ({ id: `a-fondo-${f.id}`, titulo: `Fondo ${f.nombre.toLowerCase()}`, detalle: f.nota, icono: PALETA, hacer: () => d.tema.ponerFondo(f.id) })),
    { id: 'a-bloquear-pc', titulo: 'Bloquear el PC', detalle: 'Orden al equipo de casa', icono: PC, mantener: true, hacer: async () => d.avisar(await d.orden('bloquear')) },
    { id: 'a-suspender-pc', titulo: 'Suspender el PC', detalle: 'Orden al equipo de casa', icono: PC, mantener: true, hacer: async () => d.avisar(await d.orden('suspender')) },
    { id: 'a-despierto-on', titulo: 'Mantener el PC despierto', detalle: 'Orden al equipo de casa', icono: PC, mantener: true, hacer: async () => d.avisar(await d.orden('despierto', 'on')) },
    { id: 'a-despierto-off', titulo: 'Dejar que el PC se suspenda', detalle: 'Orden al equipo de casa', icono: PC, mantener: true, hacer: async () => d.avisar(await d.orden('despierto', 'off')) },
    ...(d.habitos?.() ?? []).map((h) => ({ id: `a-habito-${h.id}`, titulo: `Hecho: ${h.nombre}`, detalle: 'Apuntar el hábito de hoy', icono: 'M5 12.5l4.2 4.2L19 7', mantener: true, hacer: async () => d.avisar(await d.marcarHabito!(h.id)) })),
    ...d.escenas().map((e) => ({ id: `a-escena-${e.id}`, titulo: `Escena ${e.nombre}`, detalle: 'Escenas del PC', icono: ESCENA, mantener: true, hacer: async () => d.avisar(await d.aplicarEscena(e.id)) })),
  ];
}
