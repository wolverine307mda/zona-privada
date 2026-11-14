import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

// ---- runbooks ----
export type TipoPaso = 'aviso' | 'espera' | 'comprobar' | 'despertar' | 'escena' | 'casa' | 'orden';
export interface PasoRunbook { tipo: TipoPaso; texto?: string | null; valor?: string | null; segundos?: number | null; aprobar: boolean; continuar: boolean; resumen?: string; }
export interface Runbook {
  id: number; nombre: string; descripcion: string; guardia: boolean; pasos: PasoRunbook[];
  ultima: { id: number; estado: string; inicio: string; fin: string | null } | null;
}
export interface EntradaRunbook { nombre: string; descripcion: string; guardia: boolean; pasos: PasoRunbook[]; }
export interface EstadoPaso { tipo: string; resumen: string; aprobar: boolean; estado: 'pendiente' | 'en-curso' | 'esperando' | 'ok' | 'error' | 'omitido'; salida: string; }
export interface Ejecucion { id: number; runbookId: number | null; nombre: string; origen: string; inicio: string; fin: string | null; estado: string; pasos: EstadoPaso[]; }

// lo que ofrece cada tipo de paso en el editor
export const TIPOS_PASO: { id: TipoPaso; nombre: string; ayuda: string }[] = [
  { id: 'comprobar', nombre: 'Comprobar', ayuda: 'Mira que algo esté bien' },
  { id: 'aviso', nombre: 'Avisar', ayuda: 'Te manda un mensaje' },
  { id: 'espera', nombre: 'Esperar', ayuda: 'Una pausa (hasta 300 s)' },
  { id: 'despertar', nombre: 'Despertar un PC', ayuda: 'Wake-on-LAN' },
  { id: 'escena', nombre: 'Aplicar una escena', ayuda: 'Una escena del PC' },
  { id: 'casa', nombre: 'Llegar / salir de casa', ayuda: 'Lo que haces al llegar o al irte' },
  { id: 'orden', nombre: 'Orden al PC', ayuda: 'Bloquear, suspender, apagar…' },
];
export const COMPROBACIONES = [{ id: 'base', nombre: 'La base de datos' }, { id: 'agente', nombre: 'El agente del PC' }, { id: 'direccion', nombre: 'La dirección pública' }, { id: 'vigilancia', nombre: 'Las vigilancias' }];
export const ORDENES = [{ id: 'bloquear', nombre: 'Bloquear la pantalla' }, { id: 'suspender', nombre: 'Suspender' }, { id: 'apagar', nombre: 'Apagar' }, { id: 'reiniciar', nombre: 'Reiniciar' }, { id: 'cancelar', nombre: 'Cancelar un apagado' }];
// estas cortan lo que haces en el PC: el paso tiene que pedir permiso
export const ORDENES_DELICADAS = ['suspender', 'apagar', 'reiniciar'];

export const pasoNuevo = (tipo: TipoPaso): PasoRunbook => ({
  tipo, aprobar: false, continuar: false,
  texto: tipo === 'aviso' ? '' : null,
  segundos: tipo === 'espera' ? 5 : null,
  valor: tipo === 'comprobar' ? 'base' : tipo === 'casa' ? 'llegada' : tipo === 'orden' ? 'bloquear' : tipo === 'escena' ? '' : tipo === 'despertar' ? '' : null,
});

// un paso que corta lo que haces no puede ir sin permiso: se marca solo al elegirlo
export const conPermisoSiHaceFalta = (p: PasoRunbook): PasoRunbook => (p.tipo === 'orden' && ORDENES_DELICADAS.includes(p.valor ?? '') ? { ...p, aprobar: true } : p);

// ---- línea de tiempo ----
export interface Suceso { hora: string; tipo: string; titulo: string; detalle: string; gravedad: 'info' | 'aviso' | 'grave'; }
export const TIPOS_SUCESO: { id: string; nombre: string; icono: string }[] = [
  { id: 'acceso', nombre: 'Accesos', icono: 'M15 7a4 4 0 1 1-3.5 5.9L4 20v-3l2-1v-2l2-1 3.5-3.1A4 4 0 0 1 15 7z' },
  { id: 'aprobacion', nombre: 'Entradas con el iPhone', icono: 'M7 3h10v18H7zM11 18h2' },
  { id: 'guardia', nombre: 'Guardia', icono: 'M12 3l8 3v6c0 5-3.5 8-8 9-4.5-1-8-4-8-9V6z' },
  { id: 'alerta', nombre: 'Alertas', icono: 'M12 3l9 16H3zM12 10v4M12 17v.5' },
  { id: 'runbook', nombre: 'Runbooks', icono: 'M6 3h9l4 4v14H6zM9 12h7M9 16h5' },
  { id: 'pc', nombre: 'Órdenes al PC', icono: 'M3 5h18v11H3zM8 20h8M12 16v4' },
  { id: 'sistema', nombre: 'Sistema', icono: 'M12 8v4l3 2M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18z' },
];

// ---- entrar con el iPhone ----
export interface Pendiente { id: string; equipo: string; ip: string; lugar: string; creada: string; caduca: string; }
export interface Principal { id: number; nombre: string; mac: string; enLinea: boolean | null; conMac: boolean; }

@Injectable({ providedIn: 'root' })
export class ServidorService {
  private readonly http = inject(HttpClient);

  runbooks() { return firstValueFrom(this.http.get<Runbook[]>('/api/servidor/runbooks')); }
  crear(e: EntradaRunbook) { return firstValueFrom(this.http.post<Runbook>('/api/servidor/runbooks', e)); }
  guardar(id: number, e: EntradaRunbook) { return firstValueFrom(this.http.put(`/api/servidor/runbooks/${id}`, e)); }
  borrar(id: number) { return firstValueFrom(this.http.delete(`/api/servidor/runbooks/${id}`)); }
  ejecutar(id: number) { return firstValueFrom(this.http.post<{ ejecucion: number }>(`/api/servidor/runbooks/${id}/ejecutar`, null)); }
  ejecuciones(max = 12) { return firstValueFrom(this.http.get<Ejecucion[]>('/api/servidor/ejecuciones', { params: { max } })); }
  aprobarPaso(ejecucion: number) { return firstValueFrom(this.http.post(`/api/servidor/ejecuciones/${ejecucion}/aprobar`, null)); }
  cancelar(ejecucion: number) { return firstValueFrom(this.http.post(`/api/servidor/ejecuciones/${ejecucion}/cancelar`, null)); }

  guardia() { return firstValueFrom(this.http.get<{ activa: boolean; telegram: boolean; runbooks: number }>('/api/servidor/guardia')); }
  ponerGuardia(activa: boolean) { return firstValueFrom(this.http.put('/api/servidor/guardia', { activa })); }
  probarGuardia() { return firstValueFrom(this.http.post<{ mensaje: string }>('/api/servidor/guardia/probar', null)); }

  linea(params: { desde?: string; hasta?: string; tipos?: string; q?: string; max?: number }) {
    const p: Record<string, string | number> = {};
    for (const [k, v] of Object.entries(params)) if (v !== undefined && v !== '') p[k] = v as string | number;
    return firstValueFrom(this.http.get<{ desde: string; hasta: string; sucesos: Suceso[] }>('/api/servidor/linea', { params: p }));
  }

  pendientes() { return firstValueFrom(this.http.get<Pendiente[]>('/api/auth/aprobacion/pendientes')); }
  aprobarEntrada(id: string, codigo: string) { return firstValueFrom(this.http.post(`/api/auth/aprobacion/${id}/aprobar`, { codigo })); }
  denegarEntrada(id: string) { return firstValueFrom(this.http.post(`/api/auth/aprobacion/${id}/denegar`, null)); }

  principal() { return firstValueFrom(this.http.get<{ principal: Principal | null }>('/api/pc/principal')); }
  ponerPrincipal(id: number | null) { return firstValueFrom(this.http.put('/api/pc/principal', { id })); }
  despertarPrincipal() { return firstValueFrom(this.http.post<{ mensaje: string }>('/api/pc/principal/despertar', null)); }
  aparatos() { return firstValueFrom(this.http.get<{ id: number; nombre: string; mac: string }[]>('/api/red')); }
}

// «hace 3 min», «hace 2 h»…
export function hace(iso: string, ahora = Date.now()): string {
  const m = Math.round((ahora - new Date(iso).getTime()) / 60000);
  return m < 1 ? 'ahora mismo' : m < 60 ? `hace ${m} min` : m < 1440 ? `hace ${Math.round(m / 60)} h` : `hace ${Math.round(m / 1440)} d`;
}

// los sucesos de la línea agrupados por día («hoy», «ayer», «lunes 10 de noviembre»), del más nuevo al más viejo
export function porDia(sucesos: Suceso[], ahora = new Date()): { dia: string; sucesos: Suceso[] }[] {
  const nombre = (d: Date) => {
    const hoy = new Date(ahora.getFullYear(), ahora.getMonth(), ahora.getDate()).getTime();
    const t = new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();
    return t === hoy ? 'Hoy' : t === hoy - 86400000 ? 'Ayer' : d.toLocaleDateString('es-ES', { weekday: 'long', day: 'numeric', month: 'long' });
  };
  const grupos: { dia: string; sucesos: Suceso[] }[] = [];
  for (const s of sucesos) {
    const dia = nombre(new Date(s.hora));
    const g = grupos.find((x) => x.dia === dia);
    if (g) g.sucesos.push(s); else grupos.push({ dia, sucesos: [s] });
  }
  return grupos;
}
