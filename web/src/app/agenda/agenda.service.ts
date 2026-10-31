import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export type TipoEvento = 'Evento' | 'Recordatorio';
export type Repeticion = 'No' | 'Diaria' | 'Semanal' | 'Mensual' | 'Anual';

// una aparición en el calendario: un evento propio (en la fecha de esa repetición) o uno de Google
export interface Aparicion {
  clave: string; id: number | null; origen: 'propio' | 'google'; tipo: TipoEvento; titulo: string; notas: string | null; lugar: string | null;
  inicio: string; fin: string | null; todoElDia: boolean; color: string; repetir: Repeticion; avisoMinutos: number | null;
  porCorreo: boolean; porWhatsApp: boolean; porPush: boolean; hecho: boolean; enGoogle: boolean; url: string | null;
}
export interface EventoEntrada {
  tipo: TipoEvento; titulo: string; notas: string | null; lugar: string | null; inicio: string; fin: string | null; todoElDia: boolean;
  color: string; repetir: Repeticion; repetirHasta: string | null; avisoMinutos: number | null; porCorreo: boolean; porWhatsApp: boolean; porPush: boolean; enGoogle: boolean; hecho: boolean;
}
export interface EstadoAgenda { canales: { correo: boolean; whatsApp: boolean }; google: { configurado: boolean; conectado: boolean; cuenta: string | null }; }

export const COLORES_EVENTO: { id: string; color: string }[] = [
  { id: '', color: 'var(--brand)' }, { id: 'naranja', color: '#ff8a3d' }, { id: 'rojo', color: '#ff5a5f' }, { id: 'rosa', color: '#ff5fa2' },
  { id: 'morado', color: '#9b6bff' }, { id: 'azul', color: '#4d8dff' }, { id: 'verde', color: '#3ddc84' }, { id: 'amarillo', color: '#ffc93c' },
];
export const colorEvento = (id: string) => (id === 'google' ? '#4285f4' : COLORES_EVENTO.find((c) => c.id === id)?.color ?? 'var(--brand)');

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
  desconectarGoogle() { return firstValueFrom(this.http.delete('/api/conexiones/google')); }
}

// ---- fechas en hora local ----
export const iso = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
export const hora = (d: Date) => `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`;
export const mismoDia = (a: Date, b: Date) => a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate();
