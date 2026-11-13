import { Component, inject, signal } from '@angular/core';
import { EquipoService, TIPOS_EQUIPO } from '../core/equipo.service';
import type { TipoEquipo } from '../inicio/pc-3d';

// «Tipo de equipo» dentro de Ajustes: si la app corre en un portátil o en un mini PC. Cambia el dibujo en 3D de Inicio y PC
// (el mini PC no lleva batería). Se guarda en el servidor, porque es del equipo y no de este navegador.
@Component({
  selector: 'app-ajustes-equipo',
  template: `
    <section class="seccion" aria-labelledby="ajustes-equipo">
      <h3 id="ajustes-equipo">Tipo de equipo</h3>
      <p class="nota">Dónde corre la zona privada. Cambia el dibujo en 3D de «Tu PC por dentro» en Inicio y en PC. Se guarda en el servidor: cuando la app pase al mini PC, solo hay que elegirlo aquí.</p>
      <div class="grupo" role="radiogroup" aria-label="Tipo de equipo">
        @for (t of tipos; track t.id) {
          <button type="button" role="radio" class="opcion" [attr.aria-checked]="equipo.tipo() === t.id" [disabled]="guardando()" (click)="elegir(t.id)">
            <svg class="icon" viewBox="0 0 24 24" aria-hidden="true">
              @if (t.id === 'portatil') { <path d="M5 6h14v9H5zM2 18h20l-1.5-3h-17z" /> } @else { <path d="M4 12h16v6H4zM6 12v-1.5a2 2 0 0 1 2-2h8a2 2 0 0 1 2 2V12M8 15h.01M16 15h2" /> }
            </svg>
            <span><strong>{{ t.nombre }}</strong><small>{{ t.detalle }}</small></span>
          </button>
        }
      </div>
      <span class="estado" role="status">{{ mensaje() }}</span>
    </section>
  `,
  styles: `
    :host { display: block; }
    .seccion { display: flex; flex-direction: column; gap: 10px; }
    h3 { font-size: 12px; font-weight: 500; text-transform: uppercase; letter-spacing: .08em; color: var(--soft); margin: 0; }
    .nota { margin: 0; color: var(--soft); font-size: .82rem; line-height: 1.5; }
    .grupo { display: flex; flex-wrap: wrap; gap: 8px; }
    .opcion { display: inline-flex; align-items: center; gap: 12px; min-height: 52px; padding: 0 18px 0 14px; border-radius: 16px; border: 1px solid var(--linea); background: none; color: var(--texto); font: inherit; text-align: left; cursor: pointer; }
    .opcion span { display: grid; gap: 2px; } .opcion small { color: var(--soft); font-size: .78rem; }
    .opcion[aria-checked='true'] { background: var(--brand); border-color: var(--brand); color: var(--on-brand); }
    .opcion[aria-checked='true'] small { color: inherit; opacity: .8; }
    .icon { width: 26px; height: 26px; fill: none; stroke: currentColor; stroke-width: 1.7; stroke-linecap: round; stroke-linejoin: round; flex: none; }
    .estado { font-size: .85rem; color: var(--soft); }
  `,
})
export class AjustesEquipo {
  protected readonly equipo = inject(EquipoService);
  protected readonly tipos = TIPOS_EQUIPO;
  protected readonly guardando = signal(false);
  protected readonly mensaje = signal('');

  constructor() { this.equipo.cargar(); }

  protected async elegir(t: TipoEquipo): Promise<void> {
    if (t === this.equipo.tipo()) return;
    this.guardando.set(true); this.mensaje.set('');
    try { await this.equipo.poner(t); this.mensaje.set(t === 'minipc' ? 'Guardado: ahora se dibuja un mini PC.' : 'Guardado: ahora se dibuja un portátil.'); }
    catch { this.mensaje.set('No se pudo guardar. Prueba otra vez.'); }
    finally { this.guardando.set(false); }
  }
}
