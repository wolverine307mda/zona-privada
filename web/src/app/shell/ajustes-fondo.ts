import { Component, DestroyRef, ElementRef, afterNextRender, effect, inject, untracked, viewChildren } from '@angular/core';
import { resolverColor } from '../core/color-css';
import { AMBITOS, FONDOS, GRUPOS_FONDO, TemaService, type FondoId } from '../core/tema.service';
import { REACCIONES, SenalesFondo } from './senales-fondo';

// «Fondo» dentro de Ajustes: elegir el fondo animado de toda la app, cuánto se ve, cuánto se mueve, la fuerza de su color y dónde.
// Cada miniatura es el fondo de verdad, dibujado una vez en pequeño con tu color y tu fuerza.
@Component({
  selector: 'app-ajustes-fondo',
  template: `
    <section class="seccion" aria-labelledby="ajustes-fondo">
      <h3 id="ajustes-fondo">Fondo</h3>
      @for (g of grupos; track g.id) {
        <div class="grupo">
          <h4>{{ g.nombre }} <small>{{ g.fondos.length }}</small></h4>
          @if (g.nota) { <p class="nota">{{ g.nota }}</p> }
          <div class="fondos" role="group" [attr.aria-label]="g.nombre">
            @for (f of g.fondos; track f.id) {
              <button type="button" class="fondo" [attr.data-fondo]="f.id" [attr.aria-pressed]="tema.fondo() === f.id" (click)="tema.ponerFondo(f.id)">
                <span class="fondo__vista" aria-hidden="true">@if (animado(f.id)) { <canvas #vista [attr.data-id]="f.id"></canvas> }</span>
                <span class="fondo__nombre">{{ f.nombre }}</span>
                <span class="fondo__nota">{{ f.nota }}</span>
              </button>
            }
          </div>
        </div>
      }
      @if (animado(tema.fondo())) {
        <div class="reglas">
          <label class="regla"><span>Cuánto se ve <output>{{ tema.intensidad() }} %</output></span>
            <input type="range" min="5" max="100" step="5" [value]="tema.intensidad()" (input)="tema.ponerIntensidad(+$any($event.target).value)"></label>
          <label class="regla"><span>Fuerza del color <output>{{ tema.fuerza() }} %</output></span>
            <input type="range" min="50" max="200" step="5" [value]="tema.fuerza()" (input)="tema.ponerFuerza(+$any($event.target).value)"></label>
          <label class="regla"><span>Velocidad <output>{{ tema.velocidad() }} %</output></span>
            <input type="range" min="25" max="200" step="5" [value]="tema.velocidad()" (input)="tema.ponerVelocidad(+$any($event.target).value)"></label>
        </div>
      }
      @if (animado(tema.fondo())) {
        <div class="ambito" role="group" aria-label="Reaccionar a">
          <span>Reaccionar a</span>
          @for (r of reacciones; track r.id) {
            <button type="button" class="chip" [attr.aria-pressed]="senales.modo() === r.id" [title]="r.nota" (click)="senales.ponerModo(r.id)">{{ r.nombre }}</button>
          }
        </div>
        @if (senales.modo() !== 'nada') {
          <div class="reglas">
            <label class="regla"><span>Cuánto reacciona <output>{{ senales.cuanto() }} %</output></span>
              <input type="range" min="0" max="100" step="5" [value]="senales.cuanto()" (input)="senales.ponerCuanto(+$any($event.target).value)"></label>
          </div>
          <p class="nota">Es un latido suave y aproximado: sabe que suena algo, no sigue el ritmo real. En el móvil solo reacciona la música, y más despacio, para gastar menos batería.</p>
        }
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
    .seccion { display: flex; flex-direction: column; gap: 14px; }
    h3 { font-size: 12px; font-weight: 500; text-transform: uppercase; letter-spacing: .08em; color: var(--soft); }
    h4 { margin: 0 0 6px; font-size: .9rem; font-weight: 600; }
    h4 small { margin-left: 6px; font-weight: 500; color: var(--soft); }
    .grupo { display: grid; gap: 6px; }
    .fondos { display: grid; grid-template-columns: repeat(auto-fill, minmax(112px, 1fr)); gap: 10px; }
    .fondo { display: flex; flex-direction: column; align-items: stretch; gap: 4px; padding: 8px; text-align: left; border-radius: 14px; border: 2px solid var(--linea); background: transparent; color: inherit; font: inherit; cursor: pointer; transition: border-color 200ms ease, transform 200ms var(--ease-out); }
    .fondo[aria-pressed='true'] { border-color: var(--brand); }
    @media (hover: hover) { .fondo:hover { transform: translateY(-2px); } }
    .fondo:focus-visible { outline: 2px solid var(--acento); outline-offset: 2px; }
    .fondo__vista { position: relative; display: block; height: 58px; border-radius: 8px; overflow: hidden; background: var(--tarjeta-2); }
    .fondo__vista canvas { display: block; width: 100%; height: 100%; }
    .fondo__nombre { font-size: .86rem; font-weight: 600; }
    .fondo__nota { font-size: .72rem; line-height: 1.25; color: var(--soft); }
    /* «Aura» y «Nada» no tienen dibujo: su miniatura es solo CSS */
    [data-fondo='aura'] .fondo__vista { background-image: radial-gradient(60% 80% at 20% 10%, color-mix(in srgb, var(--brand) 55%, transparent), transparent 70%), radial-gradient(60% 70% at 90% 100%, color-mix(in srgb, var(--brand) 28%, #3a5bff), transparent 70%); }
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
    .nota { margin: 0; font-size: var(--step-small); color: var(--soft); }
  `,
})
export class AjustesFondo {
  protected readonly tema = inject(TemaService);
  protected readonly ambitos = AMBITOS;
  protected readonly senales = inject(SenalesFondo);
  protected readonly reacciones = REACCIONES;
  protected readonly grupos = GRUPOS_FONDO.map((g) => ({ ...g, fondos: FONDOS.filter((f) => f.grupo === g.id) }));
  private readonly vistas = viewChildren<ElementRef<HTMLCanvasElement>>('vista');
  private generacion = 0;
  private espera?: ReturnType<typeof setTimeout>;
  private listo = false;

  constructor() {
    afterNextRender(() => { this.listo = true; this.pintar(); });
    // otro color, modo claro u oscuro o otra fuerza: se vuelven a dibujar las miniaturas (con un respiro mientras se arrastra el deslizador)
    effect(() => { this.tema.cambio(); this.tema.fuerza(); untracked(() => { if (this.listo) { clearTimeout(this.espera); this.espera = setTimeout(() => this.pintar(), 180); } }); });
    inject(DestroyRef).onDestroy(() => { this.generacion++; clearTimeout(this.espera); });
  }

  protected animado(id: FondoId): boolean { return id !== 'aura' && id !== 'ninguno'; }

  private async pintar(): Promise<void> {
    const mi = ++this.generacion;
    try {
      const { crearEscena, paletaDe } = await import('./fondos');
      const css = getComputedStyle(document.documentElement);
      const paleta = paletaDe(resolverColor(css.getPropertyValue('--brand'), '#ff7a1f'), document.documentElement.dataset['mode'] !== 'light');
      paleta.fuerza = this.tema.fuerza() / 100;
      for (const ref of this.vistas()) {
        if (mi !== this.generacion) return;
        const canvas = ref.nativeElement, id = canvas.dataset['id'] as FondoId, caja = canvas.parentElement?.getBoundingClientRect();
        const escena = await crearEscena(id, canvas, paleta, { intensidad: 0.9, velocidad: 1, ligero: true });
        if (!escena || mi !== this.generacion) continue;
        canvas.style.imageRendering = escena.pixelado ? 'pixelated' : 'auto';
        escena.cambiar(caja?.width || 112, caja?.height || 58);
        for (let i = 0; i < 9; i++) escena.dibujar(4 + i * 0.4, 0.05); // varios fotogramas: los que dejan estela o se calculan en capas necesitan «calentar»
        await new Promise((r) => setTimeout(r, 12)); // una miniatura tras otra, sin bloquear la pantalla
      }
    } catch { /* sin canvas: las miniaturas se quedan vacías y el resto de Ajustes sigue igual */ }
  }
}
