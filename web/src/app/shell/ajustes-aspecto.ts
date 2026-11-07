import { Component, inject } from '@angular/core';
import { DENSIDADES, ESCALA_MAX, ESCALA_MIN, FUENTES } from '../core/aspecto';
import { TemaService } from '../core/tema.service';

// «Letra y espacio» dentro de Ajustes: el tamaño de la letra (90–130 %), cuánto aire hay entre las cosas y la fuente. Se guarda en este dispositivo.
@Component({
  selector: 'app-ajustes-aspecto',
  template: `
    <section class="seccion" aria-labelledby="ajustes-aspecto">
      <h3 id="ajustes-aspecto">Letra y espacio</h3>
      <label class="regla"><span>Tamaño de la letra <output>{{ tema.escala() }} %</output></span>
        <input type="range" [min]="min" [max]="max" step="5" [value]="tema.escala()" (input)="tema.ponerEscala(+$any($event.target).value)"></label>
      <div class="grupo" role="group" aria-label="Densidad"><span>Espacio</span>
        @for (d of densidades; track d.id) { <button type="button" class="chip" [attr.aria-pressed]="tema.densidad() === d.id" (click)="tema.ponerDensidad(d.id)">{{ d.nombre }}</button> }</div>
      <div class="grupo" role="group" aria-label="Fuente"><span>Fuente</span>
        @for (f of fuentes; track f.id) { <button type="button" class="chip" [attr.aria-pressed]="tema.fuente() === f.id" [title]="f.nota" (click)="tema.ponerFuente(f.id)">{{ f.nombre }}</button> }</div>
      <p class="nota">El tamaño escala lo que va en rem; algunas cosas con medida fija (gráficas, escenas 3D) no cambian. Cada dispositivo guarda el suyo.</p>
      <button type="button" class="restablecer" (click)="tema.restablecerAspecto()">Volver a lo de siempre</button>
    </section>
  `,
  styles: `
    :host { display: block; }
    .seccion { display: flex; flex-direction: column; gap: 10px; }
    h3 { font-size: 12px; font-weight: 500; text-transform: uppercase; letter-spacing: .08em; color: var(--soft); margin: 0; }
    .regla { display: grid; gap: 6px; font-size: .9rem; } .regla output { color: var(--soft); margin-left: 6px; }
    .grupo { display: flex; flex-wrap: wrap; align-items: center; gap: 6px; font-size: .9rem; } .grupo > span { margin-right: 6px; }
    .chip { min-height: 40px; padding: 0 14px; border-radius: 999px; border: 1px solid var(--linea); background: none; color: var(--texto); font: inherit; cursor: pointer; }
    .chip[aria-pressed='true'] { background: var(--brand); border-color: var(--brand); color: var(--on-brand); }
    .nota { margin: 0; color: var(--soft); font-size: .82rem; line-height: 1.5; }
    .restablecer { align-self: flex-start; min-height: 40px; padding: 0 4px; border: 0; background: none; color: var(--brand); font: inherit; cursor: pointer; }
  `,
})
export class AjustesAspecto {
  protected readonly tema = inject(TemaService);
  protected readonly densidades = DENSIDADES;
  protected readonly fuentes = FUENTES;
  protected readonly min = ESCALA_MIN;
  protected readonly max = ESCALA_MAX;
}
