import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { CATEGORIAS, euros } from './modelo';

interface Mapeo { fecha: number; concepto: number; importe: number | null; cargo: number | null; abono: number | null; }
interface Fila { fecha: string; concepto: string; importe: number; tipo: 'Gasto' | 'Ingreso'; categoria: string; duplicado: boolean; incluir?: boolean; }
interface Analisis { separador: string; columnas: string[]; mapeo: Mapeo | null; filas: Fila[]; errores: string[]; }

// Importar el extracto del banco: eliges el CSV, ves qué se ha entendido (con la categoría que sugieren tus reglas y los duplicados sin marcar) y solo entra lo que confirmas.
@Component({
  selector: 'app-importar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  styleUrls: ['./comun.css'],
  template: `
    <section class="caja">
      <header class="caja__cab"><h3>Importar extracto</h3><a routerLink="/gastos/movimientos" class="enlace">Volver a movimientos</a></header>
      @if (!analisis()) {
        <p class="ayuda">Descarga el extracto de tu banco en CSV (Excel) y elígelo aquí. Se lee en este equipo: no sale de tu casa.</p>
        <label class="archivo"><input type="file" accept=".csv,.txt,text/csv" (change)="elegir($any($event.target).files?.[0])"> Elegir archivo CSV</label>
      }
      @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
      @if (hecho() !== null) { <p class="ok" role="status">✅ Se han importado {{ hecho() }} movimientos.</p> }

      @if (analisis(); as a) {
        @if (!a.mapeo) {
          <p class="ayuda">No he reconocido las columnas. Dime cuáles son:</p>
          <div class="rejilla">
            <label>Fecha<select (change)="manual.update(m => ({ ...m, fecha: +$any($event.target).value }))">@for (c of a.columnas; track $index) { <option [value]="$index">{{ c }}</option> }</select></label>
            <label>Concepto<select (change)="manual.update(m => ({ ...m, concepto: +$any($event.target).value }))">@for (c of a.columnas; track $index) { <option [value]="$index" [selected]="$index === 1">{{ c }}</option> }</select></label>
            <label>Importe<select (change)="manual.update(m => ({ ...m, importe: +$any($event.target).value }))">@for (c of a.columnas; track $index) { <option [value]="$index" [selected]="$index === 2">{{ c }}</option> }</select></label>
          </div>
          <button type="button" class="boton" (click)="usarManual()">Probar con estas columnas</button>
        } @else {
          <p class="resumen"><b>{{ elegidas().length }}</b> de {{ a.filas.length }} filas se importarán{{ duplicadas() ? ' · ' + duplicadas() + ' ya estaban apuntadas' : '' }}.</p>
          <ul class="filas">
            @for (f of a.filas; track $index) {
              <li [class.dup]="f.duplicado" [class.off]="!f.incluir">
                <input type="checkbox" [checked]="f.incluir" (change)="marcar($index, $any($event.target).checked)" [attr.aria-label]="'Importar ' + f.concepto">
                <span class="f__texto"><strong>{{ f.concepto }}</strong><small>{{ f.fecha }}{{ f.duplicado ? ' · posible duplicado' : '' }}</small></span>
                @if (f.tipo === 'Gasto') {
                  <select class="f__cat" [attr.aria-label]="'Categoría de ' + f.concepto" (change)="cambiarCategoria($index, $any($event.target).value)">
                    @for (c of gastos; track c.id) { <option [value]="c.id" [selected]="c.id === f.categoria">{{ c.nombre }}</option> }
                  </select>
                } @else { <span class="f__cat f__ing">Ingreso</span> }
                <b class="f__imp" [class.verde]="f.tipo === 'Ingreso'">{{ f.tipo === 'Gasto' ? '−' : '+' }}{{ euros(f.importe) }}</b>
              </li>
            }
          </ul>
          <div class="acciones">
            <button type="button" class="enlace" (click)="reiniciar()">Elegir otro archivo</button>
            <button type="button" class="boton" (click)="importar()" [disabled]="!elegidas().length || ocupado()">Importar {{ elegidas().length }}</button>
          </div>
        }
        @if (a.errores.length) { <details class="errores"><summary>{{ a.errores.length }} filas que no he entendido</summary><ul>@for (e of a.errores; track $index) { <li>{{ e }}</li> }</ul></details> }
      }
    </section>
  `,
  styles: `
    .ayuda { color: var(--soft); margin: 0 0 12px; }
    .archivo { display: inline-flex; align-items: center; justify-content: center; min-height: 44px; padding: 0 18px; border-radius: 999px; background: var(--brand); color: var(--on-brand); font-weight: 600; cursor: pointer; }
    .archivo input { position: absolute; opacity: 0; width: 1px; height: 1px; }
    .archivo:focus-within { outline: 2px solid var(--texto); outline-offset: 2px; }
    .ok { color: #3fb96a; font-weight: 600; }
    .resumen { margin: 0 0 10px; }
    .rejilla { display: grid; grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); gap: 10px; margin-bottom: 12px; }
    label { display: grid; gap: 4px; font-size: .78rem; color: var(--soft); }
    select { min-height: 40px; border-radius: 12px; border: 1px solid var(--vidrio-borde); background: color-mix(in srgb, var(--texto) 5%, transparent); color: var(--texto); padding: 0 10px; font: 500 .88rem/1 var(--font-text); max-width: 100%; }
    .filas { list-style: none; margin: 0; padding: 0; display: grid; gap: 4px; max-height: 60vh; overflow: auto; }
    .filas li { display: grid; grid-template-columns: 28px minmax(0, 1fr) auto auto; gap: 8px; align-items: center; padding: 6px 8px; border-radius: 12px; border: 1px solid var(--vidrio-borde); }
    .filas li.dup { border-style: dashed; }
    .filas li.off { opacity: .5; }
    .filas input[type='checkbox'] { width: 20px; height: 20px; accent-color: var(--brand); }
    .f__texto { display: grid; min-width: 0; }
    .f__texto strong { font-weight: 500; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .f__texto small { color: var(--soft); font-size: .74rem; }
    .f__cat { max-width: 130px; }
    .f__ing { color: var(--soft); font-size: .8rem; text-align: center; }
    .f__imp { font: 600 .84rem/1 var(--font-code); white-space: nowrap; }
    .acciones { display: flex; justify-content: space-between; align-items: center; gap: 10px; margin-top: 12px; flex-wrap: wrap; }
    .errores { margin-top: 12px; color: var(--soft); font-size: .8rem; }
    @media (max-width: 520px) { .filas li { grid-template-columns: 28px minmax(0, 1fr) auto; } .f__cat { grid-column: 2 / -1; max-width: none; } }
  `,
})
export class Importar {
  private readonly http = inject(HttpClient);
  protected readonly euros = euros;
  protected readonly gastos = CATEGORIAS.filter((c) => c.tipo === 'Gasto');
  protected readonly analisis = signal<Analisis | null>(null);
  protected readonly error = signal('');
  protected readonly hecho = signal<number | null>(null);
  protected readonly ocupado = signal(false);
  protected readonly manual = signal<Mapeo>({ fecha: 0, concepto: 1, importe: 2, cargo: null, abono: null });
  private texto = '';
  protected readonly elegidas = computed(() => (this.analisis()?.filas ?? []).filter((f) => f.incluir));
  protected readonly duplicadas = computed(() => (this.analisis()?.filas ?? []).filter((f) => f.duplicado).length);

  protected async elegir(archivo: File | undefined): Promise<void> {
    if (!archivo) return;
    this.hecho.set(null);
    if (archivo.size > 1_500_000) { this.error.set('El archivo es demasiado grande (máximo 1,5 MB).'); return; }
    this.texto = await archivo.text();
    await this.analizar(null);
  }

  private async analizar(mapeo: Mapeo | null): Promise<void> {
    this.error.set('');
    try {
      const a = await firstValueFrom(this.http.post<Analisis>('/api/gastos/importar/analizar', { texto: this.texto, mapeo }));
      // lo que ya estaba apuntado empieza desmarcado
      this.analisis.set({ ...a, filas: a.filas.map((f) => ({ ...f, incluir: !f.duplicado })) });
    } catch (e) { this.error.set(this.mensaje(e)); }
  }

  protected usarManual(): void { void this.analizar(this.manual()); }
  protected reiniciar(): void { this.analisis.set(null); this.texto = ''; this.error.set(''); }

  protected marcar(i: number, v: boolean): void { this.cambiar(i, { incluir: v }); }
  protected cambiarCategoria(i: number, c: string): void { this.cambiar(i, { categoria: c }); }
  private cambiar(i: number, parche: Partial<Fila>): void {
    this.analisis.update((a) => (a ? { ...a, filas: a.filas.map((f, n) => (n === i ? { ...f, ...parche } : f)) } : a));
  }

  protected async importar(): Promise<void> {
    this.ocupado.set(true); this.error.set('');
    try {
      const filas = this.elegidas().map(({ fecha, concepto, importe, tipo, categoria }) => ({ fecha, concepto, importe, tipo, categoria }));
      const r = await firstValueFrom(this.http.post<{ importados: number }>('/api/gastos/importar', { filas }));
      this.hecho.set(r.importados); this.reiniciar();
    } catch (e) { this.error.set(this.mensaje(e)); }
    finally { this.ocupado.set(false); }
  }

  private mensaje(e: unknown): string {
    const err = (e as HttpErrorResponse).error as { errors?: Record<string, string[]> } | undefined;
    return Object.values(err?.errors ?? {})[0]?.[0] ?? 'No se pudo leer el archivo.';
  }
}
