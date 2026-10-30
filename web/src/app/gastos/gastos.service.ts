import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Estadisticas, Gasolina, Movimiento, Recurrente, ResumenMes, Tipo } from './modelo';

export type MovimientoEntrada = Omit<Movimiento, 'id' | 'recurrenteId'> & { recurrenteId?: number | null };
export type RecurrenteEntrada = Omit<Recurrente, 'id' | 'mensual'>;

// Errores de validación de la API (ValidationProblem): campo -> mensajes
export type Errores = Record<string, string[]>;
export const erroresDe = (e: unknown): Errores =>
  e instanceof HttpErrorResponse ? (e.error?.errors ?? { general: [e.error?.detail ?? 'No se pudo guardar.'] }) : { general: ['No hay conexión con el servidor.'] };

// Estado compartido del módulo: el mes que se está viendo, los diálogos y un contador para recargar
@Injectable({ providedIn: 'root' })
export class GastosService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/gastos';

  readonly anio = signal(new Date().getFullYear());
  readonly mes = signal(new Date().getMonth() + 1);
  readonly version = signal(0);

  // diálogos (los pinta la página de gastos)
  readonly editandoMovimiento = signal<{ movimiento?: Movimiento; tipo?: Tipo; categoria?: string } | null>(null);
  readonly editandoFijo = signal<{ fijo?: Recurrente; tipo?: Tipo; categoria?: string } | null>(null);

  recargar(): void { this.version.update((v) => v + 1); }

  moverMes(delta: number): void {
    const d = new Date(this.anio(), this.mes() - 1 + delta, 1);
    this.anio.set(d.getFullYear());
    this.mes.set(d.getMonth() + 1);
  }

  irAHoy(): void {
    this.anio.set(new Date().getFullYear());
    this.mes.set(new Date().getMonth() + 1);
  }

  esMesActual(): boolean { return this.anio() === new Date().getFullYear() && this.mes() === new Date().getMonth() + 1; }

  // ---- Lecturas ----
  resumen(anio: number, mes: number) { return firstValueFrom(this.http.get<ResumenMes>(`${this.base}/mes/${anio}/${mes}`)); }
  estadisticas(meses: number, anio: number, mes: number) { return firstValueFrom(this.http.get<Estadisticas>(`${this.base}/estadisticas`, { params: { meses, anio, mes } })); }
  gasolina() { return firstValueFrom(this.http.get<Gasolina>(`${this.base}/gasolina`)); }
  movimientos(filtro: { desde?: string; hasta?: string; categoria?: string; q?: string }) {
    const params: Record<string, string> = {};
    Object.entries(filtro).forEach(([k, v]) => { if (v) params[k] = v; });
    return firstValueFrom(this.http.get<Movimiento[]>(`${this.base}/movimientos`, { params }));
  }
  recurrentes() { return firstValueFrom(this.http.get<Recurrente[]>(`${this.base}/recurrentes`)); }

  // ---- Escrituras ----
  guardarMovimiento(m: MovimientoEntrada, id?: number) {
    return firstValueFrom(id ? this.http.put<Movimiento>(`${this.base}/movimientos/${id}`, m) : this.http.post<Movimiento>(`${this.base}/movimientos`, m));
  }
  borrarMovimiento(id: number) { return firstValueFrom(this.http.delete(`${this.base}/movimientos/${id}`)); }
  guardarFijo(r: RecurrenteEntrada, id?: number) {
    return firstValueFrom(id ? this.http.put<Recurrente>(`${this.base}/recurrentes/${id}`, r) : this.http.post<Recurrente>(`${this.base}/recurrentes`, r));
  }
  borrarFijo(id: number) { return firstValueFrom(this.http.delete(`${this.base}/recurrentes/${id}`)); }
  registrarFijo(id: number, fecha: string, importe?: number) {
    return firstValueFrom(this.http.post<Movimiento>(`${this.base}/recurrentes/${id}/registrar`, { fecha, importe: importe ?? null }));
  }
}
