import { ChangeDetectionStrategy, Component, ElementRef, OnDestroy, ViewEncapsulation, effect, inject, input, untracked, viewChild } from '@angular/core';
import type * as Leaflet from 'leaflet';
import { resolverColor } from '../core/color-css';
import { TemaService } from '../core/tema.service';
import { euros } from './modelo';

export interface ComercioMapa { nombre: string; total: number; pagos: number; }
export interface ZonaMapa { lat: number; lon: number; total: number; pagos: number; comercios: ComercioMapa[]; }

// El mapa de lo que gastas: un círculo por zona de ~1 km, tanto más grande cuanto más se gasta, sobre OpenStreetMap. Al tocarlo, los comercios que más pesan.
// Está en su propio componente (sin encapsulado de estilos) porque Leaflet crea elementos que los estilos de un componente normal no alcanzan.
// OpenStreetMap rechaza las teselas sin Referer y la app manda «no-referrer»: solo estas peticiones llevan el origen (`referrerPolicy: 'origin'`).
// Leaflet pesa unos 150 kB: se baja solo al abrir el mapa.
@Component({
  selector: 'app-mapa-gasto',
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  styleUrl: './mapa-gasto.css',
  template: `<div class="mapa-gasto" #mapa role="application" aria-label="Mapa de lo que gastas, con un círculo por zona"></div>`,
})
export class MapaGasto implements OnDestroy {
  readonly zonas = input.required<ZonaMapa[]>();
  private readonly tema = inject(TemaService);
  private readonly contenedor = viewChild.required<ElementRef<HTMLDivElement>>('mapa');
  private L?: typeof Leaflet;
  private mapa?: Leaflet.Map;
  private capa?: Leaflet.LayerGroup;
  private observador?: ResizeObserver;

  constructor() {
    effect(() => {
      const zonas = this.zonas(), el = this.contenedor().nativeElement;
      this.tema.cambio(); // otro color de acento: se vuelven a pintar los círculos
      untracked(() => void this.pintar(el, zonas));
    });
  }

  private async pintar(el: HTMLDivElement, zonas: ZonaMapa[]): Promise<void> {
    if (!this.L) this.L = await import('leaflet');
    const L = this.L;
    if (!this.mapa) {
      this.mapa = L.map(el, { worldCopyJump: true, zoomControl: true, attributionControl: true }).setView([40.4, -3.7], 5);
      L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
        maxZoom: 18, referrerPolicy: 'origin',
        attribution: '© <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noopener">OpenStreetMap</a>',
      }).addTo(this.mapa);
      this.observador = new ResizeObserver(() => this.mapa?.invalidateSize());
      this.observador.observe(el);
    }
    if (this.capa) this.mapa.removeLayer(this.capa);
    this.capa = L.layerGroup().addTo(this.mapa);
    const color = resolverColor(getComputedStyle(document.documentElement).getPropertyValue('--brand'), '#ff7a1f');
    const maximo = Math.max(1, ...zonas.map((z) => z.total));
    for (const z of zonas) {
      const radio = 9 + 24 * Math.sqrt(z.total / maximo);
      L.circleMarker([z.lat, z.lon], { radius: radio, color, weight: 2, fillColor: color, fillOpacity: 0.32 }).bindPopup(this.ventana(z), { maxWidth: 260 }).addTo(this.capa);
    }
    if (zonas.length) this.mapa.fitBounds(L.latLngBounds(zonas.map((z) => [z.lat, z.lon] as [number, number])), { padding: [40, 40], maxZoom: 13 });
  }

  // la ventana de una zona, hecha con elementos (los nombres de los comercios vienen de lo que apuntas: nunca se pegan como HTML)
  private ventana(z: ZonaMapa): HTMLElement {
    const div = document.createElement('div');
    div.className = 'mapa-gasto__ventana';
    const t = document.createElement('strong');
    t.textContent = `${euros(z.total)} · ${z.pagos} ${z.pagos === 1 ? 'pago' : 'pagos'}`;
    div.appendChild(t);
    const lista = document.createElement('ul');
    for (const c of z.comercios) {
      const li = document.createElement('li');
      li.textContent = `${c.nombre}: ${euros(c.total)}${c.pagos > 1 ? ` (${c.pagos})` : ''}`;
      lista.appendChild(li);
    }
    div.appendChild(lista);
    return div;
  }

  ngOnDestroy(): void {
    this.observador?.disconnect();
    this.mapa?.remove();
  }
}
