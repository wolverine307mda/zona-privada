import { Component, computed, input } from '@angular/core';

export interface DiscoUso { nombre: string; usado: number; total: number; }

const GB = 1_073_741_824;
const tam = (b: number) => (b >= 1024 * GB ? `${(b / (1024 * GB)).toFixed(1)} TB` : `${(b / GB).toFixed(b < 100 * GB ? 1 : 0)} GB`);

// El espacio de cada disco: una barra fina que se llena (verde, ámbar al 85 %, roja al 95 %) y debajo lo que queda libre.
@Component({
  selector: 'app-espacio',
  template: `
    @for (d of lista(); track d.nombre) {
      <div class="disco" [class.disco--lleno]="d.pct >= 85" [class.disco--critico]="d.pct >= 95">
        <div class="disco__cab"><strong>{{ d.nombre }}</strong><span>{{ d.libre }} libres</span></div>
        <div class="barra" role="progressbar" [attr.aria-label]="'Disco ' + d.nombre + ' lleno al ' + d.pct + ' por ciento'" [attr.aria-valuenow]="d.pct" aria-valuemin="0" aria-valuemax="100"><i [style.width.%]="d.pct"></i></div>
        <small>{{ d.usado }} de {{ d.total }} · {{ d.pct }} %</small>
      </div>
    } @empty { <p class="nada">Sin datos de discos todavía.</p> }
  `,
  styles: `
    :host { display: grid; gap: 14px; width: 100%; }
    .disco { display: grid; gap: 6px; --c: #3fb96a; }
    .disco--lleno { --c: #f0b53a; } .disco--critico { --c: var(--bad); }
    .disco__cab { display: flex; justify-content: space-between; gap: 10px; align-items: baseline; }
    .disco__cab strong { font: 700 .95rem/1 var(--font-display); }
    .disco__cab span { font-size: .8rem; color: var(--soft); font-variant-numeric: tabular-nums; }
    .barra { height: 8px; border-radius: 99px; background: color-mix(in srgb, var(--texto) 10%, transparent); overflow: hidden; }
    .barra i { display: block; height: 100%; border-radius: inherit; background: linear-gradient(90deg, color-mix(in srgb, var(--c) 70%, transparent), var(--c)); box-shadow: 0 0 10px color-mix(in srgb, var(--c) 50%, transparent); transition: width 900ms var(--ease-expo, ease); }
    small { font-size: .74rem; color: var(--soft); font-variant-numeric: tabular-nums; }
    .nada { margin: 0; color: var(--soft); font-size: .85rem; }
    @media (prefers-reduced-motion: reduce) { .barra i { transition: none; } }
  `,
})
export class Espacio {
  readonly discos = input.required<DiscoUso[]>();
  protected readonly lista = computed(() => this.discos().filter((d) => d.total > 0).map((d) => ({
    nombre: d.nombre.replace(/\\$/, ''), pct: Math.round((100 * d.usado) / d.total), libre: tam(d.total - d.usado), usado: tam(d.usado), total: tam(d.total),
  })));
}
