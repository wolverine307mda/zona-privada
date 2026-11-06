import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { EstadoError, EstadoVacio, Esqueleto } from '../ui';

interface Articulo { id: number; nombre: string; cantidad: string; comprado: boolean; }
interface Sugerencia { nombre: string; dias: number; cadaDias: number; }
interface Estado { pendientes: Articulo[]; comprados: Articulo[]; sugerencias: Sugerencia[]; habituales: string[]; }

// La lista de la compra con memoria: al marcar algo como comprado aprende cada cuántos días lo compras y, cuando toca, te lo sugiere («ya toca leche»).
// Escribe varias cosas separadas por comas. Por Telegram: «/compra leche, pan», «/compra» y «/comprado leche».
@Component({
  selector: 'app-compra',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [EstadoError, EstadoVacio, Esqueleto],
  styleUrls: ['../gastos/comun.css'],
  template: `
    <header class="cab"><p class="cab__sub" aria-live="polite">{{ resumen() }}</p></header>

    <form class="rapida" (submit)="agregar($event)">
      <input class="campo__input" type="text" maxlength="400" placeholder="Leche, pan, huevos… (Intro para añadir)" aria-label="Qué hay que comprar" [value]="texto()" (input)="texto.set($any($event.target).value)">
      <button class="button" type="submit" [disabled]="!texto().trim() || guardando()">Añadir</button>
    </form>
    @if (error()) { <p class="error" role="alert">{{ error() }}</p> }

    @if (fallo()) { <app-estado-error titulo="No pude cargar la compra" (reintentar)="cargar()" /> }
    @else if (datos() === null) { <section class="caja"><app-esqueleto forma="bloque" alto="48px" /><app-esqueleto forma="bloque" alto="48px" /></section> }
    @else {
      @if (datos()!.sugerencias.length) {
        <section class="caja">
          <header class="caja__cab"><h3>Ya toca</h3><span class="sub">por lo que sueles tardar en volver a comprarlo</span></header>
          <div class="chips">
            @for (s of datos()!.sugerencias; track s.nombre) {
              <span class="chip-par">
                <button type="button" class="chip chip--toca" (click)="anadir(s.nombre)" [title]="'Lo compras cada ~' + s.cadaDias + ' días; hace ' + s.dias">+ {{ s.nombre }}</button>
                <button type="button" class="chip-x" (click)="olvidar(s.nombre)" [attr.aria-label]="'No sugerir más ' + s.nombre" title="No sugerir más">×</button>
              </span>
            }
          </div>
        </section>
      }

      @if (datos()!.pendientes.length === 0) {
        <app-estado-vacio icono="M6 6h15l-1.5 9h-12zM6 6 5 3H2M9 20h.01M18 20h.01" titulo="La lista está vacía" texto="Escribe arriba lo que falta o toca uno de los habituales." />
      } @else {
        <ul class="lista" aria-label="Por comprar">
          @for (a of datos()!.pendientes; track a.id) {
            <li class="art">
              <button type="button" class="check" [attr.aria-label]="'Comprado: ' + a.nombre" [disabled]="ocupado() === a.id" (click)="marcar(a, true)">
                <svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M5 12.5l4.2 4.2L19 7" /></svg>
              </button>
              <span class="art__texto"><strong>{{ a.nombre }}</strong>@if (a.cantidad) { <small>{{ a.cantidad }}</small> }</span>
              <button type="button" class="quitar" (click)="quitar(a)" [attr.aria-label]="'Quitar ' + a.nombre">×</button>
            </li>
          }
        </ul>
      }

      @if (datos()!.habituales.length) {
        <section class="caja">
          <header class="caja__cab"><h3>Habituales</h3><span class="sub">toca para añadir</span></header>
          <div class="chips">@for (h of datos()!.habituales; track h) { <button type="button" class="chip" (click)="anadir(h)">+ {{ h }}</button> }</div>
        </section>
      }

      @if (datos()!.comprados.length) {
        <section class="caja">
          <header class="caja__cab"><h3>Comprado hace poco</h3><span class="sub">toca para volver a ponerlo en la lista</span></header>
          <div class="chips">@for (a of datos()!.comprados; track a.id) { <button type="button" class="chip chip--hecho" (click)="marcar(a, false)">{{ a.nombre }}</button> }</div>
        </section>
      }
    }
  `,
  styles: `
    .cab__sub { margin: 0; font: var(--peso-ligero) 1.3rem/1.2 var(--font-text); }
    .cab { margin-bottom: var(--esp-4); }
    .rapida { display: flex; gap: 10px; margin-bottom: var(--esp-4); }
    .rapida input { flex: 1; min-width: 0; }
    .lista { list-style: none; margin: 0 0 var(--esp-4); padding: 0; display: grid; gap: 8px; }
    .art { display: flex; align-items: center; gap: 12px; padding: 8px 14px; border-radius: var(--radio-l); border: 1px solid var(--vidrio-borde); background: var(--vidrio); }
    .art__texto { flex: 1; display: grid; min-width: 0; }
    .art__texto strong { font-weight: 500; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .art__texto small { color: var(--soft); font-size: .78rem; }
    .check { flex: none; display: grid; place-items: center; width: 44px; height: 44px; border-radius: 50%; border: 2px solid var(--linea); background: transparent; color: transparent; cursor: pointer; transition: background var(--dur-2) var(--ease-expo), color var(--dur-2); }
    .check .icon { width: 22px; height: 22px; stroke-width: 2.4; }
    .check:hover, .check:focus-visible { background: var(--ok); border-color: var(--ok); color: var(--on-negra, #05210f); }
    .quitar, .chip-x { width: 36px; height: 36px; border: 0; border-radius: 50%; background: transparent; color: var(--soft); font-size: 1.3rem; line-height: 1; cursor: pointer; }
    .chips { display: flex; flex-wrap: wrap; gap: 8px; }
    .chip { min-height: 40px; padding: 0 14px; border-radius: 999px; border: 1px solid var(--vidrio-borde); background: color-mix(in srgb, var(--texto) 6%, transparent); color: inherit; font: inherit; cursor: pointer; }
    .chip--toca { border-color: color-mix(in srgb, var(--acento) 60%, transparent); background: color-mix(in srgb, var(--acento) 14%, transparent); }
    .chip--hecho { opacity: .7; text-decoration: line-through; }
    .chip-par { display: inline-flex; align-items: center; }
    .chip-par .chip-x { width: 28px; height: 28px; font-size: 1rem; }
    .error { color: var(--bad); margin: 0 0 var(--esp-3); }
    @media (prefers-reduced-motion: reduce) { .check { transition: none; } }
  `,
})
export class Compra {
  private readonly http = inject(HttpClient);
  protected readonly datos = signal<Estado | null>(null);
  protected readonly fallo = signal(false);
  protected readonly texto = signal('');
  protected readonly error = signal('');
  protected readonly guardando = signal(false);
  protected readonly ocupado = signal<number | null>(null);

  protected readonly resumen = computed(() => {
    const d = this.datos();
    if (!d) return 'Lo que falta en casa';
    const n = d.pendientes.length;
    const toca = d.sugerencias.length;
    if (n === 0) return toca ? `Lista vacía · ${toca} por reponer` : 'Nada por comprar';
    return `${n} por comprar${toca ? ` · ${toca} que ya tocan` : ''}`;
  });

  constructor() { void this.cargar(); }

  protected async cargar(): Promise<void> {
    this.fallo.set(false);
    try { this.datos.set(await firstValueFrom(this.http.get<Estado>('/api/compra'))); } catch { this.fallo.set(true); }
  }

  protected async agregar(ev: Event): Promise<void> {
    ev.preventDefault();
    const nombre = this.texto().trim();
    if (!nombre) return;
    if (await this.anadir(nombre)) this.texto.set('');
  }

  protected async anadir(nombre: string): Promise<boolean> {
    if (this.guardando()) return false;
    this.guardando.set(true); this.error.set('');
    try { this.datos.set(await firstValueFrom(this.http.post<Estado>('/api/compra', { nombre }))); return true; }
    catch (x) { this.error.set(this.mensaje(x)); return false; }
    finally { this.guardando.set(false); }
  }

  protected async marcar(a: Articulo, comprado: boolean): Promise<void> {
    if (this.ocupado() !== null) return;
    this.ocupado.set(a.id);
    try { this.datos.set(await firstValueFrom(this.http.put<Estado>(`/api/compra/${a.id}`, { comprado }))); } catch { void this.cargar(); } finally { this.ocupado.set(null); }
  }

  protected async quitar(a: Articulo): Promise<void> {
    try { await firstValueFrom(this.http.delete(`/api/compra/${a.id}`)); await this.cargar(); } catch { void this.cargar(); }
  }

  protected async olvidar(nombre: string): Promise<void> {
    try { await firstValueFrom(this.http.delete(`/api/compra/habituales/${encodeURIComponent(nombre)}`)); await this.cargar(); } catch { void this.cargar(); }
  }

  private mensaje(x: unknown): string {
    const e = x as HttpErrorResponse;
    const errores = e.error?.errors as Record<string, string[]> | undefined;
    return (errores ? Object.values(errores).flat()[0] : null) ?? 'No se ha podido guardar.';
  }
}
