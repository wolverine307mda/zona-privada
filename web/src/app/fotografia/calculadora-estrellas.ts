import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';
import { EntradaNpf, calcular, pixelDesdeSensor, texto } from './npf';

const CLAVE = 'mda.npf';
const RECORTES = [{ v: 1, n: 'Pleno formato (1×)' }, { v: 1.5, n: 'APS-C Nikon/Sony (1,5×)' }, { v: 1.6, n: 'APS-C Canon (1,6×)' }, { v: 2, n: 'Micro 4/3 (2×)' }];

// Cuánto exponer a las estrellas sin que dejen rastro: regla 500 y NPF (que tiene en cuenta apertura y tamaño del píxel). Se acuerda tus valores.
@Component({
  selector: 'app-calculadora-estrellas',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrls: ['../gastos/comun.css'],
  template: `
    <section class="caja">
      <header class="caja__cab"><h3>Tiempo de exposición para estrellas</h3><span class="sub">regla 500 y NPF</span></header>
      <div class="rejilla">
        <label>Focal (mm)<input type="number" inputmode="decimal" min="1" [value]="e().focal" (input)="poner('focal', $any($event.target).value)"></label>
        <label>Sensor<select (change)="poner('recorte', $any($event.target).value)">@for (r of recortes; track r.v) { <option [value]="r.v" [selected]="r.v === e().recorte">{{ r.n }}</option> }</select></label>
        <label>Apertura (f/)<input type="number" inputmode="decimal" min="0.7" step="0.1" [value]="e().apertura" (input)="poner('apertura', $any($event.target).value)"></label>
        <label>Píxel (µm)<input type="number" inputmode="decimal" min="1" step="0.1" [value]="e().pixelUm" (input)="poner('pixelUm', $any($event.target).value)"></label>
        <label>Declinación (°)<input type="number" inputmode="decimal" min="0" max="85" [value]="e().declinacion" (input)="poner('declinacion', $any($event.target).value)"></label>
      </div>
      <details class="ayuda-px">
        <summary>¿No sabes el píxel? Calcúlalo</summary>
        <div class="rejilla">
          <label>Ancho del sensor (mm)<input type="number" inputmode="decimal" min="1" [value]="ancho()" (input)="ancho.set(+$any($event.target).value)"></label>
          <label>Megapíxeles<input type="number" inputmode="decimal" min="1" [value]="mp()" (input)="mp.set(+$any($event.target).value)"></label>
        </div>
        @if (pxCalc(); as p) { <button type="button" class="enlace" (click)="poner('pixelUm', '' + p)">Usar {{ p }} µm</button> }
      </details>
      @if (r(); as x) {
        <div class="resultado">
          <div><small>NPF (recomendado)</small><b>{{ t(x.npf) }}</b></div>
          <div><small>Regla 500</small><b>{{ t(x.regla500) }}</b></div>
        </div>
        <p class="nota">Si expones más que el NPF, las estrellas empezarán a verse como rayitas al ampliar. Apunta al sur o al horizonte para ser prudente; cerca del polo norte celeste puedes exponer más.</p>
      } @else { <p class="nota">Rellena focal, apertura y píxel para calcular.</p> }
    </section>
  `,
  styles: `
    .rejilla { display: grid; grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); gap: 10px; }
    .rejilla > label { min-width: 0; }
    .rejilla input, .rejilla select { width: 100%; min-width: 0; }
    label { display: grid; gap: 4px; font-size: .78rem; color: var(--soft); }
    input, select { min-height: 40px; border-radius: 12px; border: 1px solid var(--vidrio-borde); background: color-mix(in srgb, var(--texto) 5%, transparent); color: var(--texto); padding: 0 12px; font: 500 .95rem/1 var(--font-text); }
    .ayuda-px { margin-top: 10px; font-size: .84rem; color: var(--soft); }
    .ayuda-px summary { cursor: pointer; min-height: 32px; }
    .resultado { display: grid; grid-template-columns: 1fr 1fr; gap: 10px; margin-top: 14px; }
    .resultado div { display: grid; gap: 2px; padding: 12px; border-radius: 14px; background: color-mix(in srgb, var(--brand) 12%, transparent); }
    .resultado b { font: var(--peso-ligero) 1.6rem/1.1 var(--font-text); font-variant-numeric: tabular-nums; }
    .resultado small { color: var(--soft); }
    .nota { margin: 10px 0 0; color: var(--soft); font-size: .8rem; }
  `,
})
export class CalculadoraEstrellas {
  protected readonly recortes = RECORTES;
  protected readonly e = signal<EntradaNpf>(this.leer());
  protected readonly ancho = signal(36);
  protected readonly mp = signal(24);
  protected readonly r = computed(() => calcular(this.e()));
  protected readonly pxCalc = computed(() => { const p = pixelDesdeSensor(this.ancho(), this.mp()); return p === null ? null : Math.round(p * 10) / 10; });
  protected readonly t = texto;

  protected poner(campo: keyof EntradaNpf, valor: string): void {
    this.e.update((x) => ({ ...x, [campo]: Number(valor) }));
    try { localStorage.setItem(CLAVE, JSON.stringify(this.e())); } catch { /* sin almacenamiento */ }
  }

  private leer(): EntradaNpf {
    const por: EntradaNpf = { focal: 20, recorte: 1, apertura: 2.8, pixelUm: 4.3, declinacion: 0 };
    try { return { ...por, ...(JSON.parse(localStorage.getItem(CLAVE) ?? '{}') as Partial<EntradaNpf>) }; } catch { return por; }
  }
}
