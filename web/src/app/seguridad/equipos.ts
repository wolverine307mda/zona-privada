import { HttpClient } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { EstadoError, Esqueleto } from '../ui';

interface Equipo { id: number; nombre: string; pais: string; ciudad: string; ultimaIp: string; primera: string; ultima: string; entradas: number; confianza: boolean; }

// Los equipos desde los que has entrado (sistema + navegador + país). Cuando entra uno que no está aquí, te llega un aviso; si además es de un país
// donde nunca habías entrado, el aviso lo dice. Olvidar un equipo hace que su próxima entrada vuelva a avisar.
@Component({
  selector: 'app-equipos',
  imports: [Esqueleto, EstadoError],
  styleUrls: ['../gastos/comun.css'],
  template: `
    <section class="caja">
      <header class="caja__cab"><h3>Equipos conocidos</h3><span class="sub">Si entra uno que no esté aquí, te avisa</span></header>
      @if (fallo()) { <app-estado-error titulo="No pude leer los equipos" (reintentar)="cargar()" /> }
      @else if (equipos() === null) { <app-esqueleto forma="linea" /><app-esqueleto forma="linea" /> }
      @else {
        <ul class="lista">
          @for (e of equipos()!; track e.id) {
            <li>
              <span class="lista__texto">
                <strong>{{ e.nombre }}</strong>
                <span>{{ lugar(e) }} · {{ e.entradas }} {{ e.entradas === 1 ? 'entrada' : 'entradas' }} · visto {{ visto(e.ultima) }}</span>
              </span>
              <label class="interruptor lista__confianza" [title]="'Equipo de confianza: no avisa si cambia de país'">
                <input type="checkbox" [checked]="e.confianza" (change)="confianza(e, $any($event.target).checked)" [attr.aria-label]="'De confianza: ' + e.nombre">
                <span class="interruptor__pista" aria-hidden="true"></span>
              </label>
              <button class="enlace" type="button" (click)="olvidar(e)" [attr.aria-label]="'Olvidar ' + e.nombre">Olvidar</button>
            </li>
          } @empty { <p class="nada">Aún no hay equipos: se van apuntando según entras.</p> }
        </ul>
      }
    </section>
  `,
  styles: `
    .lista { display: flex; flex-direction: column; gap: 4px; list-style: none; margin: 0; padding: 0; }
    .lista li { display: grid; grid-template-columns: minmax(0, 1fr) auto auto; align-items: center; gap: 12px; padding: 8px 4px; border-bottom: 1px solid var(--linea); }
    .lista li:last-child { border-bottom: 0; }
    .lista__texto { display: grid; min-width: 0; }
    .lista__texto span { color: var(--soft); font-size: .8rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .lista__confianza { margin: 0; }
  `,
})
export class Equipos implements OnInit {
  private readonly http = inject(HttpClient);
  protected readonly equipos = signal<Equipo[] | null>(null);
  protected readonly fallo = signal(false);

  ngOnInit(): void { void this.cargar(); }

  protected async cargar(): Promise<void> {
    this.fallo.set(false);
    try { this.equipos.set(await firstValueFrom(this.http.get<Equipo[]>('/api/seguridad/equipos'))); } catch { this.fallo.set(true); }
  }

  protected lugar(e: Equipo): string { return e.pais === '?' ? 'lugar desconocido' : e.ciudad ? `${e.ciudad}, ${e.pais}` : e.pais; }
  protected visto(iso: string): string { return new Date(iso).toLocaleDateString('es-ES', { day: 'numeric', month: 'short' }); }

  protected async confianza(e: Equipo, v: boolean): Promise<void> {
    this.equipos.update((l) => l?.map((x) => (x.id === e.id ? { ...x, confianza: v } : x)) ?? null);
    try { await firstValueFrom(this.http.put(`/api/seguridad/equipos/${e.id}/confianza`, { confianza: v })); } catch { void this.cargar(); }
  }

  protected async olvidar(e: Equipo): Promise<void> {
    try { await firstValueFrom(this.http.delete(`/api/seguridad/equipos/${e.id}`)); this.equipos.update((l) => l?.filter((x) => x.id !== e.id) ?? null); } catch { void this.cargar(); }
  }
}
