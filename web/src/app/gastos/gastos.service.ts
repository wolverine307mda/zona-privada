import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Consejos, Deudas, PrevisionCierre, Estadisticas, Frecuencia, Gasolina, Hucha, Mantenimiento, Movimiento, Presupuesto, Recurrente, ResumenMes, Saldo, Tipo, PresupuestosSemana, EstadoCobro } from './modelo';
import { RecargaService } from '../core/recarga';

export type MovimientoEntrada = Omit<Movimiento, 'id' | 'recurrenteId' | 'tieneTicket' | 'moneda' | 'importeOriginal'> & { moneda?: string | null; importeOriginal?: number | null } & { recurrenteId?: number | null; repite?: Frecuencia | null; variable?: boolean };
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
  readonly editandoSaldo = signal(false);

  recargar(): void { this.version.update((v) => v + 1); }

  constructor() { inject(RecargaService).alRecargar(() => this.recargar(), inject(DestroyRef)); }

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
  deudas() { return firstValueFrom(this.http.get<Deudas>(`${this.base}/deudas`)); }
  guardarDeuda(d: { nombre: string; total: number; categoria: string; inicio: string; notas: string | null }, id?: number) {
    return firstValueFrom(id ? this.http.put(`${this.base}/deudas/${id}`, d) : this.http.post(`${this.base}/deudas`, d));
  }
  borrarDeuda(id: number) { return firstValueFrom(this.http.delete(`${this.base}/deudas/${id}`)); }
  pagarDeuda(id: number, p: { importe: number; fecha: string; concepto?: string; notas?: string }) {
    return firstValueFrom(this.http.post<Movimiento>(`${this.base}/deudas/${id}/pagos`, p));
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
  // `periodo` (2026-11): el mes de la ocurrencia que cubre, si el cobro cae en otro (cobrada antes de su día)
  registrarFijo(id: number, fecha: string, importe?: number, periodo?: string) {
    return firstValueFrom(this.http.post<Movimiento>(`${this.base}/recurrentes/${id}/registrar`, { fecha, importe: importe ?? null, periodo: periodo ?? null }));
  }

  // ---- Saldo, huchas, presupuestos y consejos ----
  saldo(anio?: number, mes?: number) {
    return firstValueFrom(this.http.get<Saldo>(`${this.base}/saldo`, { params: anio && mes ? { anio, mes } : {} }));
  }
  ponerSaldo(actual: number) { return firstValueFrom(this.http.put(`${this.base}/saldo`, { actual })); }
  huchas() { return firstValueFrom(this.http.get<Hucha[]>(`${this.base}/huchas`)); }
  guardarHucha(h: { nombre: string; objetivo: number; color: string; emoji: string; fecha: string | null }, id?: number) {
    return firstValueFrom(id ? this.http.put(`${this.base}/huchas/${id}`, h) : this.http.post(`${this.base}/huchas`, h));
  }
  aportar(id: number, importe: number) { return firstValueFrom(this.http.post(`${this.base}/huchas/${id}/aportar`, { importe })); }
  borrarHucha(id: number) { return firstValueFrom(this.http.delete(`${this.base}/huchas/${id}`)); }
  presupuestos(anio: number, mes: number) { return firstValueFrom(this.http.get<Presupuesto[]>(`${this.base}/presupuestos`, { params: { anio, mes } })); }
  ponerPresupuesto(categoria: string, limite: number) { return firstValueFrom(this.http.put(`${this.base}/presupuestos/${categoria}`, { limite })); }
  quitarPresupuesto(categoria: string) { return firstValueFrom(this.http.delete(`${this.base}/presupuestos/${categoria}`)); }
  cobro() { return firstValueFrom(this.http.get<EstadoCobro>(`${this.base}/cobro`)); }
  ponerCobro(dia: number | null) { return firstValueFrom(this.http.put<EstadoCobro>(`${this.base}/cobro`, { dia })); }
  presupuestosSemana() { return firstValueFrom(this.http.get<PresupuestosSemana>(`${this.base}/presupuestos-semana`)); }
  ponerPresupuestoSemana(categoria: string, limite: number, arrastrar: boolean) { return firstValueFrom(this.http.put(`${this.base}/presupuestos-semana/${categoria}`, { limite, arrastrar })); }
  quitarPresupuestoSemana(categoria: string) { return firstValueFrom(this.http.delete(`${this.base}/presupuestos-semana/${categoria}`)); }
  prevision() { return firstValueFrom(this.http.get<PrevisionCierre>(`${this.base}/prevision`)); }
  consejos() { return firstValueFrom(this.http.get<Consejos>(`${this.base}/consejos`)); }

  // ---- Tickets ----
  subirTicket(id: number, foto: Blob) {
    return firstValueFrom(this.http.put(`${this.base}/movimientos/${id}/ticket`, foto, { headers: { 'Content-Type': foto.type || 'image/jpeg' } }));
  }
  borrarTicket(id: number) { return firstValueFrom(this.http.delete(`${this.base}/movimientos/${id}/ticket`)); }
  urlTicket(id: number, v = 0) { return `${this.base}/movimientos/${id}/ticket?v=${v}`; }

  // ---- Mantenimiento del coche ----
  mantenimiento() { return firstValueFrom(this.http.get<{ kmActual: number | null; lista: Mantenimiento[] }>(`${this.base}/mantenimiento`)); }
  guardarMantenimiento(m: { nombre: string; cadaKm: number | null; cadaMeses: number | null; ultimaFecha: string | null; ultimoKm: number | null; notas: string | null }, id?: number) {
    return firstValueFrom(id ? this.http.put(`${this.base}/mantenimiento/${id}`, m) : this.http.post(`${this.base}/mantenimiento`, m));
  }
  mantenimientoHecho(id: number, fecha?: string, km?: number | null) {
    return firstValueFrom(this.http.post(`${this.base}/mantenimiento/${id}/hecho`, { fecha: fecha ?? null, km: km ?? null }));
  }
  borrarMantenimiento(id: number) { return firstValueFrom(this.http.delete(`${this.base}/mantenimiento/${id}`)); }
}

// Reduce una foto (del móvil pueden ser 12 MP) a JPEG de 1600 px como mucho: el ticket se lee igual y pesa ~300 KB
export async function reducirFoto(f: File, max = 1600, calidad = 0.82): Promise<Blob> {
  try {
    const img = await createImageBitmap(f);
    const k = Math.min(1, max / Math.max(img.width, img.height));
    const c = document.createElement('canvas');
    c.width = Math.round(img.width * k);
    c.height = Math.round(img.height * k);
    c.getContext('2d')!.drawImage(img, 0, 0, c.width, c.height);
    return await new Promise((ok) => c.toBlob((b) => ok(b ?? f), 'image/jpeg', calidad));
  } catch {
    return f; // formato que el navegador no sabe abrir (HEIC en algunos): se manda tal cual
  }
}
