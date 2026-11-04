import { Component, ElementRef, OnDestroy, afterNextRender, computed, effect, inject, signal, untracked, viewChild } from '@angular/core';
import { TemaService } from '../core/tema.service';
import { GastosService } from './gastos.service';
import { Lugares } from './lugares';
import { echarts, opcionesEvolucion } from './graficas-gastos';
import { Estadisticas as Datos, categoria, euros, eurosCorto, mesLargo } from './modelo';

// Estadísticas de los últimos meses: ingresos frente a gastos, ahorro acumulado, medias,
// el mejor y el peor mes, y las categorías donde más se gasta
@Component({
  selector: 'app-estadisticas',
  imports: [Lugares],
  template: `
    <div class="periodo" role="group" aria-label="Periodo">
      @for (p of periodos; track p) {
        <button type="button" [attr.aria-pressed]="meses() === p" (click)="meses.set(p)">{{ p }} meses</button>
      }
    </div>

    @if (datos(); as d) {
      <section class="kpis">
        <article data-inclina class="caja kpi"><span class="etq">Ahorrado</span><strong class="grande" [class.verde]="d.ahorro > 0" [class.rojo]="d.ahorro < 0">{{ eurosCorto(d.ahorro) }}</strong>
          <span class="sub">{{ d.tasaAhorro != null ? d.tasaAhorro + ' % de lo ingresado' : 'sin ingresos en el periodo' }}</span></article>
        <article data-inclina class="caja kpi"><span class="etq">Ingresos</span><strong class="grande">{{ eurosCorto(d.ingresos) }}</strong><span class="sub">media {{ euros(d.mediaIngresos) }} / mes</span></article>
        <article data-inclina class="caja kpi"><span class="etq">Gastos</span><strong class="grande">{{ eurosCorto(d.gastos) }}</strong><span class="sub">media {{ euros(d.mediaGastos) }} / mes</span></article>
        <article data-inclina class="caja kpi kpi--meses">
          <span class="etq">Mejor mes</span>
          <strong>{{ d.mejorMes ? mesLargo(d.mejorMes.anio, d.mejorMes.mes) : '—' }}</strong>
          <span class="sub verde">{{ d.mejorMes ? '+' + euros(d.mejorMes.ahorro) : '' }}</span>
          <span class="etq">Peor mes</span>
          <strong>{{ d.peorMes ? mesLargo(d.peorMes.anio, d.peorMes.mes) : '—' }}</strong>
          <span class="sub" [class.rojo]="(d.peorMes?.ahorro ?? 0) < 0">{{ d.peorMes ? euros(d.peorMes.ahorro) : '' }}</span>
        </article>
      </section>

      <section class="caja">
        <header class="caja__cab"><h3>Evolución</h3><span class="sub">ingresos, gastos y ahorro acumulado</span></header>
        <div class="grafica" #grafica role="img" aria-label="Ingresos y gastos por mes con el ahorro acumulado"></div>
      </section>

      <section class="caja">
        <header class="caja__cab"><h3>Dónde se va el dinero</h3><span class="sub">en {{ meses() }} meses</span></header>
        <ul class="ranking">
          @for (c of ranking(); track c.categoria; let i = $index) {
            <li [style.--c]="c.cat.color">
              <span class="ranking__pos">{{ i + 1 }}</span>
              <span class="icono-cat"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path [attr.d]="c.cat.icono" /></svg></span>
              <div class="ranking__datos">
                <div class="ranking__fila"><strong>{{ c.cat.nombre }}</strong><span>{{ euros(c.total) }}</span></div>
                <div class="ranking__barra"><span [style.width.%]="c.ancho"></span></div>
                <span class="ranking__sub">{{ c.cantidad }} movimientos · {{ euros(c.total / meses()) }} al mes de media · {{ c.pct }} %</span>
              </div>
            </li>
          } @empty {
            <li class="vacio">Sin gastos en este periodo.</li>
          }
        </ul>
      </section>
      <app-lugares />
    } @else {
      <p class="cargando">{{ fallo() ? 'El equipo no responde: comprueba la conexión.' : 'Cargando…' }}</p>
    }
  `,
  styleUrls: ['./comun.css'],
  styles: `
    .periodo { display: inline-grid; grid-auto-flow: column; align-self: flex-start; padding: 4px; border-radius: 12px; background: var(--vidrio); backdrop-filter: var(--desenfoque); border: 1px solid var(--vidrio-borde); }
    .periodo button { min-height: 36px; padding: 0 16px; border: 0; border-radius: 9px; background: transparent; color: var(--soft); font-weight: 500; cursor: pointer; }
    .periodo button[aria-pressed='true'] { background: var(--brand); color: var(--on-brand); }
    .kpis { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 20px; }
    .kpi { gap: 6px; }
    .kpi .sub { font-size: var(--step-small); color: var(--soft); }
    .kpi--meses { gap: 2px; }
    .kpi--meses strong { font-weight: 600; }
    .kpi--meses .etq:nth-of-type(2) { margin-top: 8px; }
    .grafica { width: 100%; height: 340px; }
    .ranking { display: flex; flex-direction: column; gap: 14px; }
    .ranking li { display: grid; grid-template-columns: 28px 40px minmax(0, 1fr); align-items: center; gap: 12px; }
    .ranking__pos { font: 700 var(--step-body)/1 var(--font-display); color: var(--soft); text-align: center; }
    .ranking__datos { display: flex; flex-direction: column; gap: 6px; min-width: 0; }
    .ranking__fila { display: flex; justify-content: space-between; gap: 12px; min-width: 0; }
    .ranking__fila strong { min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .ranking__fila span { flex: none; }
    .ranking__fila strong { font-weight: 500; }
    .ranking__fila span { font: 600 var(--step-small)/1.4 var(--font-code); }
    .ranking__barra { height: 8px; border-radius: 999px; background: var(--tarjeta-2); overflow: hidden; }
    .ranking__barra span { display: block; height: 100%; border-radius: inherit; background: var(--c); transition: width 900ms var(--ease-expo); }
    .ranking__sub { font-size: 12px; color: var(--soft); }
    @media (max-width: 1100px) { .kpis { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
    @media (max-width: 560px) {
      .kpis { grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 12px; }
      .kpi:first-child, .kpi--meses { grid-column: 1 / -1; }
      .grafica { height: 260px; }
      .periodo { display: grid; grid-auto-columns: 1fr; align-self: stretch; }
      .periodo button { padding: 0 8px; white-space: nowrap; }
      .ranking__fila span { font-size: 12px; }
    }
  `,
})
export class Estadisticas implements OnDestroy {
  protected readonly g = inject(GastosService);
  private readonly tema = inject(TemaService);
  private readonly graficaEl = viewChild<ElementRef<HTMLDivElement>>('grafica');
  private grafica?: echarts.ECharts;
  private observador?: ResizeObserver;

  protected readonly periodos = [6, 12, 24];
  protected readonly meses = signal(12);
  protected readonly datos = signal<Datos | null>(null);
  protected readonly fallo = signal(false);
  protected readonly euros = euros;
  protected readonly eurosCorto = eurosCorto;
  protected readonly mesLargo = mesLargo;

  protected readonly ranking = computed(() => {
    const d = this.datos();
    if (!d) return [];
    const max = Math.max(1, ...d.porCategoria.map((c) => c.total));
    return d.porCategoria.map((c) => ({ ...c, cat: categoria(c.categoria), ancho: (100 * c.total) / max, pct: d.gastos > 0 ? Math.round((100 * c.total) / d.gastos) : 0 }));
  });

  constructor() {
    effect(() => {
      const n = this.meses(), anio = this.g.anio(), mes = this.g.mes();
      this.g.version();
      untracked(async () => {
        try { this.datos.set(await this.g.estadisticas(n, anio, mes)); this.fallo.set(false); } catch { this.fallo.set(true); }
      });
    });
    effect(() => {
      this.tema.cambio();
      this.datos();
      untracked(() => setTimeout(() => this.pintar()));
    });
    afterNextRender(() => this.pintar());
  }

  private pintar(): void {
    const el = this.graficaEl()?.nativeElement, d = this.datos();
    if (!el || !d) return;
    if (!this.grafica) {
      this.grafica = echarts.init(el);
      this.observador = new ResizeObserver(() => this.grafica?.resize());
      this.observador.observe(el);
    }
    this.grafica.setOption(opcionesEvolucion(d.serie), true);
  }

  ngOnDestroy(): void {
    this.observador?.disconnect();
    this.grafica?.dispose();
  }
}
