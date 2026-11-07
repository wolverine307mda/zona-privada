import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { MapaGasto, type ZonaMapa } from './mapa-gasto';
import { CATEGORIAS, euros } from './modelo';

interface ComercioTotal { nombre: string; total: number; pagos: number; zonas: number; }
interface CategoriaLugar { id: string; total: number; pagos: number; }
interface Respuesta { zonas: ZonaMapa[]; comercios: ComercioTotal[]; categorias: CategoriaLugar[]; conUbicacion: number; }

// Dónde gastas: un mapa de verdad (OpenStreetMap) con un círculo por zona de ~1 km, tanto más grande cuanto más se gasta, y los comercios que más pesan.
// Se puede filtrar por mes, año y categoría. Los pagos traen la ubicación solo si el atajo de Apple Pay del iPhone la manda.
@Component({
  selector: 'app-lugares',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MapaGasto],
  styleUrls: ['./comun.css'],
  template: `
    <section class="caja">
      <header class="caja__cab"><h3>Dónde gastas</h3>
        <div class="selector" role="group" aria-label="Periodo">
          <button type="button" [attr.aria-pressed]="!anio()" (click)="cambiar(false)">Este mes</button>
          <button type="button" [attr.aria-pressed]="anio()" (click)="cambiar(true)">Este año</button>
        </div>
      </header>
      @if (datos(); as d) {
        @if (d.conUbicacion > 0) {
          @if (d.categorias.length > 1) {
            <div class="filtro" role="group" aria-label="Categoría">
              <button type="button" [attr.aria-pressed]="categoria() === ''" (click)="filtrar('')">Todas</button>
              @for (c of d.categorias; track c.id) {
                <button type="button" [attr.aria-pressed]="categoria() === c.id" (click)="filtrar(c.id)">{{ nombre(c.id) }} <small>{{ eurosCorto(c.total) }}</small></button>
              }
            </div>
          }
          @if (d.zonas.length) {
            <app-mapa-gasto [zonas]="d.zonas" />
            <ol class="top">
              @for (z of d.zonas.slice(0, 3); track z.lat + ',' + z.lon) {
                <li><span>{{ z.comercios[0]?.nombre }}{{ z.comercios.length > 1 ? ' y ' + (z.comercios.length - 1) + ' más' : '' }} <small>{{ z.pagos }} pagos</small></span><b>{{ euros(z.total) }}</b></li>
              }
            </ol>
            @if (d.comercios.length) {
              <h4 class="titulillo">Comercios con más gasto</h4>
              <ol class="top">@for (c of d.comercios.slice(0, 5); track c.nombre) { <li><span>{{ c.nombre }} <small>{{ c.pagos }} pagos{{ c.zonas > 1 ? ' en ' + c.zonas + ' sitios' : '' }}</small></span><b>{{ euros(c.total) }}</b></li> }</ol>
            }
          } @else { <p class="vacio">No hay pagos de esa categoría con ubicación en este periodo.</p> }
        } @else { <p class="vacio">Aún no hay pagos con ubicación. Añade «Obtener ubicación actual» al atajo de Apple Pay y manda lat y lon (Seguridad → Atajos).</p> }
      }
    </section>
  `,
  styles: `
    .selector, .filtro { display: inline-flex; flex-wrap: wrap; gap: 4px; }
    .filtro { margin-bottom: 10px; }
    .selector button, .filtro button { border: 1px solid var(--vidrio-borde); background: none; color: var(--soft); border-radius: 999px; padding: 6px 12px; font: 500 .78rem/1 var(--font-text); min-height: 32px; cursor: pointer; }
    .selector button[aria-pressed='true'], .filtro button[aria-pressed='true'] { background: var(--brand); border-color: var(--brand); color: var(--on-brand); }
    .filtro small { opacity: .75; margin-left: 2px; }
    .top { margin: 0; padding: 0; list-style: none; display: grid; gap: 6px; }
    .top li { display: flex; justify-content: space-between; gap: 10px; font-size: .88rem; }
    .top small { color: var(--soft); }
    .top b { font: 600 .86rem/1.2 var(--font-code); }
    .titulillo { margin: 14px 0 6px; font-size: .72rem; font-weight: 600; letter-spacing: .1em; text-transform: uppercase; color: var(--soft); }
    .vacio { color: var(--soft); margin: 0; }
  `,
})
export class Lugares implements OnInit {
  private readonly http = inject(HttpClient);
  protected readonly euros = euros;
  protected readonly datos = signal<Respuesta | null>(null);
  protected readonly anio = signal(false);
  protected readonly categoria = signal('');

  ngOnInit(): void { void this.leer(); }

  protected cambiar(anio: boolean): void { this.anio.set(anio); this.categoria.set(''); void this.leer(); }
  protected filtrar(id: string): void { this.categoria.set(id); void this.leer(); }
  protected nombre(id: string): string { return CATEGORIAS.find((c) => c.id === id)?.nombre ?? id; }
  protected eurosCorto(n: number): string { return n >= 1000 ? `${(n / 1000).toFixed(1).replace('.', ',')} k€` : `${Math.round(n)} €`; }

  private async leer(): Promise<void> {
    const q = new URLSearchParams();
    if (this.anio()) q.set('mes', '0');
    if (this.categoria()) q.set('categoria', this.categoria());
    try { this.datos.set(await firstValueFrom(this.http.get<Respuesta>('/api/gastos/lugares' + (q.size ? '?' + q : '')))); } catch { this.datos.set(null); }
  }
}
