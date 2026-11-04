import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';

export interface ResultadoPin { ok: boolean; detalle?: string; restantes?: number; bloqueado?: boolean; }

// PIN de Seguridad: cuando el servidor contesta 403 «pin», el interceptor abre el teclado (app-pin) y repite la petición al acertar.
// Varias peticiones a la vez comparten un solo teclado. Si se cancela y estás en Documentos o Galería, vuelves a Inicio.
@Injectable({ providedIn: 'root' })
export class PinService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  readonly abierto = signal(false);
  private espera: Promise<boolean> | null = null;
  private resolver: ((ok: boolean) => void) | null = null;

  pedir(): Promise<boolean> {
    if (this.espera) return this.espera;
    this.abierto.set(true);
    return (this.espera = new Promise((res) => { this.resolver = res; }));
  }

  terminar(ok: boolean): void {
    this.abierto.set(false);
    const r = this.resolver; this.resolver = null; this.espera = null;
    r?.(ok);
    if (!ok && /^\/(documentos|galeria)/.test(this.router.url)) void this.router.navigateByUrl('/inicio');
  }

  async probar(pin: string): Promise<ResultadoPin> {
    try { await firstValueFrom(this.http.post('/api/seguridad/pin', { pin })); return { ok: true }; }
    catch (e) {
      const x = e as HttpErrorResponse;
      return { ok: false, detalle: x.error?.detalle ?? 'No se pudo comprobar el PIN.', restantes: x.error?.restantes, bloqueado: x.status === 429 };
    }
  }
}
