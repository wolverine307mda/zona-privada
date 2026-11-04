// Lo que manda la API de la casa y las cuentas pequeñas que hace la pantalla (todo puro, con su spec).
export interface CampoCasa {
  clave: string; tipo: 'numero' | 'bool' | 'color' | 'opcion'; etiqueta: string; unidad: string | null; escribible: boolean;
  min: number | null; max: number | null; opciones: string[] | null; valor: string | null; actualizado: string | null;
}
export interface AparatoCasa { id: number; nombre: string; tipo: string; tipoNombre: string; driver: string; estanciaId: number | null; entidades: CampoCasa[]; }
export interface EstanciaCasa { id: number; nombre: string; icono: string; orden: number; }
export interface EstadoCasa { estancias: EstanciaCasa[]; aparatos: AparatoCasa[]; panel: string | null; }
export interface TipoCatalogo { id: string; nombre: string; campos: unknown[]; }
export interface Catalogo { tipos: TipoCatalogo[]; drivers: string[]; }
export interface DispositivoTuya { id: string; nombre: string; categoria: string; enLinea: boolean; tipoSugerido: string | null; }
export interface ReglaCasa {
  id: number; nombre: string; activa: boolean; sensorId: number; sensorClave: string; operador: '>' | '<'; umbral: number; minutos: number; destinoId: number;
  accion: 'encender' | 'apagar'; objetivo: number | null; modo: string | null; revertir: boolean; histeresis: number; esperaMinutos: number; soloAvisar: boolean;
  disparada: boolean; ultima: string | null; resumen: string;
}
export type ReglaEntrada = Omit<ReglaCasa, 'id' | 'disparada' | 'ultima' | 'resumen'>;
export interface PlantillaRegla { id: string; nombre: string; descripcion: string; valores: Partial<ReglaEntrada>; }
export interface RegistroRegla { id: number; reglaId: number | null; fecha: string; texto: string; ok: boolean; }
export interface EscenaCasa { id: number; nombre: string; icono: string; acciones: number; }
export interface PuntoHistorial { fecha: string; valor: number; }

// Cómo está colocado el panel: el orden de los aparatos en cada habitación y los que has ocultado
export interface PanelGuardado { orden: Record<string, number[]>; ocultos: number[]; }
export const PANEL_VACIO: PanelGuardado = { orden: {}, ocultos: [] };

export function leerPanel(json: string | null): PanelGuardado {
  if (!json) return { orden: {}, ocultos: [] };
  try {
    const p = JSON.parse(json) as Partial<PanelGuardado>;
    const orden: Record<string, number[]> = {};
    for (const [k, v] of Object.entries(p.orden ?? {})) if (Array.isArray(v)) orden[k] = v.filter((n) => Number.isInteger(n));
    return { orden, ocultos: Array.isArray(p.ocultos) ? p.ocultos.filter((n) => Number.isInteger(n)) : [] };
  } catch { return { orden: {}, ocultos: [] }; }
}

// La clave de una pestaña: «todo» o el id de la habitación
export const clavePestana = (estanciaId: number | null | 'todo') => (estanciaId === null ? 'sin' : String(estanciaId));

// Los aparatos de una pestaña en el orden guardado; los nuevos van al final
export function aparatosDe(aparatos: AparatoCasa[], pestana: 'todo' | number, panel: PanelGuardado, conOcultos: boolean): AparatoCasa[] {
  const de = pestana === 'todo' ? aparatos : aparatos.filter((a) => a.estanciaId === pestana);
  const orden = panel.orden[String(pestana)] ?? [];
  const pos = (id: number) => { const i = orden.indexOf(id); return i < 0 ? Number.MAX_SAFE_INTEGER : i; };
  return [...de].sort((a, b) => pos(a.id) - pos(b.id) || a.id - b.id).filter((a) => conOcultos || !panel.ocultos.includes(a.id));
}

// Sube o baja un aparato una posición en su pestaña y devuelve el panel nuevo (el original no se toca)
export function mover(aparatos: AparatoCasa[], pestana: 'todo' | number, panel: PanelGuardado, id: number, delta: -1 | 1): PanelGuardado {
  const ids = aparatosDe(aparatos, pestana, panel, true).map((a) => a.id);
  const i = ids.indexOf(id), j = i + delta;
  if (i < 0 || j < 0 || j >= ids.length) return panel;
  [ids[i], ids[j]] = [ids[j], ids[i]];
  return { ...panel, orden: { ...panel.orden, [String(pestana)]: ids } };
}

export function alternarOculto(panel: PanelGuardado, id: number): PanelGuardado {
  return { ...panel, ocultos: panel.ocultos.includes(id) ? panel.ocultos.filter((x) => x !== id) : [...panel.ocultos, id] };
}

export const campo = (a: AparatoCasa, clave: string): CampoCasa | undefined => a.entidades.find((e) => e.clave === clave);
export const valor = (a: AparatoCasa, clave: string): string | null => campo(a, clave)?.valor ?? null;
export const encendido = (a: AparatoCasa, clave = 'encendido'): boolean => valor(a, clave) === 'true';
export function numero(a: AparatoCasa, clave: string): number | null {
  const v = valor(a, clave);
  const n = v === null ? NaN : Number(v);
  return Number.isFinite(n) ? n : null;
}

// «23,4 °C»
export function texto(n: number | null, unidad: string | null, decimales = 1): string {
  if (n === null) return '—';
  return `${n.toLocaleString('es-ES', { minimumFractionDigits: 0, maximumFractionDigits: decimales })}${unidad ? (unidad === '%' ? ' %' : ' ' + unidad) : ''}`;
}

// El trazo de una gráfica pequeña (viewBox ancho × alto) a partir de las lecturas; vacío si hay menos de dos
export function trazo(puntos: PuntoHistorial[], ancho = 120, alto = 36, margen = 3): string {
  if (puntos.length < 2) return '';
  const t0 = new Date(puntos[0].fecha).getTime(), t1 = new Date(puntos[puntos.length - 1].fecha).getTime();
  const vs = puntos.map((p) => p.valor);
  const min = Math.min(...vs), max = Math.max(...vs), rango = max - min || 1, dt = t1 - t0 || 1;
  return puntos.map((p, i) => {
    const x = margen + ((new Date(p.fecha).getTime() - t0) / dt) * (ancho - 2 * margen);
    const y = alto - margen - ((p.valor - min) / rango) * (alto - 2 * margen);
    return `${i ? 'L' : 'M'}${x.toFixed(1)} ${y.toFixed(1)}`;
  }).join(' ');
}

// Cuánto hace que se leyó un dato: «ahora», «hace 3 min», «hace 2 h»
export function hace(iso: string | null, ahora = Date.now()): string {
  if (!iso) return 'sin datos';
  const s = Math.max(0, Math.round((ahora - new Date(iso).getTime()) / 1000));
  if (s < 60) return 'ahora';
  if (s < 3600) return `hace ${Math.round(s / 60)} min`;
  if (s < 86400) return `hace ${Math.round(s / 3600)} h`;
  return `hace ${Math.round(s / 86400)} d`;
}
