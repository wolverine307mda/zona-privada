import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AparatoCasa, Catalogo, EstadoCasa, PanelGuardado, PuntoHistorial } from './modelo';

// Habla con /api/domotica y guarda el estado de la casa en una señal: las tarjetas se pintan solas al cambiar.
@Injectable({ providedIn: 'root' })
export class CasaService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/domotica';

  readonly estado = signal<EstadoCasa | null>(null);
  readonly catalogo = signal<Catalogo | null>(null);
  readonly error = signal('');

  async cargar(): Promise<void> {
    try {
      this.estado.set(await firstValueFrom(this.http.get<EstadoCasa>(`${this.base}/`)));
      this.error.set('');
    } catch {
      // si ya había datos, se dejan; solo se avisa
      this.error.set('No se pudo leer la casa. Se reintentará solo.');
    }
  }

  async cargarCatalogo(): Promise<void> {
    if (this.catalogo()) return;
    try { this.catalogo.set(await firstValueFrom(this.http.get<Catalogo>(`${this.base}/catalogo`))); } catch { /* se reintenta al abrir el diálogo */ }
  }

  // El valor se ve al instante y, si el aparato no obedece, vuelve a como estaba
  async ejecutar(a: AparatoCasa, clave: string, valor: string | number | boolean): Promise<boolean> {
    const antes = this.estado();
    this.poner(a.id, clave, String(valor));
    try {
      await firstValueFrom(this.http.post(`${this.base}/aparatos/${a.id}/ejecutar`, { clave, valor }));
      this.error.set('');
      return true;
    } catch (e) {
      this.estado.set(antes);
      const d = e instanceof HttpErrorResponse ? (e.error?.detail ?? e.error?.title) : null;
      this.error.set(typeof d === 'string' ? d : 'El aparato no contestó.');
      return false;
    }
  }

  private poner(id: number, clave: string, valor: string): void {
    this.estado.update((s) => s && ({
      ...s,
      aparatos: s.aparatos.map((a) => a.id === id ? { ...a, entidades: a.entidades.map((e) => e.clave === clave ? { ...e, valor } : e) } : a),
    }));
  }

  historial(id: number, clave: string, horas = 24): Promise<PuntoHistorial[]> {
    return firstValueFrom(this.http.get<PuntoHistorial[]>(`${this.base}/aparatos/${id}/historial`, { params: { clave, horas } }));
  }

  guardarPanel(p: PanelGuardado): Promise<unknown> { return firstValueFrom(this.http.put(`${this.base}/panel`, { panel: p })); }

  crearEstancia(nombre: string, icono: string) { return firstValueFrom(this.http.post(`${this.base}/estancias`, { nombre, icono })); }
  borrarEstancia(id: number) { return firstValueFrom(this.http.delete(`${this.base}/estancias/${id}`)); }
  crearAparato(nombre: string, tipo: string, driver: string, estanciaId: number | null) { return firstValueFrom(this.http.post(`${this.base}/aparatos`, { nombre, tipo, driver, estanciaId })); }
  editarAparato(id: number, nombre: string, estanciaId: number | null) { return firstValueFrom(this.http.put(`${this.base}/aparatos/${id}`, { nombre, estanciaId })); }
  borrarAparato(id: number) { return firstValueFrom(this.http.delete(`${this.base}/aparatos/${id}`)); }
  cargarEjemplo() { return firstValueFrom(this.http.post(`${this.base}/ejemplo`, null)); }
}
