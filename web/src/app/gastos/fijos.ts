import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { GastosService } from './gastos.service';
import { FRECUENCIAS, Recurrente, categoria, euros, eurosCorto } from './modelo';

interface Grupo { id: string; titulo: string; lista: Recurrente[]; mensual: number; }

// Fijos y suscripciones: lo que se repite, agrupado (suscripciones, casa, coche, otros e ingresos),
// con lo que suponen al mes y al año. Pulsar uno lo edita; el interruptor lo pausa.
@Component({
  selector: 'app-fijos',
  template: `
    <section class="kpis">
      <article data-inclina class="caja kpi"><span class="etq">Gastos fijos al mes</span><strong class="grande">{{ euros(totales().gastos) }}</strong><span class="sub">{{ eurosCorto(totales().gastos * 12) }} al año</span></article>
      <article data-inclina class="caja kpi kpi--subs">
        <span class="etq">Suscripciones</span><strong class="grande">{{ euros(totales().subs) }}<small>/mes</small></strong>
        <span class="sub">{{ totales().nSubs }} activas · {{ eurosCorto(totales().subs * 12) }} al año</span>
      </article>
      <article data-inclina class="caja kpi"><span class="etq">Ingresos fijos al mes</span><strong class="grande verde">{{ euros(totales().ingresos) }}</strong><span class="sub">te quedan {{ euros(totales().ingresos - totales().gastos) }} libres</span></article>
    </section>

    <div class="barra-acciones">
      <button class="button" type="button" (click)="g.editandoFijo.set({ tipo: 'Gasto', categoria: 'suscripciones' })">
        <svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M12 5v14M5 12h14" /></svg> Nueva suscripción
      </button>
      <button class="button button--ghost" type="button" (click)="g.editandoFijo.set({ tipo: 'Gasto', categoria: 'vivienda' })">Otro fijo</button>
      <button class="button button--ghost" type="button" (click)="g.editandoFijo.set({ tipo: 'Ingreso', categoria: 'nomina' })">Ingreso fijo</button>
    </div>

    @for (gr of grupos(); track gr.id) {
      <section class="grupo">
        <header class="grupo__cab"><h3>{{ gr.titulo }}</h3><span>{{ euros(gr.mensual) }} / mes</span></header>
        <div class="tarjetas">
          @for (r of gr.lista; track r.id) {
            <article class="fijo" data-inclina [class.fijo--pausado]="!r.activo" [style.--c]="categoria(r.categoria).color">
              <button class="fijo__cuerpo" type="button" (click)="g.editandoFijo.set({ fijo: r })" [attr.aria-label]="'Editar ' + r.nombre">
                <span class="fijo__logo">{{ r.nombre.slice(0, 1).toUpperCase() }}</span>
                <span class="fijo__nombre">{{ r.nombre }}</span>
                <span class="fijo__importe" [class.verde]="r.tipo === 'Ingreso'">{{ euros(r.importe) }}@if (r.variable) {<small> aprox.</small>}</span>
                <span class="fijo__cuando">{{ cada(r) }} · día {{ r.dia }}</span>
                @if (r.frecuencia !== 'Mensual') { <span class="fijo__mes">{{ euros(r.mensual) }} al mes</span> }
              </button>
              <label class="fijo__activo" [title]="r.activo ? 'Pausar' : 'Reactivar'">
                <input type="checkbox" [checked]="r.activo" (change)="alternar(r)">
                <span aria-hidden="true"></span>
                <span class="sr">{{ r.activo ? 'Activo' : 'Pausado' }}</span>
              </label>
            </article>
          }
        </div>
      </section>
    } @empty {
      <div class="caja vacio">
        <strong>{{ cargado() ? 'Aún no hay fijos' : 'Cargando…' }}</strong>
        @if (cargado()) { Añade tus suscripciones, el alquiler o la nómina: cada mes te recordará apuntarlos. }
      </div>
    }
  `,
  styleUrls: ['./comun.css'],
  styles: `
    .sr { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); }
    .kpis { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 20px; }
    .kpi { gap: 6px; }
    .kpi .grande small { font-size: 0.45em; color: var(--soft); margin-left: 4px; }
    .kpi .sub { font-size: var(--step-small); color: var(--soft); }
    .kpi--subs { background: linear-gradient(135deg, color-mix(in srgb, #e62429 16%, var(--tarjeta)), var(--tarjeta) 60%); }
    .barra-acciones { display: flex; flex-wrap: wrap; gap: 10px; }
    .barra-acciones .button { min-height: 44px; padding: 0 18px; }
    .barra-acciones .icon { width: 18px; height: 18px; }
    .grupo { display: flex; flex-direction: column; gap: 12px; }
    .grupo__cab { display: flex; justify-content: space-between; align-items: baseline; }
    .grupo__cab h3 { font: 700 var(--step-body)/1.1 var(--font-display); text-transform: uppercase; letter-spacing: -0.02em; }
    .grupo__cab span { font: 600 var(--step-small)/1 var(--font-code); color: var(--soft); }
    .tarjetas { display: grid; grid-template-columns: repeat(auto-fill, minmax(210px, 1fr)); gap: 14px; }
    .fijo { position: relative; border-radius: var(--radio); border: 1px solid var(--vidrio-borde); background: var(--brillo), var(--vidrio); backdrop-filter: var(--desenfoque); box-shadow: var(--sombra); overflow: hidden; transition: transform 500ms var(--ease-out), border-color 240ms ease; }
    .fijo::before { content: ''; position: absolute; inset: 0 0 auto; height: 4px; background: var(--c); }
    @media (hover: hover) { .fijo:hover { border-color: color-mix(in srgb, var(--c) 50%, var(--linea)); } }
    .fijo--pausado { opacity: 0.55; }
    .fijo__cuerpo { display: grid; grid-template-columns: 44px minmax(0, 1fr); grid-template-rows: auto auto auto; gap: 2px 12px; width: 100%; padding: 18px 16px 16px; border: 0; background: transparent; color: var(--texto); text-align: left; cursor: pointer; font: inherit; }
    .fijo__logo { grid-row: 1 / span 2; display: grid; place-items: center; width: 44px; height: 44px; border-radius: 12px; background: var(--c); color: #fff; font: 700 20px/1 var(--font-display); text-shadow: 0 1px 2px rgba(0, 0, 0, 0.3); }
    .fijo__nombre { font-weight: 500; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; padding-right: 44px; }
    .fijo__importe { font: 700 var(--step-lead)/1.1 var(--font-display); letter-spacing: -0.03em; }
    .fijo__importe small { font: 400 11px/1 var(--font-text); color: var(--soft); letter-spacing: 0; }
    .fijo__cuando, .fijo__mes { grid-column: 1 / -1; font-size: 12px; color: var(--soft); margin-top: 6px; }
    .fijo__mes { margin-top: 0; }
    .fijo__activo { position: absolute; top: 16px; right: 14px; cursor: pointer; }
    .fijo__activo input { position: absolute; opacity: 0; width: 1px; height: 1px; }
    .fijo__activo span:not(.sr) { display: block; width: 36px; height: 22px; border-radius: 999px; background: var(--tarjeta-2); border: 1px solid var(--linea); position: relative; transition: background-color 200ms ease; }
    .fijo__activo span:not(.sr)::after { content: ''; position: absolute; top: 2px; left: 2px; width: 16px; height: 16px; border-radius: 50%; background: var(--soft); transition: translate 300ms var(--ease-expo); }
    .fijo__activo input:checked + span { background: var(--ok); border-color: var(--ok); }
    .fijo__activo input:checked + span::after { translate: 14px 0; background: #fff; }
    .fijo__activo input:focus-visible + span { outline: 3px solid var(--acento); outline-offset: 2px; }
    @media (max-width: 900px) { .kpis { grid-template-columns: minmax(0, 1fr); } }
  `,
})
export class Fijos {
  protected readonly g = inject(GastosService);
  protected readonly lista = signal<Recurrente[]>([]);
  protected readonly cargado = signal(false);
  protected readonly categoria = categoria;
  protected readonly euros = euros;
  protected readonly eurosCorto = eurosCorto;

  protected readonly totales = computed(() => {
    const activos = this.lista().filter((r) => r.activo);
    const subs = activos.filter((r) => r.categoria === 'suscripciones');
    const suma = (l: Recurrente[]) => l.reduce((t, r) => t + r.mensual, 0);
    return {
      gastos: suma(activos.filter((r) => r.tipo === 'Gasto')),
      ingresos: suma(activos.filter((r) => r.tipo === 'Ingreso')),
      subs: suma(subs), nSubs: subs.length,
    };
  });

  protected readonly grupos = computed<Grupo[]>(() => {
    const def = [
      { id: 'subs', titulo: 'Suscripciones', f: (r: Recurrente) => r.categoria === 'suscripciones' },
      { id: 'casa', titulo: 'Casa', f: (r: Recurrente) => categoria(r.categoria).grupo === 'Casa' },
      { id: 'coche', titulo: 'Coche', f: (r: Recurrente) => categoria(r.categoria).grupo === 'Coche' },
      { id: 'otros', titulo: 'Otros gastos', f: (r: Recurrente) => r.tipo === 'Gasto' && !['Suscripciones', 'Casa', 'Coche'].includes(categoria(r.categoria).grupo) },
      { id: 'ingresos', titulo: 'Ingresos', f: (r: Recurrente) => r.tipo === 'Ingreso' },
    ];
    return def.map((d) => {
      const lista = this.lista().filter(d.f);
      return { id: d.id, titulo: d.titulo, lista, mensual: lista.filter((r) => r.activo).reduce((t, r) => t + r.mensual, 0) };
    }).filter((gr) => gr.lista.length);
  });

  constructor() {
    effect(() => {
      this.g.version();
      untracked(async () => {
        try { this.lista.set(await this.g.recurrentes()); } finally { this.cargado.set(true); }
      });
    });
  }

  protected cada(r: Recurrente): string { return FRECUENCIAS.find((f) => f.id === r.frecuencia)?.cada ?? ''; }

  protected async alternar(r: Recurrente): Promise<void> {
    const { id, mensual, ...resto } = r;
    await this.g.guardarFijo({ ...resto, activo: !r.activo }, id);
    this.g.recargar();
  }
}
