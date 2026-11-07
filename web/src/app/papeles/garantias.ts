import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';

type Situacion = 'vigente' | 'por-caducar' | 'caducada' | 'devuelta' | 'reclamada';
interface Garantia {
  id: number; movimientoId: number | null; producto: string; comercio: string; fechaCompra: string; garantiaHasta: string; devolucionHasta: string | null;
  estado: 'vigente' | 'devuelta' | 'reclamada'; notas: string; diasGarantia: number; diasDevolucion: number | null; situacion: Situacion;
}
interface Respuesta { reglaAnios: number; reglaDevolucionDias: number; garantias: Garantia[]; }
type Filtro = 'abiertas' | 'por-caducar' | 'cerradas';

const sumarDias = (iso: string, n: number): string => { const d = new Date(`${iso}T12:00:00`); d.setDate(d.getDate() + n); return d.toISOString().slice(0, 10); };
const sumarAnios = (iso: string, n: number): string => { const d = new Date(`${iso}T12:00:00`); d.setFullYear(d.getFullYear() + n); return d.toISOString().slice(0, 10); };

// Garantías y devoluciones: hasta cuándo puedes reclamar o devolver lo que compras. Por defecto 3 años de garantía legal y 14 días de devolución
// (la regla general para un bien nuevo; cámbialo si el producto o el comercio dan otra cosa). Avisa a 60, 30 y 7 días del fin, y 3 días antes de que acabe la devolución.
// Desde un gasto de 50 € o más sale el botón «Guardar garantía», que llega aquí con el producto, el comercio y la fecha rellenos.
@Component({
  selector: 'app-garantias',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrls: ['../gastos/comun.css', '../gastos/dialogos.css'],
  template: `
    <section class="gar" aria-labelledby="gar-t">
      <header class="gar__cab">
        <h2 id="gar-t">Garantías y devoluciones</h2>
        <div class="gar__acc">
          <div class="selector" role="group" aria-label="Filtro">
            @for (f of filtros; track f.id) { <button type="button" [attr.aria-pressed]="filtro() === f.id" (click)="filtro.set(f.id)">{{ f.nombre }}</button> }
          </div>
          <button class="button" type="button" (click)="nueva()">Nueva garantía</button>
        </div>
      </header>

      @if (fallo()) { <p class="gar__vacio">No se pudieron cargar. <button class="enlace" type="button" (click)="cargar()">Reintentar</button></p> }
      @else if (datos() === null) { <p class="gar__vacio" aria-busy="true">Cargando…</p> }
      @else if (visibles().length === 0) {
        <p class="gar__vacio">{{ datos()!.garantias.length === 0 ? 'Aún no hay ninguna. Guarda la garantía de lo que compras (la tele, el móvil, un electrodoméstico) y te aviso antes de que se acabe.' : 'Nada en este filtro.' }}</p>
      } @else {
        <ul class="gar__lista">
          @for (g of visibles(); track g.id) {
            <li>
              <button type="button" class="gar__item" [attr.data-s]="g.situacion" (click)="editar(g)" [attr.aria-label]="'Editar ' + g.producto">
                <strong>{{ g.producto }}</strong>
                <small>{{ g.comercio || 'Sin comercio' }} · comprado el {{ fecha(g.fechaCompra) }}</small>
                <small>Garantía hasta el {{ fecha(g.garantiaHasta) }}@if (g.devolucionHasta && g.diasDevolucion! >= 0 && g.estado === 'vigente') { · devolución hasta el {{ fecha(g.devolucionHasta) }} }</small>
                <span class="gar__etq">{{ etiqueta(g) }}</span>
              </button>
            </li>
          }
        </ul>
        <p class="gar__nota">Fechas según la regla por defecto ({{ datos()!.reglaAnios }} años de garantía y {{ datos()!.reglaDevolucionDias }} días de devolución): cada producto puede dar otra, y se cambia al editar.</p>
      }
    </section>

    <dialog class="dlg" #dialogo (click)="fuera($event)" (close)="cerrado()" aria-labelledby="dlg-gar">
      <form class="dlg__caja" (submit)="guardar($event)" novalidate>
        <header class="dlg__cabeza">
          <h2 id="dlg-gar">{{ editando() ? 'Editar garantía' : 'Nueva garantía' }}</h2>
          <button class="dlg__x" type="button" (click)="cerrar()" aria-label="Cerrar"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6l12 12M18 6 6 18" /></svg></button>
        </header>
        <label class="campo"><span class="campo__etiqueta">Producto</span>
          <input class="campo__input" type="text" maxlength="80" placeholder="Televisión, aspiradora, móvil…" [value]="producto()" (input)="producto.set(valor($event))"></label>
        <div class="fila2">
          <label class="campo"><span class="campo__etiqueta">Comercio</span>
            <input class="campo__input" type="text" maxlength="80" [value]="comercio()" (input)="comercio.set(valor($event))"></label>
          <label class="campo"><span class="campo__etiqueta">Comprado el</span>
            <input class="campo__input" type="date" [value]="compra()" (input)="cambiarCompra(valor($event))"></label>
        </div>
        <div class="fila2">
          <label class="campo"><span class="campo__etiqueta">Garantía hasta</span>
            <input class="campo__input" type="date" [value]="hasta()" (input)="hasta.set(valor($event))"></label>
          <label class="campo"><span class="campo__etiqueta">Devolución hasta</span>
            <input class="campo__input" type="date" [value]="devolucion()" [disabled]="sinDevolucion()" (input)="devolucion.set(valor($event))"></label>
        </div>
        <label class="gar__check"><input type="checkbox" [checked]="sinDevolucion()" (change)="sinDevolucion.set(marcado($event))"> <span>No se puede devolver (solo garantía)</span></label>
        @if (editando()) {
          <label class="campo"><span class="campo__etiqueta">Estado</span>
            <select class="campo__input" [value]="estado()" (change)="estado.set(valor($event))">
              <option value="vigente">Vigente</option><option value="reclamada">Reclamada (en trámite o arreglada)</option><option value="devuelta">Devuelta</option>
            </select></label>
        }
        <label class="campo"><span class="campo__etiqueta">Notas <em>(opcional: nº de serie, dónde está la factura…)</em></span>
          <textarea class="campo__input" rows="3" maxlength="500" [value]="notas()" (input)="notas.set(valor($event))"></textarea></label>
        @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
        <footer class="dlg__pie">
          @if (editando()) { <button type="button" class="boton-borrar" (click)="borrar()">{{ confirmarBorrado() ? 'Seguro: borrar' : 'Borrar' }}</button> }
          <button class="button" type="submit" [attr.aria-busy]="guardando() || null" [disabled]="guardando()">Guardar</button>
        </footer>
      </form>
    </dialog>
  `,
  styles: `
    .gar { margin-top: var(--esp-6, 32px); display: flex; flex-direction: column; gap: 12px; }
    .gar__cab { display: flex; align-items: center; justify-content: space-between; gap: 12px; flex-wrap: wrap; }
    .gar__cab h2 { margin: 0; font: var(--peso-ligero, 300) 1.3rem/1.2 var(--font-text); }
    .gar__acc { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; }
    .selector { display: inline-flex; flex-wrap: wrap; gap: 4px; }
    .selector button { border: 1px solid var(--vidrio-borde); background: none; color: var(--soft); border-radius: 999px; padding: 6px 12px; font: 500 .78rem/1 var(--font-text); min-height: 36px; cursor: pointer; }
    .selector button[aria-pressed='true'] { background: var(--brand); border-color: var(--brand); color: var(--on-brand); }
    .gar__lista { list-style: none; margin: 0; padding: 0; display: grid; gap: 8px; }
    .gar__item { width: 100%; display: grid; grid-template-columns: minmax(0, 1fr) auto; grid-template-areas: 'n e' 'a e' 'b e'; gap: 2px 12px; align-items: center; padding: 12px 16px; border-radius: var(--radio-l, 16px); border: 1px solid var(--vidrio-borde); background: var(--vidrio); color: inherit; font: inherit; text-align: left; cursor: pointer; }
    .gar__item strong { grid-area: n; font-weight: 500; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .gar__item small:nth-of-type(1) { grid-area: a; }
    .gar__item small:nth-of-type(2) { grid-area: b; }
    .gar__item small { color: var(--soft); font-size: .78rem; }
    .gar__etq { grid-area: e; padding: 4px 12px; border-radius: 999px; font-size: .8rem; background: color-mix(in srgb, var(--ok) 20%, transparent); white-space: nowrap; }
    .gar__item[data-s='por-caducar'] { border-color: color-mix(in srgb, var(--acento) 55%, transparent); }
    .gar__item[data-s='por-caducar'] .gar__etq { background: color-mix(in srgb, var(--acento) 25%, transparent); }
    .gar__item[data-s='caducada'] { border-color: color-mix(in srgb, var(--bad) 55%, transparent); }
    .gar__item[data-s='caducada'] .gar__etq { background: color-mix(in srgb, var(--bad) 30%, transparent); }
    .gar__item[data-s='devuelta'], .gar__item[data-s='reclamada'] { opacity: .7; }
    .gar__item[data-s='devuelta'] .gar__etq, .gar__item[data-s='reclamada'] .gar__etq { background: var(--tarjeta-2); }
    .gar__vacio, .gar__nota { margin: 0; color: var(--soft); font-size: .85rem; line-height: 1.5; }
    .gar__check { display: flex; align-items: center; gap: 10px; font-size: .9rem; }
    @media (max-width: 560px) { .gar__item { grid-template-columns: minmax(0, 1fr); grid-template-areas: 'e' 'n' 'a' 'b'; } .gar__etq { justify-self: start; } }
  `,
})
export class Garantias {
  private readonly http = inject(HttpClient);
  private readonly ruta = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly dialogo = viewChild.required<ElementRef<HTMLDialogElement>>('dialogo');

  protected readonly filtros: { id: Filtro; nombre: string }[] = [{ id: 'abiertas', nombre: 'Vigentes' }, { id: 'por-caducar', nombre: 'Por caducar' }, { id: 'cerradas', nombre: 'Cerradas' }];
  protected readonly filtro = signal<Filtro>('abiertas');
  protected readonly datos = signal<Respuesta | null>(null);
  protected readonly fallo = signal(false);

  protected readonly editando = signal<Garantia | null>(null);
  private movimientoId: number | null = null;
  protected readonly producto = signal('');
  protected readonly comercio = signal('');
  protected readonly compra = signal('');
  protected readonly hasta = signal('');
  protected readonly devolucion = signal('');
  protected readonly sinDevolucion = signal(false);
  protected readonly estado = signal('vigente');
  protected readonly notas = signal('');
  protected readonly error = signal('');
  protected readonly guardando = signal(false);
  protected readonly confirmarBorrado = signal(false);

  protected readonly visibles = computed(() => {
    const l = this.datos()?.garantias ?? [];
    switch (this.filtro()) {
      case 'por-caducar': return l.filter((g) => g.situacion === 'por-caducar' || (g.estado === 'vigente' && g.diasDevolucion !== null && g.diasDevolucion >= 0 && g.diasDevolucion <= 14));
      case 'cerradas': return l.filter((g) => g.situacion !== 'vigente' && g.situacion !== 'por-caducar');
      default: return l.filter((g) => g.situacion === 'vigente' || g.situacion === 'por-caducar');
    }
  });

  constructor() {
    void this.cargar().then(() => {
      // llega desde un gasto: /papeles?garantia=1&producto=…&comercio=…&fecha=…&movimiento=12
      const q = this.ruta.snapshot.queryParamMap;
      if (q.get('garantia')) {
        this.abrir(null, { producto: q.get('producto') ?? '', comercio: q.get('comercio') ?? '', fecha: q.get('fecha') ?? '', movimiento: Number(q.get('movimiento')) || null });
        void this.router.navigate([], { queryParams: {}, replaceUrl: true });
      }
    });
  }

  protected async cargar(): Promise<void> {
    this.fallo.set(false);
    try { this.datos.set(await firstValueFrom(this.http.get<Respuesta>('/api/garantias'))); } catch { this.fallo.set(true); }
  }

  protected fecha(iso: string): string { return new Date(`${iso}T12:00:00`).toLocaleDateString('es-ES', { day: 'numeric', month: 'short', year: 'numeric' }); }

  protected etiqueta(g: Garantia): string {
    if (g.situacion === 'devuelta') return 'Devuelta';
    if (g.situacion === 'reclamada') return 'Reclamada';
    if (g.situacion === 'caducada') return `Acabó hace ${this.largo(-g.diasGarantia)}`;
    if (g.diasDevolucion !== null && g.diasDevolucion >= 0 && g.diasDevolucion <= 14) return g.diasDevolucion === 0 ? 'Devolver: hoy' : `Devolver: ${g.diasDevolucion} d`;
    return g.diasGarantia === 0 ? 'Acaba hoy' : `Quedan ${this.largo(g.diasGarantia)}`;
  }

  private largo(d: number): string { return d < 60 ? `${d} d` : d < 730 ? `${Math.round(d / 30)} meses` : `${(d / 365).toFixed(1).replace('.', ',')} años`; }

  // ---- diálogo ----
  protected nueva(): void { this.abrir(null); }
  protected editar(g: Garantia): void { this.abrir(g); }

  private abrir(g: Garantia | null, desde?: { producto: string; comercio: string; fecha: string; movimiento: number | null }): void {
    this.editando.set(g);
    this.movimientoId = g?.movimientoId ?? desde?.movimiento ?? null;
    const compra = g?.fechaCompra ?? (desde?.fecha || new Date().toISOString().slice(0, 10));
    this.producto.set(g?.producto ?? desde?.producto ?? ''); this.comercio.set(g?.comercio ?? desde?.comercio ?? '');
    this.compra.set(compra);
    this.hasta.set(g?.garantiaHasta ?? sumarAnios(compra, this.datos()?.reglaAnios ?? 3));
    this.sinDevolucion.set(g ? g.devolucionHasta === null : false);
    this.devolucion.set(g?.devolucionHasta ?? sumarDias(compra, this.datos()?.reglaDevolucionDias ?? 14));
    this.estado.set(g?.estado ?? 'vigente'); this.notas.set(g?.notas ?? '');
    this.error.set(''); this.confirmarBorrado.set(false);
    this.dialogo().nativeElement.showModal();
  }

  // al cambiar la fecha de la compra en una garantía nueva, las dos fechas de fin siguen a la regla por defecto
  protected cambiarCompra(v: string): void {
    this.compra.set(v);
    if (!this.editando() && v) { this.hasta.set(sumarAnios(v, this.datos()?.reglaAnios ?? 3)); this.devolucion.set(sumarDias(v, this.datos()?.reglaDevolucionDias ?? 14)); }
  }

  protected cerrar(): void { this.dialogo().nativeElement.close(); }
  protected cerrado(): void { this.editando.set(null); }
  protected fuera(e: MouseEvent): void { if (e.target === this.dialogo().nativeElement) this.cerrar(); }
  protected valor(e: Event): string { return (e.target as HTMLInputElement).value; }
  protected marcado(e: Event): boolean { return (e.target as HTMLInputElement).checked; }

  protected async guardar(ev: Event): Promise<void> {
    ev.preventDefault();
    if (this.guardando()) return;
    if (!this.producto().trim()) { this.error.set('Ponle un nombre al producto.'); return; }
    if (!this.compra() || !this.hasta()) { this.error.set('Pon la fecha de la compra y la del fin de la garantía.'); return; }
    if (!this.sinDevolucion() && !this.devolucion()) { this.error.set('Pon hasta cuándo se puede devolver, o márcalo como «no se puede devolver».'); return; }
    this.guardando.set(true); this.error.set('');
    const cuerpo: Record<string, unknown> = {
      producto: this.producto().trim(), comercio: this.comercio().trim(), fechaCompra: this.compra(), garantiaHasta: this.hasta(),
      devolucionHasta: this.sinDevolucion() ? null : this.devolucion(), sinDevolucion: this.sinDevolucion(), notas: this.notas().trim(),
    };
    try {
      const e = this.editando();
      if (e) await firstValueFrom(this.http.put(`/api/garantias/${e.id}`, { ...cuerpo, estado: this.estado() }));
      else await firstValueFrom(this.http.post('/api/garantias', { ...cuerpo, movimientoId: this.movimientoId }));
      this.cerrar();
      await this.cargar();
    } catch (x) { this.error.set(this.mensaje(x)); } finally { this.guardando.set(false); }
  }

  protected async borrar(): Promise<void> {
    const e = this.editando(); if (!e) return;
    if (!this.confirmarBorrado()) { this.confirmarBorrado.set(true); return; }
    try { await firstValueFrom(this.http.delete(`/api/garantias/${e.id}`)); this.cerrar(); await this.cargar(); } catch (x) { this.error.set(this.mensaje(x)); }
  }

  private mensaje(x: unknown): string {
    const e = x as HttpErrorResponse;
    const errores = e.error?.errors as Record<string, string[]> | undefined;
    return (errores ? Object.values(errores).flat()[0] : null) ?? 'No se ha podido guardar.';
  }
}
