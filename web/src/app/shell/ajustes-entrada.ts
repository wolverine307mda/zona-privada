import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { faseLuna, nombreFase } from '../core/astro';
import { CieloConfig, FondoEntrada } from '../core/cielo-config';
import { CieloVivo } from '../entrar/cielo-vivo';

// «Pantalla de entrar» dentro de Ajustes: el fondo que se ve al entrar y al desbloquear (el candado 3D o el cielo de verdad desde tu sitio).
// Es un ajuste de este dispositivo. La vista previa deja correr el reloj para ver cómo queda el cielo a cualquier hora.
@Component({
  selector: 'app-ajustes-entrada',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CieloVivo],
  template: `
    <section class="seccion" aria-labelledby="ajustes-entrada">
      <h3 id="ajustes-entrada">Pantalla de entrar y de bloqueo</h3>
      <div class="opciones" role="group" aria-labelledby="ajustes-entrada">
        <button type="button" class="op" [attr.aria-pressed]="config.fondo() === 'candado'" (click)="elegir('candado')"><strong>Candado 3D</strong><small>El de siempre</small></button>
        <button type="button" class="op" [attr.aria-pressed]="config.fondo() === 'cielo'" (click)="elegir('cielo')"><strong>Cielo en vivo</strong><small>Sol y Luna donde están ahora</small></button>
      </div>

      @if (config.fondo() === 'cielo') {
        <div class="vista" aria-hidden="false">
          <app-cielo-vivo [fecha]="fecha()" />
          <span class="pie">{{ pieVista() }}</span>
        </div>
        <label class="hora"><span>Ver a otra hora <output>{{ horaTexto() }}</output></span>
          <input type="range" min="0" max="1439" step="10" [value]="minutos()" (input)="minutos.set(+$any($event.target).value); manual.set(true)" aria-label="Hora del día para la vista previa"></label>
        <button type="button" class="enlace" (click)="ahora()" [hidden]="!manual()">Volver a ahora</button>

        <p class="nota">Cielo de <strong>{{ config.lugar().nombre }}</strong> ({{ origenTexto() }}).</p>
        <div class="acciones">
          <button type="button" class="mini" (click)="delTiempo()" [disabled]="ocupado()">Usar el sitio del tiempo</button>
          <button type="button" class="mini" (click)="ubicacion()" [disabled]="ocupado()">Usar mi ubicación</button>
        </div>
        @if (mensaje()) { <p class="nota" role="status">{{ mensaje() }}</p> }
      }
    </section>
  `,
  styles: `
    :host { display: block; }
    .seccion { display: flex; flex-direction: column; gap: 12px; }
    h3 { font-size: 12px; font-weight: 500; text-transform: uppercase; letter-spacing: .08em; color: var(--soft); }
    .opciones { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 8px; }
    .op { display: grid; gap: 2px; text-align: left; padding: 12px; min-height: 56px; border-radius: 14px; border: 1px solid var(--vidrio-borde); background: none; color: inherit; font: inherit; cursor: pointer; }
    .op small { color: var(--soft); font-size: .75rem; }
    .op[aria-pressed='true'] { border-color: var(--brand); background: color-mix(in srgb, var(--brand) 14%, transparent); }
    .vista { position: relative; height: 170px; border-radius: 14px; overflow: hidden; border: 1px solid var(--vidrio-borde); }
    .pie { position: absolute; left: 10px; bottom: 8px; z-index: 2; font-size: .75rem; color: #fff; text-shadow: 0 1px 3px rgb(0 0 0 / .6); }
    .hora { display: grid; gap: 6px; font-size: .86rem; color: var(--soft); }
    .hora span { display: flex; justify-content: space-between; }
    .nota { margin: 0; color: var(--soft); font-size: .8rem; }
    .acciones { display: flex; flex-wrap: wrap; gap: 8px; }
    .mini { min-height: 40px; padding: 0 14px; border-radius: 999px; border: 1px solid var(--vidrio-borde); background: none; color: inherit; font: inherit; font-size: .82rem; cursor: pointer; }
    .mini:disabled { opacity: .5; }
    .enlace { justify-self: start; border: 0; background: none; color: var(--acento); font: inherit; font-size: .82rem; cursor: pointer; padding: 6px 0; }
  `,
})
export class AjustesEntrada {
  protected readonly config = inject(CieloConfig);
  protected readonly minutos = signal(minutosDeAhora());
  protected readonly manual = signal(false);
  protected readonly ocupado = signal(false);
  protected readonly mensaje = signal('');

  // la fecha de la vista previa: hoy a la hora elegida (si no tocas el deslizador, ahora mismo)
  protected readonly fecha = computed(() => {
    if (!this.manual()) return null;
    const d = new Date(); d.setHours(0, this.minutos(), 0, 0); return d;
  });
  protected readonly horaTexto = computed(() => `${String(Math.floor(this.minutos() / 60)).padStart(2, '0')}:${String(this.minutos() % 60).padStart(2, '0')}`);
  protected readonly pieVista = computed(() => { const f = faseLuna(this.fecha() ?? new Date()); return `${nombreFase(f.fase)} · ${Math.round(f.fraccion * 100)} %`; });
  protected readonly origenTexto = computed(() => ({ defecto: 'por defecto', servidor: 'el de tu tiempo', manual: 'tu ubicación' })[this.config.lugar().origen]);

  protected elegir(f: FondoEntrada): void { this.config.ponerFondo(f); }
  protected ahora(): void { this.manual.set(false); this.minutos.set(minutosDeAhora()); }

  protected async delTiempo(): Promise<void> {
    this.ocupado.set(true); this.mensaje.set('');
    const ok = await this.config.usarElDelTiempo();
    if (!ok) this.mensaje.set('Aún no has elegido un sitio en el tiempo (Inicio › Tiempo).');
    this.ocupado.set(false);
  }

  protected async ubicacion(): Promise<void> {
    this.ocupado.set(true); this.mensaje.set('');
    const ok = await this.config.usarMiUbicacion();
    if (!ok) this.mensaje.set('No me has dado permiso para saber dónde estás.');
    this.ocupado.set(false);
  }
}

function minutosDeAhora(): number { const d = new Date(); return d.getHours() * 60 + Math.floor(d.getMinutes() / 10) * 10; }
