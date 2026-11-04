import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, output, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface PuntoFoto { id: number; nombre: string; lat: number; lon: number; orientacion: string; notas: string; }
interface Hoy { punto: PuntoFoto; mejor: { titulo: string; puntuacion: number; inicio: string } | null; }

const ORIENTACIONES = ['', 'N', 'NE', 'E', 'SE', 'S', 'SO', 'O', 'NO'];

// Tus sitios de fotografía: un mapa sencillo con todos, cuál toca hoy y cuál mañana, y poder ver el cielo de cualquiera de ellos.
@Component({
  selector: 'app-puntos',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrls: ['../gastos/comun.css'],
  template: `
    <section class="caja">
      <header class="caja__cab"><h3>Mis sitios</h3><span class="sub">puntos favoritos para fotografiar</span></header>
      @if (error()) { <p class="error" role="alert">{{ error() }}</p> }

      @if (lista().length) {
        @if (toca(); as t) { <p class="toca">📍 Hoy toca <b>{{ t.punto.nombre }}</b> — {{ t.mejor!.titulo }}, nota {{ t.mejor!.puntuacion }}/10.</p> }
        <svg class="mapa" viewBox="0 0 300 150" role="img" aria-label="Mapa esquemático de tus sitios">
          <rect x="1" y="1" width="298" height="148" rx="14" class="mapa__fondo" />
          @for (p of lista(); track p.id) {
            <g class="pin" [class.pin--sel]="p.id === elegido()" [class.pin--toca]="p.id === toca()?.punto?.id" (click)="elegir(p.id)" (keydown.enter)="elegir(p.id)" tabindex="0" role="button" [attr.aria-label]="p.nombre">
              <circle [attr.cx]="x(p)" [attr.cy]="y(p)" r="7" />
              <text [attr.x]="x(p)" [attr.y]="y(p) - 11" text-anchor="middle">{{ p.nombre }}</text>
            </g>
          }
        </svg>
        <ul class="sitios">
          @for (h of hoy(); track h.punto.id) {
            <li class="sitio" [class.sitio--sel]="h.punto.id === elegido()">
              <button type="button" class="sitio__ver" (click)="elegir(h.punto.id)" [attr.aria-pressed]="h.punto.id === elegido()">
                <strong>{{ h.punto.nombre }}</strong>
                <small>{{ h.punto.orientacion ? 'mira al ' + h.punto.orientacion + ' · ' : '' }}{{ h.mejor ? h.mejor.titulo + ': ' + h.mejor.puntuacion + '/10' : 'sin previsión' }}</small>
                @if (h.punto.notas) { <small>{{ h.punto.notas }}</small> }
              </button>
              <button type="button" class="enlace" (click)="borrar(h.punto)" [attr.aria-label]="'Borrar ' + h.punto.nombre">Borrar</button>
            </li>
          }
        </ul>
        @if (elegido() !== null) { <button type="button" class="enlace" (click)="elegir(null)">Volver a mi lugar de siempre</button> }
      } @else { <p class="nada">Guarda aquí los sitios donde sueles fotografiar y te digo cuál toca cada día.</p> }

      <details class="nuevo">
        <summary>Añadir un sitio</summary>
        <div class="rejilla">
          <label>Nombre<input type="text" maxlength="60" [value]="nombre()" (input)="nombre.set($any($event.target).value)"></label>
          <label>Latitud<input type="number" inputmode="decimal" step="0.00001" [value]="lat()" (input)="lat.set($any($event.target).value)"></label>
          <label>Longitud<input type="number" inputmode="decimal" step="0.00001" [value]="lon()" (input)="lon.set($any($event.target).value)"></label>
          <label>Miras hacia<select (change)="orientacion.set($any($event.target).value)">@for (o of orientaciones; track o) { <option [value]="o">{{ o || '—' }}</option> }</select></label>
          <label class="ancho">Notas<input type="text" maxlength="500" [value]="notas()" (input)="notas.set($any($event.target).value)"></label>
        </div>
        <div class="acciones">
          <button type="button" class="enlace" (click)="miUbicacion()">Usar mi ubicación</button>
          <button type="button" class="boton" (click)="guardar()" [disabled]="!valido()">Guardar sitio</button>
        </div>
      </details>
    </section>
  `,
  styles: `
    .toca { margin: 0 0 10px; }
    .mapa { width: 100%; height: auto; max-height: 220px; display: block; margin-bottom: 10px; }
    .mapa__fondo { fill: color-mix(in srgb, var(--texto) 5%, transparent); stroke: var(--vidrio-borde); }
    .pin { cursor: pointer; outline: none; }
    .pin circle { fill: color-mix(in srgb, var(--texto) 45%, transparent); stroke: var(--texto); stroke-width: 1.5; transition: fill 200ms; }
    .pin--toca circle { fill: #3fb96a; }
    .pin--sel circle, .pin:focus-visible circle { fill: var(--brand); stroke-width: 3; }
    .pin text { fill: var(--texto); font-size: 9px; paint-order: stroke; stroke: var(--bg, #000); stroke-width: 2px; }
    .sitios { list-style: none; margin: 0 0 8px; padding: 0; display: grid; gap: 6px; }
    .sitio { display: flex; align-items: center; gap: 8px; border-radius: 14px; border: 1px solid var(--vidrio-borde); padding: 4px 10px 4px 4px; }
    .sitio--sel { border-color: var(--brand); background: color-mix(in srgb, var(--brand) 8%, transparent); }
    .sitio__ver { flex: 1; min-width: 0; display: grid; gap: 2px; text-align: left; background: none; border: 0; color: inherit; font: inherit; padding: 8px 10px; min-height: 44px; cursor: pointer; }
    .sitio__ver small { color: var(--soft); font-size: .78rem; }
    .nuevo { margin-top: 10px; }
    .nuevo summary { cursor: pointer; min-height: 36px; color: var(--soft); }
    .rejilla { display: grid; grid-template-columns: repeat(auto-fit, minmax(140px, 1fr)); gap: 10px; margin-top: 8px; }
    .ancho { grid-column: 1 / -1; }
    label { display: grid; gap: 4px; font-size: .78rem; color: var(--soft); }
    input, select { min-height: 40px; border-radius: 12px; border: 1px solid var(--vidrio-borde); background: color-mix(in srgb, var(--texto) 5%, transparent); color: var(--texto); padding: 0 12px; font: 500 .95rem/1 var(--font-text); }
    .acciones { display: flex; justify-content: space-between; align-items: center; gap: 10px; margin-top: 10px; flex-wrap: wrap; }
  `,
})
export class Puntos implements OnInit {
  private readonly http = inject(HttpClient);
  readonly cambiar = output<number | null>();
  protected readonly orientaciones = ORIENTACIONES;
  protected readonly hoy = signal<Hoy[]>([]);
  protected readonly lista = computed(() => this.hoy().map((h) => h.punto));
  protected readonly elegido = signal<number | null>(null);
  protected readonly error = signal('');
  protected readonly nombre = signal('');
  protected readonly lat = signal('');
  protected readonly lon = signal('');
  protected readonly orientacion = signal('');
  protected readonly notas = signal('');
  protected readonly valido = computed(() => this.nombre().trim().length > 0 && this.lat() !== '' && this.lon() !== '');
  // el punto con la mejor nota de hoy y mañana (si pasa de 5)
  protected readonly toca = computed(() => this.hoy().filter((h) => h.mejor && h.mejor.puntuacion >= 6).sort((a, b) => b.mejor!.puntuacion - a.mejor!.puntuacion)[0] ?? null);

  ngOnInit(): void { void this.leer(); }

  private async leer(): Promise<void> {
    try { this.hoy.set(await firstValueFrom(this.http.get<Hoy[]>('/api/luz/puntos/hoy'))); } catch { /* sin puntos todavía */ }
  }

  // un mapa esquemático: los puntos se reparten por su latitud y longitud dentro del recuadro, con margen
  private rango(f: (p: PuntoFoto) => number): [number, number] {
    const v = this.lista().map(f);
    const a = Math.min(...v), b = Math.max(...v);
    return b - a < 1e-6 ? [a - 0.5, b + 0.5] : [a, b];
  }
  protected x(p: PuntoFoto): number { const [a, b] = this.rango((q) => q.lon); return 30 + ((p.lon - a) / (b - a)) * 240; }
  protected y(p: PuntoFoto): number { const [a, b] = this.rango((q) => q.lat); return 125 - ((p.lat - a) / (b - a)) * 90; }

  protected elegir(id: number | null): void { this.elegido.set(id); this.cambiar.emit(id); }

  protected miUbicacion(): void {
    navigator.geolocation?.getCurrentPosition(
      (p) => { this.lat.set(p.coords.latitude.toFixed(5)); this.lon.set(p.coords.longitude.toFixed(5)); },
      () => this.error.set('No pude leer tu ubicación: escribe las coordenadas a mano.'),
      { timeout: 8000 },
    );
  }

  protected async guardar(): Promise<void> {
    this.error.set('');
    try {
      await firstValueFrom(this.http.post('/api/luz/puntos', { nombre: this.nombre(), lat: Number(this.lat()), lon: Number(this.lon()), orientacion: this.orientacion(), notas: this.notas() }));
      this.nombre.set(''); this.lat.set(''); this.lon.set(''); this.orientacion.set(''); this.notas.set('');
      await this.leer();
    } catch (e) { this.error.set(this.mensaje(e)); }
  }

  protected async borrar(p: PuntoFoto): Promise<void> {
    if (!confirm(`¿Borrar «${p.nombre}»?`)) return;
    try { await firstValueFrom(this.http.delete(`/api/luz/puntos/${p.id}`)); if (this.elegido() === p.id) this.elegir(null); await this.leer(); }
    catch (e) { this.error.set(this.mensaje(e)); }
  }

  private mensaje(e: unknown): string {
    const err = (e as HttpErrorResponse).error as { errors?: Record<string, string[]> } | undefined;
    return Object.values(err?.errors ?? {})[0]?.[0] ?? 'No se pudo.';
  }
}
