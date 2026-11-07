import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { EstadoError, Esqueleto, Interruptor } from '../ui';
import { MapaEquipos } from './mapa-equipos';

interface Equipo {
  id: number; nombre: string; pais: string; ciudad: string; ultimaIp: string; primera: string; ultima: string; entradas: number; confianza: boolean;
  sospechoso: boolean; estado: 'en-linea' | 'reciente' | 'hace-tiempo'; visto: string;
}

// Los equipos desde los que has entrado (sistema + navegador + país). Cuando entra uno que no está aquí, te llega un aviso; si además es de un país
// donde nunca habías entrado, el aviso lo dice. «No lo reconozco» cierra sus sesiones y lo marca como sospechoso. Olvidar un equipo hace que su próxima entrada vuelva a avisar.
@Component({
  selector: 'app-equipos',
  imports: [Esqueleto, EstadoError, Interruptor, RouterLink, MapaEquipos],
  styleUrls: ['../gastos/comun.css'],
  template: `
    <section class="caja">
      <header class="caja__cab"><h3>Equipos conocidos</h3><span class="sub">Si entra uno que no esté aquí, te avisa</span></header>
      @if (mensaje()) {
        <p class="aviso" role="status">{{ mensaje() }} <a routerLink="/perfil">Cambiar la contraseña →</a></p>
      }
      @if (fallo()) { <app-estado-error titulo="No pude leer los equipos" (reintentar)="cargar()" /> }
      @else if (equipos() === null) { <app-esqueleto forma="linea" /><app-esqueleto forma="linea" /> }
      @else {
        <ul class="lista">
          @for (e of equipos()!; track e.id) {
            <li [class.lista__mal]="e.sospechoso">
              <span class="lista__texto">
                <strong><i class="punto punto--{{ e.estado }}" [attr.aria-label]="etiquetaEstado(e)" role="img"></i> {{ e.nombre }}@if (e.sospechoso) { <em class="mal">no reconocido</em> }</strong>
                <span>{{ e.visto }} · {{ e.entradas }} {{ e.entradas === 1 ? 'entrada' : 'entradas' }} · {{ e.ultimaIp }}</span>
              </span>
              <app-interruptor class="lista__confianza" [activo]="e.confianza" [etiqueta]="'De confianza: ' + e.nombre" (activoChange)="confianza(e, $event)" title="De confianza: no avisa si cambia de país" />
              <span class="lista__acciones">
                @if (!e.sospechoso) {
                  <button class="enlace enlace--mal" type="button" (click)="noLoReconozco(e)" [attr.aria-label]="'No reconozco ' + e.nombre">{{ confirmando() === e.id ? 'Seguro: cerrar sesiones' : 'No lo reconozco' }}</button>
                }
                <button class="enlace" type="button" (click)="olvidar(e)" [attr.aria-label]="'Olvidar ' + e.nombre">Olvidar</button>
              </span>
            </li>
          } @empty { <p class="nada">Aún no hay equipos: se van apuntando según entras.</p> }
        </ul>
        @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
      }
    </section>
    <app-mapa-equipos />
  `,
  styles: `
    .lista { display: flex; flex-direction: column; gap: 4px; list-style: none; margin: 0; padding: 0; }
    .lista li { display: grid; grid-template-columns: minmax(0, 1fr) auto auto; align-items: center; gap: 12px; padding: 8px 4px; border-bottom: 1px solid var(--linea); }
    .lista li:last-child { border-bottom: 0; }
    .lista__mal { background: color-mix(in srgb, #ff4d4d 8%, transparent); border-radius: 10px; }
    .lista__texto { display: grid; min-width: 0; }
    .lista__texto span { color: var(--soft); font-size: .8rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .lista__confianza { margin: 0; }
    .lista__acciones { display: flex; flex-direction: column; align-items: flex-end; gap: 2px; }
    .enlace--mal { color: var(--bad, #ff5a5a); }
    .punto { display: inline-block; width: 9px; height: 9px; border-radius: 50%; margin-right: 6px; background: var(--soft); }
    .punto--en-linea { background: #2fbf71; box-shadow: 0 0 0 3px color-mix(in srgb, #2fbf71 25%, transparent); }
    .punto--reciente { background: #e0a82e; }
    .mal { margin-left: 8px; font-style: normal; font-size: .72rem; padding: 2px 8px; border-radius: 999px; background: color-mix(in srgb, #ff4d4d 25%, transparent); }
    .aviso { margin: 0 0 8px; padding: 10px 12px; border-radius: 12px; background: var(--tarjeta-2); font-size: .88rem; }
    @media (max-width: 560px) { .lista li { grid-template-columns: minmax(0, 1fr) auto; } .lista__acciones { grid-column: 1 / -1; flex-direction: row; justify-content: flex-end; gap: 14px; } }
  `,
})
export class Equipos implements OnInit {
  private readonly http = inject(HttpClient);
  protected readonly equipos = signal<Equipo[] | null>(null);
  protected readonly fallo = signal(false);
  protected readonly confirmando = signal<number | null>(null);
  protected readonly mensaje = signal('');
  protected readonly error = signal('');

  ngOnInit(): void { void this.cargar(); }

  protected async cargar(): Promise<void> {
    this.fallo.set(false);
    try { this.equipos.set(await firstValueFrom(this.http.get<Equipo[]>('/api/seguridad/equipos'))); } catch { this.fallo.set(true); }
  }

  protected etiquetaEstado(e: Equipo): string { return e.estado === 'en-linea' ? 'en línea' : e.estado === 'reciente' ? 'visto hace poco' : 'visto hace tiempo'; }

  protected async confianza(e: Equipo, v: boolean): Promise<void> {
    this.equipos.update((l) => l?.map((x) => (x.id === e.id ? { ...x, confianza: v, sospechoso: v ? false : x.sospechoso } : x)) ?? null);
    try { await firstValueFrom(this.http.put(`/api/seguridad/equipos/${e.id}/confianza`, { confianza: v })); } catch { void this.cargar(); }
  }

  // dos toques: el primero pide confirmar (cierra sesiones, no se puede deshacer)
  protected async noLoReconozco(e: Equipo): Promise<void> {
    if (this.confirmando() !== e.id) { this.confirmando.set(e.id); this.error.set(''); return; }
    this.confirmando.set(null); this.error.set('');
    try {
      const r = await firstValueFrom(this.http.post<{ cerradas: number; ip: string; lugar: string }>(`/api/seguridad/equipos/${e.id}/no-reconozco`, null));
      this.mensaje.set(`«${e.nombre}» (${r.ip}, ${r.lugar}) marcado como no reconocido: ${r.cerradas} ${r.cerradas === 1 ? 'sesión cerrada' : 'sesiones cerradas'}. Te he avisado por Telegram.`);
      await this.cargar();
    } catch (x) {
      this.error.set(x instanceof HttpErrorResponse && x.error?.detalle ? x.error.detalle : 'No se pudo marcar.');
    }
  }

  protected async olvidar(e: Equipo): Promise<void> {
    try { await firstValueFrom(this.http.delete(`/api/seguridad/equipos/${e.id}`)); this.equipos.update((l) => l?.filter((x) => x.id !== e.id) ?? null); } catch { void this.cargar(); }
  }
}
