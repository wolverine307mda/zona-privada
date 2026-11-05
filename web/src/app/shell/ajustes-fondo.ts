import { Component, inject } from '@angular/core';
import { AMBITOS, FONDOS, TemaService } from '../core/tema.service';

// «Fondo» dentro de Ajustes: elegir el fondo animado de toda la app, cuánto se ve, cuánto se mueve y dónde.
@Component({
  selector: 'app-ajustes-fondo',
  template: `
    <section class="seccion" aria-labelledby="ajustes-fondo">
      <h3 id="ajustes-fondo">Fondo</h3>
      <div class="fondos" role="group" aria-labelledby="ajustes-fondo">
        @for (f of fondos; track f.id) {
          <button type="button" class="fondo" [attr.data-fondo]="f.id" [attr.aria-pressed]="tema.fondo() === f.id" (click)="tema.ponerFondo(f.id)">
            <span class="fondo__vista" aria-hidden="true"></span>
            <span class="fondo__nombre">{{ f.nombre }}</span>
            <span class="fondo__nota">{{ f.nota }}</span>
          </button>
        }
      </div>
      @if (tema.fondo() !== 'aura' && tema.fondo() !== 'ninguno') {
        <div class="reglas">
          <label class="regla"><span>Cuánto se ve <output>{{ tema.intensidad() }} %</output></span>
            <input type="range" min="5" max="100" step="5" [value]="tema.intensidad()" (input)="tema.ponerIntensidad(+$any($event.target).value)"></label>
          <label class="regla"><span>Velocidad <output>{{ tema.velocidad() }} %</output></span>
            <input type="range" min="25" max="200" step="5" [value]="tema.velocidad()" (input)="tema.ponerVelocidad(+$any($event.target).value)"></label>
        </div>
      }
      @if (tema.fondo() !== 'ninguno') {
        <div class="ambito" role="group" aria-label="Dónde se ve el fondo">
          <span>Dónde se ve</span>
          @for (a of ambitos; track a.id) {
            <button type="button" class="chip" [attr.aria-pressed]="tema.ambito() === a.id" (click)="tema.ponerAmbito(a.id)">{{ a.nombre }}</button>
          }
        </div>
        <p class="nota">En el móvil el fondo siempre va detrás de todo. Se guarda en este navegador.</p>
      }
    </section>
  `,
  styles: `
    :host { display: block; }
    .seccion { display: flex; flex-direction: column; gap: 12px; }
    h3 { font-size: 12px; font-weight: 500; text-transform: uppercase; letter-spacing: .08em; color: var(--soft); }
    .fondos { display: grid; grid-template-columns: repeat(auto-fill, minmax(104px, 1fr)); gap: 10px; }
    .fondo { display: flex; flex-direction: column; align-items: stretch; gap: 4px; padding: 8px; text-align: left; border-radius: 14px; border: 2px solid var(--linea); background: transparent; color: inherit; font: inherit; cursor: pointer; transition: border-color 200ms ease, transform 200ms var(--ease-out); }
    .fondo[aria-pressed='true'] { border-color: var(--brand); }
    @media (hover: hover) { .fondo:hover { transform: translateY(-2px); } }
    .fondo__vista { height: 54px; border-radius: 8px; background-color: var(--tarjeta-2); background-size: cover; }
    .fondo__nombre { font-size: .86rem; font-weight: 600; }
    .fondo__nota { font-size: .72rem; line-height: 1.25; color: var(--soft); }
    /* una miniatura de cada fondo, solo con CSS y el color de acento */
    [data-fondo='aura'] .fondo__vista { background-image: radial-gradient(60% 80% at 20% 10%, color-mix(in srgb, var(--brand) 55%, transparent), transparent 70%), radial-gradient(60% 70% at 90% 100%, color-mix(in srgb, var(--brand) 25%, #3a5bff), transparent 70%); }
    [data-fondo='liquido'] .fondo__vista { background-image: radial-gradient(circle at 28% 40%, var(--brand) 0 14px, transparent 15px), radial-gradient(circle at 44% 52%, var(--brand) 0 11px, transparent 12px), radial-gradient(circle at 76% 62%, color-mix(in srgb, var(--brand) 40%, #3a5bff) 0 13px, transparent 14px); filter: blur(.4px); }
    [data-fondo='isometrico'] .fondo__vista { background-image: linear-gradient(30deg, color-mix(in srgb, var(--brand) 35%, transparent) 12%, transparent 12.5% 87%, color-mix(in srgb, var(--brand) 35%, transparent) 87.5%), linear-gradient(150deg, color-mix(in srgb, var(--brand) 35%, transparent) 12%, transparent 12.5% 87%, color-mix(in srgb, var(--brand) 35%, transparent) 87.5%); background-size: 28px 48px; }
    [data-fondo='comic'] .fondo__vista { background-image: radial-gradient(circle, var(--brand) 2.4px, transparent 3px); background-size: 12px 12px; }
    [data-fondo='pixel'] .fondo__vista { background-image: linear-gradient(var(--brand), var(--brand)), linear-gradient(var(--brand), var(--brand)), linear-gradient(var(--brand), var(--brand)); background-size: 18px 12px, 12px 6px, 6px 6px; background-position: 0 100%, 22px 100%, 70% 20%; background-repeat: no-repeat; image-rendering: pixelated; }
    [data-fondo='particulas'] .fondo__vista { background-image: radial-gradient(circle, var(--brand) 2px, transparent 2.5px), linear-gradient(125deg, transparent 49%, color-mix(in srgb, var(--brand) 50%, transparent) 50%, transparent 51%); background-size: 26px 22px, 36px 36px; }
    [data-fondo='boreal'] .fondo__vista { background-image: linear-gradient(170deg, transparent 15%, color-mix(in srgb, var(--brand) 55%, transparent) 38%, transparent 62%), linear-gradient(190deg, transparent 30%, color-mix(in srgb, var(--brand-2) 40%, transparent) 52%, transparent 75%); }
    [data-fondo='bokeh'] .fondo__vista { background-image: radial-gradient(circle at 22% 38%, color-mix(in srgb, var(--brand) 60%, transparent) 0 11px, transparent 12px), radial-gradient(circle at 58% 62%, color-mix(in srgb, var(--brand-2) 55%, transparent) 0 15px, transparent 16px), radial-gradient(circle at 82% 28%, color-mix(in srgb, var(--brand) 45%, transparent) 0 9px, transparent 10px); }
    [data-fondo='olas'] .fondo__vista { background-image: radial-gradient(120% 80% at 30% 120%, color-mix(in srgb, var(--brand) 50%, transparent) 55%, transparent 56%), radial-gradient(120% 80% at 80% 135%, color-mix(in srgb, var(--brand-2) 40%, transparent) 55%, transparent 56%); }
    [data-fondo='estrellas'] .fondo__vista { background-image: radial-gradient(circle, var(--texto) 1px, transparent 1.5px), radial-gradient(circle, var(--brand-2) 1.4px, transparent 2px); background-size: 19px 17px, 31px 29px; background-position: 0 0, 9px 11px; }
    [data-fondo='rejilla'] .fondo__vista { background-image: linear-gradient(color-mix(in srgb, var(--brand) 55%, transparent) 1px, transparent 1px), linear-gradient(90deg, color-mix(in srgb, var(--brand) 55%, transparent) 1px, transparent 1px); background-size: 100% 11px, 16px 100%; mask: linear-gradient(transparent, #000 70%); -webkit-mask: linear-gradient(transparent, #000 70%); }
    [data-fondo='ninguno'] .fondo__vista { border: 1px dashed var(--linea); background: transparent; }
    .reglas { display: grid; gap: 10px; }
    .regla { display: grid; gap: 4px; font-size: .84rem; color: var(--soft); }
    .regla span { display: flex; justify-content: space-between; }
    .regla output { color: var(--texto); font-variant-numeric: tabular-nums; }
    .regla input { width: 100%; accent-color: var(--brand); }
    .ambito { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; font-size: .84rem; color: var(--soft); }
    .ambito > span { margin-right: 4px; }
    .chip { min-height: 36px; padding: 0 14px; border-radius: 999px; border: 1px solid var(--vidrio-borde); background: transparent; color: inherit; font: 600 .82rem/1 var(--font-text); cursor: pointer; }
    .chip[aria-pressed='true'] { background: var(--brand); border-color: var(--brand); color: var(--on-brand); }
    .nota { font-size: var(--step-small); color: var(--soft); }
  `,
})
export class AjustesFondo {
  protected readonly tema = inject(TemaService);
  protected readonly fondos = FONDOS;
  protected readonly ambitos = AMBITOS;
}
