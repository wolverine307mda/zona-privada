import { Component, computed, input, signal } from '@angular/core';

type Diseno = 'celda' | 'anillo' | 'barras';
const DISENOS: { id: Diseno; nombre: string }[] = [{ id: 'celda', nombre: 'Celda' }, { id: 'anillo', nombre: 'Anillo' }, { id: 'barras', nombre: 'Barras' }];
const leer = (): Diseno => { try { const d = localStorage.getItem('mda-bateria') as Diseno; return DISENOS.some((x) => x.id === d) ? d : 'celda'; } catch { return 'celda'; } };

// La batería del portátil con tres dibujos a elegir. Cuando está cargando se ve la corriente entrando; con poca carga se pone en ámbar o rojo.
// El porcentaje va pequeño y discreto: el dibujo es el que cuenta el nivel.
@Component({
  selector: 'app-bateria',
  template: `
    <figure class="bateria" [class.carga]="cargando()" [style.--n]="color()" role="img" [attr.aria-label]="'Batería al ' + nivel() + ' por ciento' + (cargando() ? ', cargando' : '')">
      @switch (diseno()) {
        @case ('celda') {
          <svg viewBox="0 0 260 110" class="celda" aria-hidden="true">
            <path class="cable" d="M2 55H40"/>
            @if (cargando()) { <path class="corriente" d="M2 55H40"/> <path class="rayo" d="M20 40l-8 17h8l-4 15 12-19h-8l4-13z"/> }
            <rect class="cuerpo" x="42" y="14" width="170" height="82" rx="16"/>
            <rect class="polo" x="214" y="40" width="11" height="30" rx="3"/>
            @for (i of segmentos; track i) {
              <rect class="seg" [class.seg--on]="i < llenos()" [class.seg--sigue]="cargando() && i === llenos()" [attr.x]="53 + i * 16.1" y="26" width="12.6" height="58" rx="4"/>
            }
            <text class="pct" x="127" y="62" text-anchor="middle" dominant-baseline="middle">{{ nivel() }}%</text>
          </svg>
        }
        @case ('anillo') {
          <svg viewBox="0 0 120 120" class="anillo" aria-hidden="true">
            @for (t of marcas; track t) { <line class="marca" [class.marca--on]="t < nivel() * 0.6" x1="60" y1="6" x2="60" y2="12" [attr.transform]="'rotate(' + t * 6 + ' 60 60)'"/> }
            <circle class="pista" cx="60" cy="60" r="42"/>
            <circle class="valor" cx="60" cy="60" r="42" [attr.stroke-dasharray]="circunferencia * nivel() / 100 + ' ' + circunferencia"/>
            @if (cargando()) { <circle class="onda" cx="60" cy="60" r="42"/> }
            <text class="pct" x="60" y="62" text-anchor="middle" dominant-baseline="middle">{{ nivel() }}</text>
            <text class="pct-u" x="60" y="80" text-anchor="middle">{{ cargando() ? '⚡ cargando' : '%' }}</text>
          </svg>
        }
        @default {
          <div class="barras" aria-hidden="true">
            @for (i of columnas; track i) { <i [class.on]="i < nivel() / 100 * columnas.length" [style.--h.%]="30 + i * 4" [style.--d.ms]="i * 70"></i> }
            <span class="pct">{{ nivel() }}%{{ cargando() ? ' ⚡' : '' }}</span>
          </div>
        }
      }
      <figcaption>{{ texto() }}</figcaption>
    </figure>
    <div class="elegir" role="group" aria-label="Dibujo de la batería">
      @for (d of disenos; track d.id) { <button type="button" [attr.aria-pressed]="diseno() === d.id" (click)="elegir(d.id)">{{ d.nombre }}</button> }
    </div>
  `,
  styles: `
    :host { display: grid; gap: 8px; justify-items: start; }
    .bateria { --n: var(--brand); margin: 0; display: grid; gap: 6px; justify-items: start; color: var(--n); }
    figcaption { font-size: .78rem; color: var(--soft); }
    svg { display: block; overflow: visible; }
    .celda { width: min(260px, 100%); height: auto; }
    .cuerpo { fill: none; stroke: color-mix(in srgb, var(--texto) 45%, transparent); stroke-width: 3; }
    .polo { fill: color-mix(in srgb, var(--texto) 45%, transparent); }
    .seg { fill: color-mix(in srgb, var(--texto) 9%, transparent); transition: fill 400ms ease; }
    .seg--on { fill: var(--n); filter: drop-shadow(0 0 4px color-mix(in srgb, var(--n) 60%, transparent)); }
    .seg--sigue { animation: parpadea 1.1s ease-in-out infinite; fill: var(--n); }
    .pct { fill: var(--texto); font: 600 18px/1 var(--font-code); opacity: .8; paint-order: stroke; stroke: color-mix(in srgb, #000 55%, transparent); stroke-width: 4px; }
    .cable { fill: none; stroke: color-mix(in srgb, var(--texto) 30%, transparent); stroke-width: 4; stroke-linecap: round; }
    .corriente { fill: none; stroke: var(--n); stroke-width: 4; stroke-linecap: round; stroke-dasharray: 5 11; filter: drop-shadow(0 0 4px var(--n)); animation: fluye .7s linear infinite; }
    .rayo { fill: var(--n); filter: drop-shadow(0 0 5px var(--n)); animation: late 1.2s ease-in-out infinite; transform-origin: 20px 55px; }
    .anillo { width: 132px; height: 132px; }
    .pista { fill: none; stroke: color-mix(in srgb, var(--texto) 10%, transparent); stroke-width: 8; }
    .valor { fill: none; stroke: var(--n); stroke-width: 8; stroke-linecap: round; transform: rotate(-90deg); transform-origin: 60px 60px; filter: drop-shadow(0 0 5px color-mix(in srgb, var(--n) 60%, transparent)); transition: stroke-dasharray 700ms var(--ease-expo, ease); }
    .onda { fill: none; stroke: var(--n); stroke-width: 2; transform-origin: 60px 60px; animation: onda 1.8s ease-out infinite; }
    .marca { stroke: color-mix(in srgb, var(--texto) 18%, transparent); stroke-width: 1.5; stroke-linecap: round; }
    .marca--on { stroke: var(--n); }
    .anillo .pct { font: 700 28px/1 var(--font-display); stroke: none; }
    .pct-u { fill: var(--soft); font: 500 8px/1 var(--font-code); letter-spacing: .08em; }
    .barras { position: relative; display: flex; align-items: flex-end; gap: 5px; height: 84px; padding-top: 4px; }
    .barras i { width: 11px; height: calc(var(--h) * 1%); border-radius: 4px; background: color-mix(in srgb, var(--texto) 10%, transparent); transition: background 400ms; }
    .barras i.on { background: var(--n); box-shadow: 0 0 8px color-mix(in srgb, var(--n) 50%, transparent); }
    .carga .barras i.on { animation: barra 1.4s ease-in-out infinite; animation-delay: calc(var(--d) * 1ms); }
    .barras .pct { position: absolute; right: 0; top: -6px; font: 600 .78rem/1 var(--font-code); stroke: none; }
    .elegir { display: inline-flex; gap: 4px; padding: 3px; border-radius: 999px; border: 1px solid var(--vidrio-borde); }
    .elegir button { min-height: 30px; padding: 0 12px; border: 0; border-radius: 999px; background: none; color: var(--soft); font: 600 .72rem/1 var(--font-text); cursor: pointer; }
    .elegir button[aria-pressed='true'] { background: var(--brand); color: var(--on-brand); }
    @keyframes fluye { to { stroke-dashoffset: -16; } }
    @keyframes parpadea { 50% { opacity: .25; } }
    @keyframes late { 50% { transform: scale(1.18); } }
    @keyframes onda { from { opacity: .8; transform: scale(1); } to { opacity: 0; transform: scale(1.35); } }
    @keyframes barra { 50% { opacity: .55; transform: scaleY(.92); } }
    @media (prefers-reduced-motion: reduce) { .corriente, .rayo, .seg--sigue, .onda, .carga .barras i.on { animation: none; } .valor, .seg { transition: none; } }
  `,
})
export class Bateria {
  readonly nivel = input.required<number>();
  readonly cargando = input(false);
  protected readonly disenos = DISENOS;
  protected readonly diseno = signal<Diseno>(leer());
  protected readonly segmentos = Array.from({ length: 10 }, (_, i) => i);
  protected readonly columnas = Array.from({ length: 14 }, (_, i) => i);
  protected readonly marcas = Array.from({ length: 60 }, (_, i) => i);
  protected readonly circunferencia = 2 * Math.PI * 42;
  protected readonly llenos = computed(() => Math.round(this.nivel() / 10));
  // poca carga: rojo (≤15) y ámbar (≤35); cargando, el color de acento; si no, verde
  protected readonly color = computed(() => (this.nivel() <= 15 && !this.cargando() ? 'var(--bad)' : this.nivel() <= 35 && !this.cargando() ? '#f0b53a' : this.cargando() ? 'var(--brand)' : '#3fb96a'));
  protected readonly texto = computed(() => (this.cargando() ? (this.nivel() >= 100 ? 'Cargada del todo' : 'Cargando…') : this.nivel() <= 15 ? 'Poca batería: enchúfalo' : 'Con batería'));

  protected elegir(d: Diseno): void { this.diseno.set(d); try { localStorage.setItem('mda-bateria', d); } catch { /* sin almacenamiento */ } }
}
