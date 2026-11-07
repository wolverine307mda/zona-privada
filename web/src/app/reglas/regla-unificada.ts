// Constructor de reglas, parte pura: un modelo único «cuando → y si → entonces» que se traduce a las reglas que ya existen
// (ReglaContexto para llego/salgo/duermo/despierto/hora y ReglaCasa para sensores) y de vuelta, sin romper lo guardado.
export type TipoCuando = 'llego' | 'salgo' | 'duermo' | 'despierto' | 'hora' | 'sensor';
export interface Cuando { tipo: TipoCuando; sensor?: string; operador?: '>' | '<'; umbral?: number; minutos?: number; }
export interface Condiciones { desde?: string; hasta?: string; dias?: number; tiempo?: string; soloEnCasa?: boolean; }
export interface Entonces { escenaCasaId?: number | null; escenaPcId?: number | null; aviso?: string; aparatoId?: number; accion?: 'encender' | 'apagar'; }
export interface ReglaUnificada { nombre: string; activa: boolean; cuando: Cuando; si: Condiciones; entonces: Entonces; esperaMinutos?: number; }

// formas guardadas (las mismas que el servidor)
export interface ReglaContextoDto { nombre: string; activa: boolean; evento: string; desde?: string | null; hasta?: string | null; dias: number; tiempo: string; soloEnCasa: boolean; escenaCasaId?: number | null; escenaPcId?: number | null; aviso: string; esperaMinutos: number; }
export interface ReglaCasaDto { nombre: string; activa: boolean; sensorId: number; sensorClave: string; operador: string; umbral: number; minutos: number; destinoId: number; accion: string; }

export const TODOS_LOS_DIAS = 127;
const NOMBRE_DIAS = ['lunes', 'martes', 'miércoles', 'jueves', 'viernes', 'sábado', 'domingo'];
const EVENTOS = ['llego', 'salgo', 'duermo', 'despierto', 'hora'];
const TIEMPOS: Record<string, string> = { lluvia: 'con lluvia', seco: 'con el cielo seco', frio: 'con frío', calor: 'con calor' };
const horaOk = (h?: string | null) => !!h && /^([01]\d|2[0-3]):[0-5]\d$/.test(h);

// Reglas de contexto ⇄ unificada
export function desdeContexto(r: ReglaContextoDto): ReglaUnificada {
  return { nombre: r.nombre, activa: r.activa, cuando: { tipo: r.evento as TipoCuando }, esperaMinutos: r.esperaMinutos,
    si: { desde: r.desde ?? undefined, hasta: r.hasta ?? undefined, dias: r.dias, tiempo: r.tiempo || undefined, soloEnCasa: r.soloEnCasa },
    entonces: { escenaCasaId: r.escenaCasaId ?? null, escenaPcId: r.escenaPcId ?? null, aviso: r.aviso || undefined } };
}
export function aContexto(r: ReglaUnificada): ReglaContextoDto {
  if (!EVENTOS.includes(r.cuando.tipo)) throw new Error('Esta regla es de un sensor: se guarda como regla de casa.');
  return { nombre: r.nombre, activa: r.activa, evento: r.cuando.tipo, desde: r.si.desde ?? null, hasta: r.si.hasta ?? null, dias: r.si.dias ?? TODOS_LOS_DIAS, tiempo: r.si.tiempo ?? '',
    soloEnCasa: !!r.si.soloEnCasa, escenaCasaId: r.entonces.escenaCasaId ?? null, escenaPcId: r.entonces.escenaPcId ?? null, aviso: r.entonces.aviso ?? '', esperaMinutos: r.esperaMinutos ?? 60 };
}
// Reglas de casa (sensores) ⇄ unificada
export function desdeCasa(r: ReglaCasaDto): ReglaUnificada {
  return { nombre: r.nombre, activa: r.activa, cuando: { tipo: 'sensor', sensor: r.sensorClave, operador: r.operador === '<' ? '<' : '>', umbral: r.umbral, minutos: r.minutos }, si: {},
    entonces: { aparatoId: r.destinoId, accion: r.accion === 'apagar' ? 'apagar' : 'encender' } };
}
export function aCasa(r: ReglaUnificada, sensorId: number): ReglaCasaDto {
  if (r.cuando.tipo !== 'sensor') throw new Error('Esta regla es de un evento: se guarda como regla de contexto.');
  return { nombre: r.nombre, activa: r.activa, sensorId, sensorClave: r.cuando.sensor ?? 'temperatura', operador: r.cuando.operador ?? '>', umbral: r.cuando.umbral ?? 0, minutos: r.cuando.minutos ?? 0, destinoId: r.entonces.aparatoId ?? 0, accion: r.entonces.accion ?? 'encender' };
}

export function nombreDias(mascara: number): string {
  if (mascara === TODOS_LOS_DIAS) return 'todos los días';
  if (mascara === 31) return 'de lunes a viernes';
  if (mascara === 96) return 'los fines de semana';
  const l = NOMBRE_DIAS.filter((_, i) => mascara & (1 << i));
  return l.length === 1 ? `los ${l[0]}` : 'los ' + l.slice(0, -1).join(', ') + ' y ' + l.at(-1);
}

// Lectura en español: «Cuando llegues a casa, de noche y con lluvia, pon la escena Cine»
export function leer(r: ReglaUnificada, escenas: Record<number, string> = {}): string {
  const c = r.cuando;
  const cuando = c.tipo === 'llego' ? 'llegues a casa' : c.tipo === 'salgo' ? 'salgas de casa' : c.tipo === 'duermo' ? 'te vayas a dormir' : c.tipo === 'despierto' ? 'te despiertes'
    : c.tipo === 'hora' ? (horaOk(r.si.desde) ? `sean las ${r.si.desde}` : 'llegue la hora')
    : `${c.sensor ?? 'el sensor'} ${c.operador === '<' ? 'baje de' : 'supere'} ${c.umbral ?? 0}${c.minutos ? ` durante ${c.minutos} min` : ''}`;
  const extra: string[] = [];
  if (c.tipo !== 'hora' && horaOk(r.si.desde) && horaOk(r.si.hasta)) extra.push(`entre las ${r.si.desde} y las ${r.si.hasta}`);
  if (r.si.dias !== undefined && r.si.dias !== TODOS_LOS_DIAS) extra.push(nombreDias(r.si.dias));
  if (r.si.tiempo && TIEMPOS[r.si.tiempo]) extra.push(TIEMPOS[r.si.tiempo]);
  if (r.si.soloEnCasa) extra.push('si estás en casa');
  const acc: string[] = [];
  const e = r.entonces;
  if (e.escenaCasaId) acc.push(`pon la escena ${escenas[e.escenaCasaId] ?? '#' + e.escenaCasaId} en casa`);
  if (e.escenaPcId) acc.push(`activa la escena del PC ${escenas[e.escenaPcId] ?? '#' + e.escenaPcId}`);
  if (e.aparatoId) acc.push(`${e.accion === 'apagar' ? 'apaga' : 'enciende'} el aparato #${e.aparatoId}`);
  if (e.aviso) acc.push(`avísame: «${e.aviso}»`);
  return `Cuando ${cuando}${extra.length ? ', ' + extra.join(', ') : ''}, ${acc.length ? acc.join(' y ') : '(sin acción)'}.`;
}

// Validación en vivo: lista de problemas en español (vacía = lista para guardar)
export function validar(r: ReglaUnificada): string[] {
  const p: string[] = [];
  if (!r.nombre.trim()) p.push('Falta el nombre.');
  const c = r.cuando;
  if (c.tipo === 'hora' && !horaOk(r.si.desde)) p.push('Una regla «a una hora» necesita la hora de inicio (HH:mm).');
  if (r.si.desde && !horaOk(r.si.desde)) p.push('La hora de inicio no es válida.');
  if (r.si.hasta && !horaOk(r.si.hasta)) p.push('La hora de fin no es válida.');
  if (!!r.si.desde !== !!r.si.hasta && c.tipo !== 'hora') p.push('Pon las dos horas de la franja o ninguna.');
  if (r.si.dias !== undefined && (r.si.dias < 1 || r.si.dias > TODOS_LOS_DIAS)) p.push('Elige al menos un día de la semana.');
  if (r.si.tiempo && !TIEMPOS[r.si.tiempo]) p.push('El tiempo elegido no existe.');
  if (c.tipo === 'sensor') {
    if (!c.sensor) p.push('Elige el sensor.');
    if (c.umbral === undefined || Number.isNaN(c.umbral)) p.push('Falta el umbral del sensor.');
    if (!r.entonces.aparatoId) p.push('Elige el aparato que se enciende o apaga.');
  } else if (!r.entonces.escenaCasaId && !r.entonces.escenaPcId && !r.entonces.aviso?.trim()) p.push('Falta qué hacer: una escena o un aviso.');
  return p;
}

// Choques entre reglas del mismo evento que se pisarían (mismos días y mismo tiempo)
export function choques(reglas: ReglaUnificada[]): string[] {
  const salida: string[] = [];
  for (let i = 0; i < reglas.length; i++) for (let j = i + 1; j < reglas.length; j++) {
    const a = reglas[i], b = reglas[j];
    if (!a.activa || !b.activa || a.cuando.tipo !== b.cuando.tipo || a.cuando.tipo === 'sensor') continue;
    if (!((a.si.dias ?? TODOS_LOS_DIAS) & (b.si.dias ?? TODOS_LOS_DIAS))) continue;
    if (a.si.tiempo && b.si.tiempo && a.si.tiempo !== b.si.tiempo) continue;
    if (a.cuando.tipo === 'hora' && a.si.desde !== b.si.desde) continue;
    salida.push(`«${a.nombre}» y «${b.nombre}» se disparan a la vez.`);
  }
  return salida;
}

// Simulación sobre lo que pasó (sin ejecutar nada): cuántas veces se habría disparado en las últimas horas
export interface Suceso { cuando: Date; evento: string; tiempo?: string; enCasa?: boolean; sensor?: string; valor?: number; }
export function simular(r: ReglaUnificada, sucesos: Suceso[], ahora: Date, horas = 24): number {
  const desde = ahora.getTime() - horas * 3600000;
  const espera = (r.esperaMinutos ?? 60) * 60000;
  let ultimo = -Infinity, n = 0;
  for (const s of [...sucesos].sort((a, b) => a.cuando.getTime() - b.cuando.getTime())) {
    const t = s.cuando.getTime();
    if (t < desde || t > ahora.getTime()) continue;
    if (!dispara(r, s)) continue;
    if (t - ultimo < espera) continue;
    ultimo = t; n++;
  }
  return n;
}
function dispara(r: ReglaUnificada, s: Suceso): boolean {
  if (!r.activa) return false;
  const c = r.cuando;
  if (c.tipo === 'sensor') {
    if (s.evento !== 'sensor' || s.sensor !== c.sensor || s.valor === undefined) return false;
    return c.operador === '<' ? s.valor < (c.umbral ?? 0) : s.valor > (c.umbral ?? 0);
  }
  if (s.evento !== c.tipo) return false;
  const dia = (s.cuando.getDay() + 6) % 7;
  if (r.si.dias !== undefined && !(r.si.dias & (1 << dia))) return false;
  const hm = `${String(s.cuando.getHours()).padStart(2, '0')}:${String(s.cuando.getMinutes()).padStart(2, '0')}`;
  if (c.tipo !== 'hora' && r.si.desde && r.si.hasta) {
    const dentro = r.si.desde <= r.si.hasta ? hm >= r.si.desde && hm <= r.si.hasta : hm >= r.si.desde || hm <= r.si.hasta;
    if (!dentro) return false;
  }
  if (r.si.tiempo && s.tiempo !== r.si.tiempo) return false;
  if (r.si.soloEnCasa && !s.enCasa) return false;
  return true;
}

// Plantillas listas para usar
export const PLANTILLAS: { id: string; titulo: string; regla: ReglaUnificada }[] = [
  { id: 'llegar-lluvia', titulo: 'Llego a casa con lluvia', regla: { nombre: 'Llegar con lluvia', activa: true, cuando: { tipo: 'llego' }, si: { tiempo: 'lluvia' }, entonces: { aviso: 'Recuerda recoger la ropa' }, esperaMinutos: 120 } },
  { id: 'noche', titulo: 'Me voy a dormir', regla: { nombre: 'Buenas noches', activa: true, cuando: { tipo: 'duermo' }, si: { dias: TODOS_LOS_DIAS }, entonces: { aviso: 'Cierra todo y a descansar' } } },
  { id: 'salgo-entre-semana', titulo: 'Salgo entre semana', regla: { nombre: 'Salgo a trabajar', activa: true, cuando: { tipo: 'salgo' }, si: { dias: 31, desde: '07:00', hasta: '10:00' }, entonces: { aviso: '¿Apagado todo?' } } },
  { id: 'calor-aire', titulo: 'Calor en el salón', regla: { nombre: 'Aire por calor', activa: true, cuando: { tipo: 'sensor', sensor: 'temperatura', operador: '>', umbral: 28, minutos: 10 }, si: {}, entonces: { aparatoId: 1, accion: 'encender' } } },
];

// Exportar / importar como JSON versionado; lo inválido se descarta con el motivo
export function exportar(reglas: ReglaUnificada[]): string { return JSON.stringify({ version: 1, reglas }, null, 2); }
export function importar(texto: string): { reglas: ReglaUnificada[]; errores: string[] } {
  let j: unknown;
  try { j = JSON.parse(texto); } catch { return { reglas: [], errores: ['El texto no es un JSON válido.'] }; }
  const lista = (j as { reglas?: unknown })?.reglas;
  if (!Array.isArray(lista)) return { reglas: [], errores: ['No encuentro la lista «reglas».'] };
  const reglas: ReglaUnificada[] = [], errores: string[] = [];
  lista.forEach((x, i) => {
    const r = x as ReglaUnificada;
    if (!r || typeof r !== 'object' || !r.cuando || typeof r.nombre !== 'string') { errores.push(`Regla ${i + 1}: forma incorrecta.`); return; }
    const norm = { ...r, activa: r.activa ?? true, si: r.si ?? {}, entonces: r.entonces ?? {} };
    const p = validar(norm);
    if (p.length) errores.push(`Regla ${i + 1} («${r.nombre}»): ${p[0]}`); else reglas.push(norm);
  });
  return { reglas, errores };
}
