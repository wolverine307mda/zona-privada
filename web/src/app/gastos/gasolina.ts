import { Component, ElementRef, OnDestroy, afterNextRender, effect, inject, signal, untracked, viewChild } from '@angular/core';
import { TemaService } from '../core/tema.service';
import { GastosService } from './gastos.service';
import { echarts, opcionesGasolina } from './graficas-gastos';
import { Gasolina as DatosGasolina, Movimiento, Repostaje, diaCorto, euros, numero } from './modelo';

// Gasolina: consumo (L/100 km), precio medio y último del litro, coste por km y cada repostaje
@Component({
  selector: 'app-gasolina',
  template: `
    @if (datos(); as d) {
      <section class="kpis">
        <article data-inclina class="caja kpi kpi--consumo">
          <span class="etq">Consumo medio</span>
          <strong class="grande">{{ d.consumoMedio != null ? numero(d.consumoMedio, 1) : '—' }}<small>L/100 km</small></strong>
          <span class="sub">{{ d.consumoMedio != null ? 'con el depósito lleno en cada repostaje' : 'apunta los km en cada repostaje para verlo' }}</span>
        </article>
        <article data-inclina class="caja kpi"><span class="etq">Precio medio</span><strong class="grande">{{ d.precioMedio != null ? numero(d.precioMedio, 3) : '—' }}<small>€/L</small></strong>
          <span class="sub">último: {{ d.ultimoPrecio != null ? numero(d.ultimoPrecio, 3) + ' €/L' : '—' }}</span></article>
        <article data-inclina class="caja kpi"><span class="etq">Este año</span><strong class="grande">{{ euros(d.gastoEsteAnio) }}</strong><span class="sub">{{ numero(d.litros, 0) }} L en total</span></article>
        <article data-inclina class="caja kpi"><span class="etq">Coste por km</span><strong class="grande">{{ d.costePorKm != null ? numero(d.costePorKm * 100, 1) : '—' }}<small>cént.</small></strong>
          <span class="sub">{{ d.kmRecorridos != null ? numero(d.kmRecorridos, 0) + ' km registrados' : 'sin kilómetros todavía' }}</span></article>
      </section>

      <div class="barra-acciones">
        <button class="button" type="button" (click)="nuevo()">
          <svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M12 5v14M5 12h14" /></svg> Nuevo repostaje
        </button>
      </div>

      <section class="caja">
        <header class="caja__cab"><h3>Precio y litros</h3><span class="sub">{{ d.repostajes.length }} repostajes</span></header>
        <div class="grafica" #grafica role="img" aria-label="Precio por litro y litros de cada repostaje"></div>
      </section>

      <section class="caja">
        <header class="caja__cab"><h3>Repostajes</h3></header>
        <div class="tabla" role="table" aria-label="Repostajes">
          <div class="fila fila--cab" role="row">
            <span role="columnheader">Fecha</span><span role="columnheader">Gasolinera</span><span role="columnheader">Litros</span>
            <span role="columnheader">€/L</span><span role="columnheader">Km</span><span role="columnheader">Consumo</span><span role="columnheader">Importe</span>
          </div>
          @for (r of d.repostajes; track r.id) {
            <button class="fila" role="row" type="button" (click)="editar(r)">
              <span role="cell">{{ diaCorto(r.fecha) }}</span>
              <span role="cell" class="fila__texto">{{ r.concepto }}</span>
              <span role="cell">{{ numero(r.litros) }} L</span>
              <span role="cell">{{ numero(r.precioLitro, 3) }}</span>
              <span role="cell">{{ r.kilometros != null ? numero(r.kilometros, 0) : '—' }}</span>
              <span role="cell" [class.bien]="r.consumo != null && d.consumoMedio != null && r.consumo <= d.consumoMedio">{{ r.consumo != null ? numero(r.consumo, 1) + ' L' : '—' }}</span>
              <span role="cell" class="fila__importe">{{ euros(r.importe) }}</span>
            </button>
          } @empty {
            <div class="vacio"><strong>Sin repostajes</strong>Apunta el primero con los litros, el precio y los km del coche.</div>
          }
        </div>
      </section>
    } @else {
      <p class="cargando">{{ fallo() ? 'El equipo no responde: comprueba la conexión.' : 'Cargando…' }}</p>
    }
  `,
  styleUrls: ['./comun.css'],
  styles: `
    .kpis { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 20px; }
    .kpi { gap: 6px; }
    .kpi .grande small { font-size: 0.42em; color: var(--soft); margin-left: 6px; letter-spacing: 0; }
    .kpi .sub { font-size: var(--step-small); color: var(--soft); }
    .kpi--consumo { background: linear-gradient(135deg, color-mix(in srgb, #ff7a1f 18%, var(--tarjeta)), var(--tarjeta) 65%); }
    .barra-acciones .button { min-height: 44px; padding: 0 18px; }
    .barra-acciones .icon { width: 18px; height: 18px; }
    .grafica { width: 100%; height: 300px; }
    .tabla { display: flex; flex-direction: column; gap: 2px; overflow-x: auto; }
    .fila { display: grid; grid-template-columns: 80px minmax(120px, 1.4fr) repeat(4, minmax(70px, 0.8fr)) minmax(90px, 0.9fr); gap: 10px; align-items: center; min-width: 640px; padding: 10px 12px; border: 0; border-radius: 10px; background: transparent; color: var(--texto); font: inherit; text-align: left; cursor: pointer; }
    .fila span { font: 500 var(--step-small)/1.2 var(--font-code); }
    .fila__texto { font-family: var(--font-text) !important; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .fila__importe { text-align: right; font-weight: 600 !important; }
    .fila--cab { cursor: default; border-bottom: 1px solid var(--linea); border-radius: 0; }
    .fila--cab span { font: 500 11px/1 var(--font-text); text-transform: uppercase; letter-spacing: 0.06em; color: var(--soft); }
    .fila--cab span:last-child { text-align: right; }
    @media (hover: hover) { .fila:not(.fila--cab):hover { background: var(--tarjeta-2); } }
    .bien { color: var(--ok); }
    [data-mode='light'] .bien { color: #138a4c; }
    @media (max-width: 1100px) { .kpis { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
    @media (max-width: 560px) {
      .kpis { grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 12px; }
      .grafica { height: 240px; }
      .fila--cab { display: none; }
      .fila { min-width: 0; grid-template-columns: minmax(0, 1fr) auto; gap: 4px 10px; padding: 12px; background: var(--tarjeta-2); margin-bottom: 6px; }
      .fila span:nth-child(n + 3):not(.fila__importe) { display: none; }
      .fila span:first-child { grid-column: 1 / -1; color: var(--soft); }
    }
  `,
})
export class Gasolina implements OnDestroy {
  protected readonly g = inject(GastosService);
  private readonly tema = inject(TemaService);
  private readonly graficaEl = viewChild<ElementRef<HTMLDivElement>>('grafica');
  private grafica?: echarts.ECharts;
  private observador?: ResizeObserver;
  private crudos: Movimiento[] = [];

  protected readonly datos = signal<DatosGasolina | null>(null);
  protected readonly euros = euros;
  protected readonly numero = numero;
  protected readonly diaCorto = diaCorto;
  protected readonly fallo = signal(false);

  constructor() {
    effect(() => {
      this.g.version();
      untracked(async () => {
        try {
          const [d, crudos] = await Promise.all([this.g.gasolina(), this.g.movimientos({ categoria: 'gasolina' })]);
          this.crudos = crudos;
          this.datos.set(d);
          this.fallo.set(false);
        } catch { this.fallo.set(true); }
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
    this.grafica.setOption(opcionesGasolina(d.repostajes), true);
  }

  protected nuevo(): void { this.g.editandoMovimiento.set({ tipo: 'Gasto', categoria: 'gasolina' }); }
  protected editar(r: Repostaje): void {
    const m = this.crudos.find((x) => x.id === r.id);
    if (m) this.g.editandoMovimiento.set({ movimiento: m });
  }

  ngOnDestroy(): void {
    this.observador?.disconnect();
    this.grafica?.dispose();
  }
}
