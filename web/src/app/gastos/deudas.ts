import { Component, ElementRef, computed, effect, inject, signal, untracked, viewChild } from '@angular/core';
import { Errores, GastosService, erroresDe } from './gastos.service';
import { MantenimientoCoche } from './mantenimiento';
import { seguirTeclado } from './teclado';
import { CATEGORIAS, Deuda, Deudas as Datos, MESES, categoria, diaCorto, euros, eurosCorto, hoyIso } from './modelo';

// Coche y deudas: lo que se va pagando hasta un total (el coche, un préstamo…), con el anillo de progreso,
// los pagos por mes, cuándo se acaba al ritmo actual y lo que ha costado el coche en total.
@Component({
  selector: 'app-deudas',
  imports: [MantenimientoCoche],
  template: `
    <app-mantenimiento />
    @for (d of datos()?.deudas ?? []; track d.id) {
      <section class="caja deuda" [style.--c]="categoria(d.categoria).color">
        <div class="deuda__anillo" data-inclina role="img" [attr.aria-label]="'Pagado el ' + d.porcentaje + ' por ciento'">
          <svg viewBox="0 0 120 120" aria-hidden="true">
            <circle class="pista" cx="60" cy="60" r="52" />
            <circle class="valor" cx="60" cy="60" r="52" [style.stroke-dasharray]="anillo(d.porcentaje)" />
          </svg>
          <span class="deuda__pct">{{ entero(d.porcentaje) }}<small>%</small></span>
          <span class="deuda__pct-sub">pagado</span>
        </div>

        <div class="deuda__datos">
          <header class="deuda__cab">
            <span class="icono-cat"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path [attr.d]="categoria(d.categoria).icono" /></svg></span>
            <h3>{{ d.nombre }}</h3>
            <button class="enlace" type="button" (click)="editar(d)">Editar</button>
          </header>
          <dl class="cifras">
            <div><dt>Pagado</dt><dd class="verde">{{ eurosCorto(d.pagado) }}</dd></div>
            <div><dt>Falta</dt><dd>{{ eurosCorto(d.restante) }}</dd></div>
            <div><dt>Total</dt><dd class="suave">{{ eurosCorto(d.total) }}</dd></div>
          </dl>
          <p class="deuda__ritmo">
            @if (d.restante === 0) { 🎉 <strong>¡Pagado del todo!</strong> El último pago fue el {{ diaCorto(d.finEstimado!) }}. }
            @else if (d.mediaMensual) {
              Pagas de media <strong>{{ euros(d.mediaMensual) }}</strong> al mes. A este ritmo terminas en <strong>{{ mesAnio(d.finEstimado) }}</strong>&nbsp;<span class="suave">({{ mesesQueFaltan(d) }})</span>.
            } @else { Aún no hay pagos apuntados. }
          </p>
          <div class="deuda__acciones">
            <button class="button" type="button" (click)="abrirPago(d)">
              <svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M12 5v14M5 12h14" /></svg> Apuntar pago
            </button>
            <span class="suave">{{ d.numPagos }} pagos</span>
          </div>
        </div>

        @if (d.porMes.length > 1) {
          <div class="meses" aria-label="Pagos por mes">
            @for (m of d.porMes; track m.anio * 12 + m.mes) {
              <div class="meses__col" [title]="nombreMes(m.anio, m.mes) + ': ' + euros(m.total)">
                <span class="meses__barra" [class.vacia]="!m.total" [style.--h]="alto(d, m.total)"></span>
                <span class="meses__etq">{{ inicialMes(m.mes) }}</span>
              </div>
            }
          </div>
        }

        <details class="pagos">
          <summary>Ver los pagos</summary>
          @for (p of d.pagos; track p.id) {
            <div class="mov">
              <span class="icono-cat"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path [attr.d]="categoria(d.categoria).icono" /></svg></span>
              <span class="mov__texto"><strong>{{ p.concepto }}</strong><span>{{ diaCorto(p.fecha) }}@if (p.notas) { · {{ p.notas }} }</span></span>
              <span class="mov__importe">{{ euros(p.importe) }}</span>
            </div>
          }
        </details>
      </section>
    }

    @for (gr of datos()?.grupos ?? []; track gr.grupo) {
      @if (gr.total > 0) {
        <section class="caja coste">
          <header class="caja__cab"><h3>{{ gr.grupo === 'Coche' ? 'Lo que te ha costado el coche' : 'Gastos de ' + gr.grupo }}</h3><span class="sub">todo lo apuntado</span></header>
          <strong class="grande">{{ euros(gr.total) }}</strong>
          <ul class="coste__lista">
            @for (c of gr.porCategoria; track c.categoria) {
              <li [style.--c]="categoria(c.categoria).color">
                <span class="icono-cat"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path [attr.d]="categoria(c.categoria).icono" /></svg></span>
                <span class="coste__nombre">{{ categoria(c.categoria).nombre }}<small>{{ c.cantidad }} apuntes</small></span>
                <span class="coste__barra"><span [style.width.%]="c.total / gr.total * 100"></span></span>
                <span class="mov__importe">{{ eurosCorto(c.total) }}</span>
              </li>
            }
          </ul>
        </section>
      }
    }

    @if (datos() && !datos()!.deudas.length) {
      <div class="caja vacio">
        <strong>Sin deudas</strong>
        Apunta el coche, un préstamo o una compra a plazos y ve cuánto te queda.
        <button class="button" type="button" (click)="editar()">Nueva deuda</button>
      </div>
    } @else if (datos()) {
      <button class="button button--ghost nueva" type="button" (click)="editar()">Otra deuda</button>
    } @else {
      <p class="cargando">Cargando…</p>
    }

    <!-- apuntar un pago -->
    <dialog class="dlg" #dlgPago (click)="fuera($event, dlgPago)" aria-labelledby="dlg-pago-t">
      <form class="dlg__caja" (submit)="pagar($event)" novalidate>
        <header class="dlg__cabeza">
          <h2 id="dlg-pago-t">Pago · {{ pagando()?.nombre }}</h2>
          <button class="dlg__x" type="button" (click)="dlgPago.close()" aria-label="Cerrar"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6l12 12M18 6 6 18" /></svg></button>
        </header>
        <label class="importe">
          <span class="sr">Importe</span>
          <input type="text" inputmode="decimal" placeholder="0" [value]="importe()" (input)="importe.set(valor($event))" autocomplete="off">
          <span class="importe__simbolo">€</span>
        </label>
        @if (pagando(); as d) {
          <p class="dlg__lead">Te quedan {{ euros(d.restante) }}@if (num(importe()) > 0) {; después de este pago, <strong>{{ euros(d.restante - num(importe())) }}</strong>}.</p>
        }
        <div class="fila2">
          <label class="campo"><span class="campo__etiqueta">Fecha</span><input class="campo__input" type="date" [value]="fecha()" (input)="fecha.set(valor($event))"></label>
          <label class="campo"><span class="campo__etiqueta">Nota <em>(opcional)</em></span><input class="campo__input" type="text" maxlength="500" [value]="nota()" (input)="nota.set(valor($event))"></label>
        </div>
        @for (e of erroresLista(); track e) { <p class="error">{{ e }}</p> }
        <footer class="dlg__pie">
          <button class="button button--ghost" type="button" (click)="dlgPago.close()">Cancelar</button>
          <button class="button" type="submit" [disabled]="guardando()">{{ guardando() ? 'Guardando…' : 'Apuntar' }}</button>
        </footer>
      </form>
    </dialog>

    <!-- crear o editar una deuda -->
    <dialog class="dlg" #dlgDeuda (click)="fuera($event, dlgDeuda)" aria-labelledby="dlg-deuda-t">
      <form class="dlg__caja" (submit)="guardarDeuda($event)" novalidate>
        <header class="dlg__cabeza">
          <h2 id="dlg-deuda-t">{{ editando() ? 'Editar deuda' : 'Nueva deuda' }}</h2>
          <button class="dlg__x" type="button" (click)="dlgDeuda.close()" aria-label="Cerrar"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6l12 12M18 6 6 18" /></svg></button>
        </header>
        <label class="campo"><span class="campo__etiqueta">Nombre</span><input class="campo__input" type="text" maxlength="80" placeholder="Coche, préstamo…" [value]="nombre()" (input)="nombre.set(valor($event))"></label>
        <div class="fila2">
          <label class="campo"><span class="campo__etiqueta">Total a pagar</span><input class="campo__input" type="text" inputmode="decimal" placeholder="9000" [value]="total()" (input)="total.set(valor($event))"></label>
          <label class="campo"><span class="campo__etiqueta">Desde</span><input class="campo__input" type="date" [value]="inicio()" (input)="inicio.set(valor($event))"></label>
        </div>
        <label class="campo">
          <span class="campo__etiqueta">Los pagos cuentan como</span>
          <select class="campo__input" [value]="cat()" (change)="cat.set(valor($event))">
            @for (c of categoriasGasto; track c.id) { <option [value]="c.id" [selected]="c.id === cat()">{{ c.grupo }} · {{ c.nombre }}</option> }
          </select>
        </label>
        @for (e of erroresLista(); track e) { <p class="error">{{ e }}</p> }
        <footer class="dlg__pie">
          @if (editando()) { <button class="boton-borrar" type="button" (click)="borrar()">{{ confirmar() ? '¿Seguro? Pulsa otra vez' : 'Eliminar' }}</button> }
          <button class="button button--ghost" type="button" (click)="dlgDeuda.close()">Cancelar</button>
          <button class="button" type="submit" [disabled]="guardando()">Guardar</button>
        </footer>
      </form>
    </dialog>
  `,
  styleUrls: ['./comun.css', './dialogos.css'],
  styles: `
    .deuda { display: grid; grid-template-columns: auto minmax(0, 1fr); gap: 22px 28px; align-items: center; overflow: hidden; }
    .deuda::before { content: ''; position: absolute; inset: -40% auto auto -10%; width: 360px; height: 360px; border-radius: 50%; pointer-events: none; background: radial-gradient(circle, color-mix(in srgb, var(--c) 22%, transparent), transparent 70%); }
    .deuda__anillo { position: relative; width: clamp(150px, 30vw, 200px); aspect-ratio: 1; display: grid; place-items: center; transform: perspective(700px) rotateX(var(--rx, 0deg)) rotateY(var(--ry, 0deg)); transition: transform 300ms var(--ease-out); }
    .deuda__anillo svg { position: absolute; inset: 0; rotate: -90deg; filter: drop-shadow(0 10px 24px color-mix(in srgb, var(--c) 35%, transparent)); }
    .deuda__anillo circle { fill: none; stroke-width: 12; }
    .pista { stroke: var(--tarjeta-2); }
    .valor { stroke: var(--c); stroke-linecap: round; transition: stroke-dasharray 1200ms var(--ease-expo); animation: llena 1400ms var(--ease-expo) backwards 200ms; }
    @keyframes llena { from { stroke-dasharray: 0 400; } }
    .deuda__pct { position: relative; font: 700 clamp(2rem, 1.4rem + 2vw, 3rem)/1 var(--font-display); letter-spacing: -0.05em; margin-top: -10px; }
    .deuda__pct small { font-size: 0.45em; color: var(--soft); }
    .deuda__pct-sub { position: absolute; top: 62%; font-size: 12px; text-transform: uppercase; letter-spacing: 0.08em; color: var(--soft); }
    .deuda__datos { display: flex; flex-direction: column; gap: 14px; min-width: 0; position: relative; }
    .deuda__cab { display: flex; align-items: center; gap: 12px; }
    .deuda__cab h3 { flex: 1; min-width: 0; font: 700 var(--step-head)/1 var(--font-display); text-transform: uppercase; letter-spacing: -0.04em; overflow: hidden; text-overflow: ellipsis; }
    .cifras { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 10px; margin: 0; }
    .cifras div { display: flex; flex-direction: column; gap: 4px; padding: 12px 10px; border-radius: 14px; background: var(--tarjeta-2); min-width: 0; container-type: inline-size; }
    .cifras dt { font-size: 11px; text-transform: uppercase; letter-spacing: 0.06em; color: var(--soft); }
    .cifras dd { margin: 0; font: 700 clamp(0.75rem, 17cqi, 1.5rem)/1.1 var(--font-display); letter-spacing: -0.03em; white-space: nowrap; }
    .suave { color: var(--soft); }
    .deuda__ritmo { color: var(--soft); line-height: 1.5; }
    .deuda__ritmo strong { color: var(--texto); }
    .deuda__acciones { display: flex; align-items: center; gap: 14px; }
    .deuda__acciones .icon { width: 18px; height: 18px; }
    .meses { grid-column: 1 / -1; display: flex; align-items: flex-end; gap: 6px; height: 120px; padding-top: 10px; }
    .meses__col { flex: 1; min-width: 0; height: 100%; display: flex; flex-direction: column; justify-content: flex-end; align-items: center; gap: 6px; }
    .meses__barra { width: 100%; max-width: 34px; height: calc(var(--h) * 1%); min-height: 4px; border-radius: 8px 8px 3px 3px; background: linear-gradient(to top, color-mix(in srgb, var(--c) 55%, transparent), var(--c)); transform-origin: bottom; animation: crece 900ms var(--ease-expo) backwards; }
    .meses__barra.vacia { background: var(--tarjeta-2); }
    .meses__col:nth-child(n) .meses__barra { animation-delay: calc(var(--i, 0) * 30ms); }
    @keyframes crece { from { transform: scaleY(0); } }
    .meses__etq { font: 500 10px/1 var(--font-code); color: var(--soft); text-transform: uppercase; }
    .pagos { grid-column: 1 / -1; }
    .pagos summary { cursor: pointer; color: var(--acento); font-weight: 500; padding: 4px 0; }
    .pagos[open] summary { margin-bottom: 8px; }
    .coste__lista { display: flex; flex-direction: column; gap: 10px; list-style: none; margin: 0; padding: 0; }
    .coste__lista li { display: grid; grid-template-columns: 40px minmax(0, 1.2fr) minmax(60px, 1fr) auto; align-items: center; gap: 12px; }
    .coste__nombre { display: flex; flex-direction: column; min-width: 0; font-weight: 500; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .coste__nombre small { font-size: 12px; color: var(--soft); font-weight: 400; }
    .coste__barra { height: 8px; border-radius: 99px; background: var(--tarjeta-2); overflow: hidden; }
    .coste__barra span { display: block; height: 100%; border-radius: inherit; background: var(--c); animation: crece-x 900ms var(--ease-expo) backwards; transform-origin: left; }
    @keyframes crece-x { from { transform: scaleX(0); } }
    .nueva { align-self: flex-start; }
    .fila2 { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; }
    .error { color: var(--bad); font-size: var(--step-small); }
    select.campo__input { appearance: auto; }
    .vacio .button { margin-top: 8px; }
    @media (max-width: 640px) {
      .deuda { grid-template-columns: minmax(0, 1fr); justify-items: center; }
      .deuda__datos { width: 100%; }
      .coste__lista li { grid-template-columns: 40px minmax(0, 1fr) auto; }
      .coste__barra { grid-column: 2 / -1; grid-row: 2; }
      .fila2 { grid-template-columns: minmax(0, 1fr); }
    }
  `,
})
export class Deudas {
  private readonly g = inject(GastosService);
  private readonly dlgPago = viewChild.required<ElementRef<HTMLDialogElement>>('dlgPago');
  private readonly dlgDeuda = viewChild.required<ElementRef<HTMLDialogElement>>('dlgDeuda');

  protected readonly datos = signal<Datos | null>(null);
  protected readonly categoria = categoria;
  protected readonly euros = euros;
  protected readonly eurosCorto = eurosCorto;
  protected readonly diaCorto = diaCorto;
  protected readonly categoriasGasto = CATEGORIAS.filter((c) => c.tipo === 'Gasto' && c.id !== 'gasolina');

  // pago
  protected readonly pagando = signal<Deuda | null>(null);
  protected readonly importe = signal('');
  protected readonly fecha = signal(hoyIso());
  protected readonly nota = signal('');
  // deuda
  protected readonly editando = signal<Deuda | null>(null);
  protected readonly nombre = signal('');
  protected readonly total = signal('');
  protected readonly inicio = signal(hoyIso());
  protected readonly cat = signal('cuota-coche');
  protected readonly confirmar = signal(false);

  protected readonly errores = signal<Errores>({});
  protected readonly erroresLista = computed(() => Object.values(this.errores()).flat());
  protected readonly guardando = signal(false);

  constructor() {
    effect(() => {
      this.g.version();
      untracked(async () => { try { this.datos.set(await this.g.deudas()); } catch { this.datos.set({ deudas: [], grupos: [] }); } });
    });
  }

  protected valor(e: Event): string { return (e.target as HTMLInputElement).value; }
  protected num(t: string): number { return Number(t.replace(/\./g, '').replace(',', '.')) || 0; }
  protected entero(n: number): string { return Math.floor(n).toString(); }
  protected anillo(pct: number): string { const l = 2 * Math.PI * 52; return `${(Math.min(pct, 100) / 100) * l} ${l}`; }
  protected alto(d: Deuda, total: number): number { const max = Math.max(...d.porMes.map((m) => m.total)); return max ? Math.max(4, (total / max) * 100) : 4; }
  protected inicialMes(m: number): string { return MESES[m - 1].slice(0, 1); }
  protected nombreMes(a: number, m: number): string { return `${MESES[m - 1]} ${a}`; }
  protected mesAnio(iso: string | null): string {
    if (!iso) return '—';
    const d = new Date(`${iso}T12:00:00`);
    return `${MESES[d.getMonth()]} de ${d.getFullYear()}`;
  }
  protected mesesQueFaltan(d: Deuda): string {
    const n = d.mediaMensual ? Math.ceil(d.restante / d.mediaMensual) : 0;
    return n >= 12 ? `${Math.floor(n / 12)} año${n >= 24 ? 's' : ''}${n % 12 ? ` y ${n % 12} meses` : ''}` : `${n} meses`;
  }

  protected abrirPago(d: Deuda): void {
    this.pagando.set(d);
    this.importe.set('');
    this.fecha.set(hoyIso());
    this.nota.set('');
    this.errores.set({});
    this.abrir(this.dlgPago().nativeElement);
  }

  protected async pagar(e: Event): Promise<void> {
    e.preventDefault();
    const d = this.pagando();
    if (!d) return;
    this.guardando.set(true);
    try {
      await this.g.pagarDeuda(d.id, { importe: this.num(this.importe()), fecha: this.fecha(), notas: this.nota() || undefined });
      this.dlgPago().nativeElement.close();
      this.g.recargar();
    } catch (err) { this.errores.set(erroresDe(err)); } finally { this.guardando.set(false); }
  }

  protected editar(d?: Deuda): void {
    this.editando.set(d ?? null);
    this.nombre.set(d?.nombre ?? '');
    this.total.set(d ? String(d.total).replace('.', ',') : '');
    this.inicio.set(d?.inicio ?? hoyIso());
    this.cat.set(d?.categoria ?? 'cuota-coche');
    this.confirmar.set(false);
    this.errores.set({});
    this.abrir(this.dlgDeuda().nativeElement);
  }

  protected async guardarDeuda(e: Event): Promise<void> {
    e.preventDefault();
    this.guardando.set(true);
    try {
      await this.g.guardarDeuda({ nombre: this.nombre().trim(), total: this.num(this.total()), categoria: this.cat(), inicio: this.inicio(), notas: this.editando()?.notas ?? null }, this.editando()?.id);
      this.dlgDeuda().nativeElement.close();
      this.g.recargar();
    } catch (err) { this.errores.set(erroresDe(err)); } finally { this.guardando.set(false); }
  }

  protected async borrar(): Promise<void> {
    if (!this.confirmar()) { this.confirmar.set(true); return; }
    try {
      await this.g.borrarDeuda(this.editando()!.id);
      this.dlgDeuda().nativeElement.close();
      this.g.recargar();
    } catch (err) {
      // si no se pudo, se dice y el botón vuelve a pedir confirmación
      this.confirmar.set(false);
      this.errores.set(erroresDe(err));
    }
  }

  private soltar?: () => void;
  private abrir(dlg: HTMLDialogElement): void {
    dlg.showModal();
    this.soltar?.();
    this.soltar = seguirTeclado(dlg);
    dlg.addEventListener('close', () => { this.soltar?.(); this.soltar = undefined; }, { once: true });
  }
  protected fuera(e: MouseEvent, dlg: HTMLDialogElement): void { if (e.target === dlg) dlg.close(); }
}
