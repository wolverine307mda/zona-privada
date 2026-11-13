import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export type TipoEvento = 'Evento' | 'Recordatorio';
export type Repeticion = 'No' | 'Diaria' | 'Semanal' | 'Mensual' | 'Anual';

// una aparición en el calendario: un evento propio (en la fecha de esa repetición) o uno de Google
export interface Aparicion {
  clave: string; id: number | null; origen: 'propio' | 'google'; tipo: TipoEvento; titulo: string; notas: string | null; lugar: string | null;
  inicio: string; fin: string | null; todoElDia: boolean; color: string; repetir: Repeticion; avisoMinutos: number | null;
  porCorreo: boolean; porWhatsApp: boolean; porPush: boolean; porTelegram?: boolean; hecho: boolean; enGoogle: boolean; url: string | null;
  // dónde está en Google (cuenta = clave de la conexión) y, en los que vienen de allí, el nombre de su calendario
  googleCuenta?: string | null; googleCalendario?: string | null; calendario?: string | null;
}
export interface EventoEntrada {
  tipo: TipoEvento; titulo: string; notas: string | null; lugar: string | null; inicio: string; fin: string | null; todoElDia: boolean;
  color: string; repetir: Repeticion; repetirHasta: string | null; avisoMinutos: number | null; porCorreo: boolean; porWhatsApp: boolean; porPush: boolean; porTelegram?: boolean; enGoogle: boolean; hecho: boolean;
  googleCuenta?: string | null; googleCalendario?: string | null;
}
// tu horario de trabajo: días 1 = lunes … 7 = domingo; horas «08:30»
export interface Horario { activo: boolean; nombre: string; dias: number[]; entrada: string; salida: string; }
// una cuenta de Google enlazada con sus calendarios (mostrar = se ve en la agenda; escribible = se puede copiar allí un evento)
export interface CalendarioGoogle { id: string; nombre: string; color: string | null; principal: boolean; escribible: boolean; mostrar: boolean; }
export interface CuentaGoogle { clave: string; cuenta: string | null; calendarios: CalendarioGoogle[]; }
export interface DestinoGoogle { cuenta: string; calendario: string; }
export interface EstadoGoogle { configurado: boolean; conectado: boolean; cuenta: string | null; cuentas: CuentaGoogle[]; defecto: DestinoGoogle | null; }
export interface EstadoAgenda { canales: { correo: boolean; whatsApp: boolean; telegram?: boolean }; google: EstadoGoogle; horario: Horario | null; }

// lo que se manda al guardar los ajustes: los calendarios marcados de cada cuenta y el de por defecto
export const ajustesGoogle = (cuentas: CuentaGoogle[], defecto: DestinoGoogle | null) => ({
  defecto,
  mostrar: Object.fromEntries(cuentas.map((c) => [c.clave, c.calendarios.filter((x) => x.mostrar).map((x) => x.id)])),
});

// los calendarios donde se puede copiar un evento: «cuenta|calendario» y cómo se llama en la lista
export const destinosGoogle = (cuentas: CuentaGoogle[]): { valor: string; texto: string }[] =>
  cuentas.flatMap((c) => c.calendarios.filter((x) => x.escribible).map((x) => ({
    valor: `${c.clave}|${x.id}`, texto: cuentas.length > 1 ? `${c.cuenta ?? c.clave} · ${x.nombre}` : x.nombre,
  })));
export const DIAS_SEMANA = [{ n: 1, c: 'L', l: 'lunes' }, { n: 2, c: 'M', l: 'martes' }, { n: 3, c: 'X', l: 'miércoles' }, { n: 4, c: 'J', l: 'jueves' }, { n: 5, c: 'V', l: 'viernes' }, { n: 6, c: 'S', l: 'sábado' }, { n: 7, c: 'D', l: 'domingo' }];
export const diaHorario = (d: Date) => (d.getDay() === 0 ? 7 : d.getDay());
// «L–V · 8:30–17:30»
export const textoHorario = (h: Horario) => {
  const d = [...h.dias].sort();
  const dias = d.length === 7 ? 'Todos los días' : d.join() === '1,2,3,4,5' ? 'L–V' : d.length > 2 && d[d.length - 1] - d[0] === d.length - 1
    ? `${DIAS_SEMANA[d[0] - 1].c}–${DIAS_SEMANA[d[d.length - 1] - 1].c}` : d.map((x) => DIAS_SEMANA[x - 1].c).join(' ');
  const c = (t: string) => t.replace(/^0/, '');
  return `${dias} · ${c(h.entrada)}–${c(h.salida)}`;
};

export const COLORES_EVENTO: { id: string; color: string }[] = [
  { id: '', color: 'var(--brand)' }, { id: 'naranja', color: '#ff8a3d' }, { id: 'rojo', color: '#ff5a5f' }, { id: 'rosa', color: '#ff5fa2' },
  { id: 'morado', color: '#9b6bff' }, { id: 'azul', color: '#4d8dff' }, { id: 'verde', color: '#3ddc84' }, { id: 'amarillo', color: '#ffc93c' },
];
// los eventos de Google traen el color de su calendario («#rrggbb»); los propios, uno de la lista
export const colorEvento = (id: string) => (id === 'google' ? '#4285f4' : /^#[0-9a-f]{6}$/i.test(id) ? id : COLORES_EVENTO.find((c) => c.id === id)?.color ?? 'var(--brand)');

export const REPETICIONES: { id: Repeticion; nombre: string }[] = [
  { id: 'No', nombre: 'No se repite' }, { id: 'Diaria', nombre: 'Cada día' }, { id: 'Semanal', nombre: 'Cada semana' },
  { id: 'Mensual', nombre: 'Cada mes' }, { id: 'Anual', nombre: 'Cada año' },
];
// avisos: minutos antes del inicio (en los de todo el día, contados desde las 00:00)
export const AVISOS_HORA: { min: number | null; nombre: string }[] = [
  { min: null, nombre: 'Sin aviso' }, { min: 0, nombre: 'A la hora' }, { min: 10, nombre: '10 minutos antes' }, { min: 30, nombre: '30 minutos antes' },
  { min: 60, nombre: '1 hora antes' }, { min: 180, nombre: '3 horas antes' }, { min: 1440, nombre: '1 día antes' }, { min: 10080, nombre: '1 semana antes' },
];
export const AVISOS_DIA: { min: number | null; nombre: string }[] = [
  { min: null, nombre: 'Sin aviso' }, { min: -540, nombre: 'El mismo día a las 9:00' }, { min: 900, nombre: 'El día antes a las 9:00' },
  { min: 3780, nombre: '3 días antes a las 9:00' }, { min: 9540, nombre: '1 semana antes a las 9:00' },
];

@Injectable({ providedIn: 'root' })
export class AgendaService {
  private readonly http = inject(HttpClient);

  rango(desde: Date, hasta: Date) {
    return firstValueFrom(this.http.get<{ google: boolean; apariciones: Aparicion[] }>('/api/agenda', { params: { desde: desde.toISOString(), hasta: hasta.toISOString() } }));
  }
  proximos(dias = 14) { return firstValueFrom(this.http.get<Aparicion[]>('/api/agenda/proximos', { params: { dias } })); }
  estado() { return firstValueFrom(this.http.get<EstadoAgenda>('/api/agenda/estado')); }
  guardar(e: EventoEntrada, id?: number) {
    return firstValueFrom(id ? this.http.put<{ id: number; googleError: string | null }>(`/api/agenda/${id}`, e) : this.http.post<{ id: number; googleError: string | null }>('/api/agenda', e));
  }
  hecho(id: number, hecho: boolean) { return firstValueFrom(this.http.post(`/api/agenda/${id}/hecho`, null, { params: { hecho } })); }
  borrar(id: number) { return firstValueFrom(this.http.delete(`/api/agenda/${id}`)); }
  horario() { return firstValueFrom(this.http.get<Horario | null>('/api/agenda/horario')); }
  guardarHorario(h: Horario) { return firstValueFrom(this.http.put<Horario>('/api/agenda/horario', h)); }
  desconectarGoogle(clave = 'google') { return firstValueFrom(this.http.delete(`/api/conexiones/${clave}`)); }
  guardarGoogle(cuentas: CuentaGoogle[], defecto: DestinoGoogle | null) { return firstValueFrom(this.http.put('/api/agenda/google', ajustesGoogle(cuentas, defecto))); }
}

// ---- fechas en hora local ----
export const iso = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
export const hora = (d: Date) => `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`;
export const mismoDia = (a: Date, b: Date) => a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate();
