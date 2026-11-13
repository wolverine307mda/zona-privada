import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type { TipoEquipo } from '../inicio/pc-3d';

export const TIPOS_EQUIPO: { id: TipoEquipo; nombre: string; detalle: string }[] = [
  { id: 'portatil', nombre: 'Portátil', detalle: 'con batería y gráfica' },
  { id: 'minipc', nombre: 'Mini PC', detalle: 'sobremesa, sin batería' },
];

// El tipo de equipo que hospeda la app (portátil o mini PC): lo guarda el servidor y cambia el dibujo de «Tu PC por dentro».
// Se lee una vez y se comparte entre Inicio, PC y Ajustes; hasta que llega, se dibuja un portátil.
@Injectable({ providedIn: 'root' })
export class EquipoService {
  private readonly http = inject(HttpClient);
  readonly tipo = signal<TipoEquipo>('portatil');
  private cargado = false;

  /** lee el tipo del servidor la primera vez; sin respuesta se queda el portátil de siempre */
  cargar(): void {
    if (this.cargado) return;
    this.cargado = true;
    firstValueFrom(this.http.get<{ tipo: TipoEquipo }>('/api/equipo'))
      .then((r) => this.tipo.set(r.tipo === 'minipc' ? 'minipc' : 'portatil'))
      .catch(() => { this.cargado = false; });
  }

  async poner(tipo: TipoEquipo): Promise<void> {
    await firstValueFrom(this.http.put('/api/equipo', { tipo }));
    this.tipo.set(tipo);
  }
}
