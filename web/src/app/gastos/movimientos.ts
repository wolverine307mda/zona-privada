import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { GastosService } from './gastos.service';
import { CATEGORIAS, Movimiento, Tipo, categoria, diaLargo, euros, numero } from './modelo';

// Todos los movimientos del mes, agrupados por día, con buscador y filtros por tipo y categoría
@Component({
  selector: 'app-movimientos',
  template: `
    <section class="caja filtros">
      <label class="buscar">
        <svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M11 18a7 7 0 1 0 0-14 7 7 0 0 0 0 14zM20 20l-4-4" /></svg>
        <span class="sr">Buscar</span>
        <input type="search" placeholder="Buscar concepto o nota…" [value]="q()" (input)="q.set($any($event.target).value)">
      </label>
      <div class="seg" role="group" aria-label="Tipo">
        @for (t of tipos; track t.id) {
          <button type="button" [attr.aria-pressed]="tipo() === t.id" (click)="tipo.set(t.id)">{{ t.nombre }}</button>
        }
      </div>
      <select class="cat-sel" aria-label="Categoría" [value]="cat()" (change)="cat.set($any($event.target).value)">
        <option value="">Todas las categorías</option>
        @for (c of categorias; track c.id) { <option [value]="c.id">{{ c.nombre }}</option> }
      </select>
    </section>

    <section class="caja totales">
      <div><span class="etq">Movimientos</span><strong>{{ filtrados().length }}</strong></div>
      <div><span class="etq">Ingresos</span><strong class="verde">{{ euros(sumas().ingresos) }}</strong></div>
      <div><span class="etq">Gastos</span><strong>{{ euros(sumas().gastos) }}</strong></div>
      <div><span class="etq">Balance</span><strong [class.verde]="sumas().ingresos - sumas().gastos > 0" [class.rojo]="sumas().ingresos - sumas().gastos < 0">{{ euros(sumas().ingresos - sumas().gastos) }}</strong></div>
    </section>

    @for (dia of porDia(); track dia.fecha) {
      <section class="caja dia">
        <header class="dia__cab">
          <h3>{{ diaLargo(dia.fecha) }}</h3>
          <span>{{ dia.total >= 0 ? '+' : '−' }}{{ euros(dia.total < 0 ? -dia.total : dia.total) }}</span>
        </header>
        @for (m of dia.lista; track m.id) {
          <button class="mov" type="button" [style.--c]="categoria(m.categoria).color" (click)="g.editandoMovimiento.set({ movimiento: m })">
            <span class="icono-cat"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path [attr.d]="categoria(m.categoria).icono" /></svg></span>
            <span class="mov__texto">
              <strong>{{ m.concepto }}@if (m.recurrenteId) {<em class="chip-fijo">fijo</em>}</strong>
              <span>{{ categoria(m.categoria).nombre }}@if (m.litros) { · {{ numero(m.litros) }} L a {{ numero(m.precioLitro, 3) }} €/L }@if (m.notas) { · {{ m.notas }} }</span>
            </span>
            <span class="mov__importe" [class.ingreso]="m.tipo === 'Ingreso'">{{ m.tipo === 'Ingreso' ? '+' : '−' }}{{ euros(m.importe) }}</span>
          </button>
        }
      </section>
    } @empty {
      <div class="caja vacio">
        <strong>{{ cargado() ? 'No hay movimientos' : 'Cargando…' }}</strong>
        @if (cargado()) { {{ q() || cat() || tipo() !== 'todos' ? 'Prueba a quitar algún filtro.' : 'Apunta el primero con «Gasto» o «Ingreso».' }} }
      </div>
    }
  `,
  styleUrls: ['./comun.css'],
  styles: `
    .sr { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); }
    .filtros { flex-direction: row; flex-wrap: wrap; align-items: center; gap: 10px; padding: 12px; }
    .buscar { flex: 1 1 220px; display: flex; align-items: center; gap: 8px; padding: 0 12px; min-height: 42px; border-radius: 12px; background: var(--tarjeta-2); color: var(--soft); }
    .buscar input { flex: 1; min-width: 0; border: 0; background: transparent; color: var(--texto); font: inherit; }
    .buscar input:focus { outline: none; }
    .buscar:focus-within { outline: 2px solid var(--brand); }
    .seg { display: inline-grid; grid-auto-flow: column; padding: 4px; border-radius: 12px; background: var(--tarjeta-2); }
    .seg button { min-height: 34px; padding: 0 14px; border: 0; border-radius: 9px; background: transparent; color: var(--soft); font-weight: 500; cursor: pointer; }
    .seg button[aria-pressed='true'] { background: var(--tarjeta); color: var(--texto); box-shadow: 0 1px 4px rgba(0, 0, 0, 0.18); }
    .cat-sel { min-height: 42px; padding: 0 12px; border-radius: 12px; border: 1px solid var(--linea); background: var(--tarjeta-2); color: var(--texto); font: inherit; }
    .totales { flex-direction: row; flex-wrap: wrap; gap: 12px 32px; padding: 16px 20px; }
    .totales div { display: flex; flex-direction: column; gap: 2px; }
    .totales strong { font: 700 var(--step-lead)/1.1 var(--font-display); letter-spacing: -0.03em; }
    .dia { gap: 4px; padding: 12px; }
    .dia__cab { display: flex; justify-content: space-between; align-items: baseline; padding: 4px 10px 8px; border-bottom: 1px solid var(--linea); margin-bottom: 4px; }
    .dia__cab h3 { font-size: var(--step-small); font-weight: 500; color: var(--soft); }
    .dia__cab h3::first-letter { text-transform: uppercase; }
    .dia__cab span { font: 600 12px/1 var(--font-code); color: var(--soft); }
  `,
})
export class Movimientos {
  protected readonly g = inject(GastosService);
  protected readonly lista = signal<Movimiento[]>([]);
  protected readonly cargado = signal(false);
  protected readonly q = signal('');
  protected readonly tipo = signal<'todos' | Tipo>('todos');
  protected readonly cat = signal('');
  protected readonly tipos = [{ id: 'todos' as const, nombre: 'Todos' }, { id: 'Gasto' as const, nombre: 'Gastos' }, { id: 'Ingreso' as const, nombre: 'Ingresos' }];
  protected readonly categorias = CATEGORIAS;
  protected readonly categoria = categoria;
  protected readonly euros = euros;
  protected readonly numero = numero;
  protected readonly diaLargo = diaLargo;

  protected readonly filtrados = computed(() => {
    const q = this.q().trim().toLowerCase();
    return this.lista().filter((m) =>
      (this.tipo() === 'todos' || m.tipo === this.tipo()) &&
      (!this.cat() || m.categoria === this.cat()) &&
      (!q || m.concepto.toLowerCase().includes(q) || (m.notas ?? '').toLowerCase().includes(q)));
  });
  protected readonly sumas = computed(() => this.filtrados().reduce(
    (s, m) => (m.tipo === 'Ingreso' ? { ...s, ingresos: s.ingresos + m.importe } : { ...s, gastos: s.gastos + m.importe }),
    { ingresos: 0, gastos: 0 }));
  protected readonly porDia = computed(() => {
    const grupos = new Map<string, Movimiento[]>();
    for (const m of this.filtrados()) grupos.set(m.fecha, [...(grupos.get(m.fecha) ?? []), m]);
    return [...grupos.entries()].map(([fecha, lista]) => ({
      fecha, lista, total: lista.reduce((t, m) => t + (m.tipo === 'Ingreso' ? m.importe : -m.importe), 0),
    }));
  });

  constructor() {
    effect(() => {
      const anio = this.g.anio(), mes = this.g.mes();
      this.g.version();
      untracked(async () => {
        const desde = `${anio}-${String(mes).padStart(2, '0')}-01`;
        const ultimo = new Date(anio, mes, 0).getDate();
        const hasta = `${anio}-${String(mes).padStart(2, '0')}-${ultimo}`;
        try { this.lista.set(await this.g.movimientos({ desde, hasta })); } finally { this.cargado.set(true); }
      });
    });
  }
}
