import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { euros } from './modelo';

interface Comercio { nombre: string; total: number; pagos: number; }
interface Zona { lat: number; lon: number; total: number; pagos: number; comercios: Comercio[]; }
interface ComercioTotal extends Comercio { zonas: number; }
interface Respuesta { zonas: Zona[]; comercios: ComercioTotal[]; conUbicacion: number; }

// Dónde gastas: un mapa de calor esquemático (cada círculo es una zona de ~1 km, tanto más grande cuanto más se gasta) y los comercios que más pesan.
// Los pagos traen la ubicación solo si el atajo de Apple Pay del iPhone la manda.
@Component({
  selector: 'app-lugares',
  changeDetection: ChangeDetectionStrategy.OnPush,
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
        @if (d.zonas.length) {
          <svg class="mapa" viewBox="0 0 300 170" role="img" [attr.aria-label]="'Mapa de calor de ' + d.zonas.length + ' zonas de gasto'">
            <rect x="1" y="1" width="298" height="168" rx="14" class="mapa__fondo" />
            @for (z of d.zonas; track z.lat + ',' + z.lon) {
              <circle [attr.cx]="x(z)" [attr.cy]="y(z)" [attr.r]="radio(z)" class="calor"><title>{{ euros(z.total) }} · {{ z.pagos }} pagos</title></circle>
            }
          </svg>
          <ol class="top">
            @for (z of d.zonas.slice(0, 3); track z.lat + ',' + z.lon) {
              <li><span>{{ z.comercios[0]?.nombre }}{{ z.comercios.length > 1 ? ' y ' + (z.comercios.length - 1) + ' más' : '' }} <small>{{ z.pagos }} pagos</small></span><b>{{ euros(z.total) }}</b></li>
            }
          </ol>
          @if (d.comercios.length) {
            <h4 class="titulillo">Comercios con más gasto</h4>
            <ol class="top">@for (c of d.comercios.slice(0, 5); track c.nombre) { <li><span>{{ c.nombre }} <small>{{ c.pagos }} pagos{{ c.zonas > 1 ? ' en ' + c.zonas + ' sitios' : '' }}</small></span><b>{{ euros(c.total) }}</b></li> }</ol>
          }
        } @else { <p class="vacio">Aún no hay pagos con ubicación. Añade «Obtener ubicación actual» al atajo de Apple Pay y manda lat y lon (Seguridad → Atajos).</p> }
      }
    </section>
  `,
  styles: `
    .selector { display: inline-flex; gap: 4px; }
    .selector button { border: 1px solid var(--vidrio-borde); background: none; color: var(--soft); border-radius: 999px; padding: 6px 12px; font: 500 .78rem/1 var(--font-text); min-height: 32px; cursor: pointer; }
    .selector button[aria-pressed='true'] { background: var(--brand); border-color: var(--brand); color: var(--on-brand); }
    .mapa { width: 100%; height: auto; max-height: 240px; display: block; margin-bottom: 10px; }
    .mapa__fondo { fill: color-mix(in srgb, var(--texto) 5%, transparent); stroke: var(--vidrio-borde); }
    .calor { fill: color-mix(in srgb, var(--brand) 45%, transparent); stroke: var(--brand); stroke-width: 1.5; }
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
  private readonly maximo = computed(() => Math.max(1, ...(this.datos()?.zonas.map((z) => z.total) ?? [1])));

  ngOnInit(): void { void this.leer(); }

  protected cambiar(anio: boolean): void { this.anio.set(anio); void this.leer(); }

  private async leer(): Promise<void> {
    try { this.datos.set(await firstValueFrom(this.http.get<Respuesta>('/api/gastos/lugares' + (this.anio() ? '?mes=0' : '')))); } catch { this.datos.set(null); }
  }

  // un mapa esquemático: las zonas se reparten por su latitud y longitud dentro del recuadro (la longitud se acorta con el coseno de la latitud)
  private caja = computed(() => {
    const z = this.datos()?.zonas ?? [];
    const lat = z.map((q) => q.lat), lon = z.map((q) => q.lon);
    const cos = Math.cos(((Math.min(...lat) + Math.max(...lat)) / 2) * Math.PI / 180) || 1;
    const ancho = Math.max((Math.max(...lon) - Math.min(...lon)) * cos, 0.02), alto = Math.max(Math.max(...lat) - Math.min(...lat), 0.02);
    const escala = Math.min(240 / ancho, 120 / alto);
    return { lat0: (Math.min(...lat) + Math.max(...lat)) / 2, lon0: (Math.min(...lon) + Math.max(...lon)) / 2, cos, escala };
  });
  protected x(z: Zona): number { const c = this.caja(); return 150 + (z.lon - c.lon0) * c.cos * c.escala; }
  protected y(z: Zona): number { const c = this.caja(); return 85 - (z.lat - c.lat0) * c.escala; }
  protected radio(z: Zona): number { return 6 + 20 * Math.sqrt(z.total / this.maximo()); }
}
