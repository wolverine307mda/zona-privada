import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { SonidoService } from '../core/sonido.service';

// «Sonido» dentro de Ajustes: un tono ambiental ligado a la carga del PC. Apagado por defecto.
@Component({
  selector: 'app-ajustes-sonido',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="seccion" aria-labelledby="ajustes-sonido">
      <h3 id="ajustes-sonido">Sonido</h3>
      <label class="interruptor-fila">
        <span><strong>Tono ambiental</strong><small>Suena más agudo y brillante cuanto más trabaja tu PC. Solo suena con la app abierta.</small></span>
        <input type="checkbox" role="switch" [checked]="sonido.activo()" (change)="sonido.ponerActivo($any($event.target).checked)" aria-label="Activar el tono ambiental">
      </label>
      @if (sonido.activo()) {
        <label class="regla"><span>Volumen <output>{{ sonido.volumen() }} %</output></span>
          <input type="range" min="5" max="100" step="5" [value]="sonido.volumen()" (input)="sonido.ponerVolumen(+$any($event.target).value)"></label>
      }
    </section>
  `,
  styles: `
    :host { display: block; }
    .seccion { display: flex; flex-direction: column; gap: 12px; }
    h3 { font-size: 12px; font-weight: 500; text-transform: uppercase; letter-spacing: .08em; color: var(--soft); }
    .interruptor-fila { display: flex; align-items: center; justify-content: space-between; gap: 14px; min-height: 44px; }
    .interruptor-fila span { display: grid; gap: 2px; }
    .interruptor-fila small { color: var(--soft); font-size: .78rem; }
    .interruptor-fila input { width: 22px; height: 22px; accent-color: var(--brand); flex: none; }
    .regla { display: grid; gap: 6px; font-size: .86rem; color: var(--soft); }
    .regla span { display: flex; justify-content: space-between; }
  `,
})
export class AjustesSonido {
  protected readonly sonido = inject(SonidoService);
}
