import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, ElementRef, OnDestroy, ViewEncapsulation, computed, effect, inject, signal, untracked, viewChild } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type * as Leaflet from 'leaflet';
import { TemaService } from '../core/tema.service';
import { EstadoError, EstadoVacio, Esqueleto } from '../ui';

interface PuntoMapa { r: string; v: string; lat: number; lon: number; f: string | null; }
interface RespuestaMapa { total: number; conGps: number; pendientes: number; indexado: boolean; puntos: PuntoMapa[]; }

// El mapa de tus fotos: todas las de la carpeta «Imágenes» que llevan GPS, agrupadas según el zoom. Es solo para ti (lleva el PIN): ni las posiciones ni las
// fechas salen nunca hacia la web. Las teselas son de OpenStreetMap (con su atribución, y sin descargas en bloque, como pide su política de uso).
// OpenStreetMap rechaza las teselas sin Referer y la app manda «no-referrer»: solo estas peticiones llevan el origen (`referrerPolicy: 'origin'`).
// Leaflet pesa unos 150 kB: se baja solo al abrir esta pestaña.
@Component({
  selector: 'app-mapa-fotos',
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,   // los estilos de Leaflet (y los de sus marcadores) tienen que llegar a lo que él crea
  imports: [EstadoError, EstadoVacio, Esqueleto],
  styleUrl: './mapa-fotos.css',
  template: `
    <section class="caja mapa-fotos">
      <header class="caja__cab"><h3>Mapa de fotos</h3><span class="sub">{{ resumen() }}</span></header>
      @if (fallo()) { <app-estado-error titulo="No pude cargar el mapa" mensaje="Puede que haga falta el PIN o que el equipo no responda." (reintentar)="cargar()" /> }
      @else if (datos() === null) { <app-esqueleto forma="bloque" alto="320px" /> }
      @else if (datos()!.conGps === 0) {
        <app-estado-vacio icono="M12 21s-7-6.2-7-11a7 7 0 0 1 14 0c0 4.8-7 11-7 11zM12 12a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5z" titulo="Aún no hay fotos con ubicación" [texto]="datos()!.indexado ? 'Ninguna foto de «Imágenes» lleva GPS en su archivo.' : 'Las fotos se están revisando para saber dónde se hicieron; vuelve en unos minutos.'" />
      } @else {
        @if (anios().length > 1) {
          <label class="mapa-fotos__filtro">Año
            <select (change)="filtrar($any($event.target).value)">
              <option value="">Todos</option>
              @for (a of anios(); track a) { <option [value]="a">{{ a }}</option> }
            </select>
          </label>
        }
        <div class="mapa-fotos__mapa" #mapa role="application" aria-label="Mapa con tus fotos"></div>
        @if (seleccion().length) {
          <div class="mapa-fotos__tira" aria-live="polite">
            @for (p of seleccion(); track p.r) {
              <a [href]="grande(p)" target="_blank" rel="noopener" [attr.aria-label]="'Abrir ' + nombre(p.r) + (p.f ? ', ' + cuando(p.f) : '')">
                <img [src]="mini(p)" alt="" loading="lazy" width="88" height="88">
                <small>{{ p.f ? cuando(p.f) : nombre(p.r) }}</small>
              </a>
            }
          </div>
        }
        @if (datos()!.pendientes > 0) { <p class="nada">Revisando más fotos… faltan {{ datos()!.pendientes }}.</p> }
      }
    </section>
  `,
})
export class MapaFotos implements OnDestroy {
  private readonly http = inject(HttpClient);
  private readonly tema = inject(TemaService);
  private readonly contenedor = viewChild<ElementRef<HTMLDivElement>>('mapa');

  protected readonly datos = signal<RespuestaMapa | null>(null);
  protected readonly fallo = signal(false);
  protected readonly anio = signal('');
  protected readonly seleccion = signal<PuntoMapa[]>([]);
  protected readonly anios = computed(() => [...new Set((this.datos()?.puntos ?? []).filter((p) => p.f).map((p) => p.f!.slice(0, 4)))].sort().reverse());
  protected readonly resumen = computed(() => {
    const d = this.datos();
    return d ? `${d.conGps} de ${d.total} fotos con ubicación` : 'cargando…';
  });
  private readonly visibles = computed(() => (this.datos()?.puntos ?? []).filter((p) => !this.anio() || p.f?.startsWith(this.anio())));

  private L?: typeof Leaflet;
  private mapa?: Leaflet.Map;
  private grupo?: Leaflet.MarkerClusterGroup;
  private observador?: ResizeObserver;

  constructor() {
    void this.cargar();
    effect(() => {
      const el = this.contenedor()?.nativeElement, pts = this.visibles();
      this.tema.cambio();
      if (el && this.datos()?.conGps) untracked(() => void this.pintar(el, pts));
    });
  }

  protected async cargar(): Promise<void> {
    this.fallo.set(false);
    try { this.datos.set(await firstValueFrom(this.http.get<RespuestaMapa>('/api/galeria/mapa'))); } catch { this.fallo.set(true); }
  }

  protected filtrar(a: string): void { this.anio.set(a); this.seleccion.set([]); }

  private async pintar(el: HTMLDivElement, pts: PuntoMapa[]): Promise<void> {
    if (!this.L) {
      const leaflet = await import('leaflet');
      // el plugin de agrupar espera encontrar L en el navegador
      (window as unknown as { L: unknown }).L = leaflet;
      await import('leaflet.markercluster');
      this.L = leaflet;
    }
    const L = this.L;
    if (!this.mapa) {
      this.mapa = L.map(el, { worldCopyJump: true, zoomControl: true, attributionControl: true }).setView([40.4, -3.7], 5);
      L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', { maxZoom: 18, referrerPolicy: 'origin', attribution: '© <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noopener">OpenStreetMap</a>' }).addTo(this.mapa);
      this.observador = new ResizeObserver(() => this.mapa?.invalidateSize());
      this.observador.observe(el);
    }
    if (this.grupo) this.mapa.removeLayer(this.grupo);
    this.grupo = L.markerClusterGroup({ showCoverageOnHover: false, maxClusterRadius: 50, spiderfyOnMaxZoom: true });
    const icono = L.divIcon({ className: 'mapa-fotos__pin', html: '<i></i>', iconSize: [18, 18], iconAnchor: [9, 9] });
    for (const p of pts) {
      L.marker([p.lat, p.lon], { icon: icono, title: p.f ? this.cuando(p.f) : this.nombre(p.r) })
        .on('click', () => this.seleccion.set(this.cercanas(p, pts)))
        .addTo(this.grupo);
    }
    this.mapa.addLayer(this.grupo);
    if (pts.length) this.mapa.fitBounds(L.latLngBounds(pts.map((p) => [p.lat, p.lon] as [number, number])), { padding: [30, 30], maxZoom: 14 });
  }

  // las fotos hechas casi en el mismo sitio (a unos 100 m) que la tocada, con un tope para la tira
  private cercanas(p: PuntoMapa, todas: PuntoMapa[]): PuntoMapa[] {
    return todas.filter((q) => Math.abs(q.lat - p.lat) < 0.001 && Math.abs(q.lon - p.lon) < 0.0015).slice(0, 12);
  }

  protected mini(p: PuntoMapa): string { return `/api/galeria/carpeta/mini?ruta=${encodeURIComponent(p.r)}&v=${p.v}`; }
  protected grande(p: PuntoMapa): string { return `/api/galeria/carpeta/mini?ruta=${encodeURIComponent(p.r)}&tam=media&v=${p.v}`; }
  protected nombre(ruta: string): string { return ruta.split('/').pop() ?? ruta; }
  protected cuando(iso: string): string { return new Date(iso).toLocaleDateString('es-ES', { day: 'numeric', month: 'short', year: 'numeric' }); }

  ngOnDestroy(): void {
    this.observador?.disconnect();
    this.mapa?.remove();
  }
}
