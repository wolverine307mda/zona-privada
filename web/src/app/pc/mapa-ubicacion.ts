import { ChangeDetectionStrategy, Component, ElementRef, OnDestroy, ViewEncapsulation, effect, input, output, untracked, viewChild } from '@angular/core';
import type * as Leaflet from 'leaflet';
import { Punto } from './ubicacion-logica';

// El mapa de «Ubicación del iPhone»: tocas un sitio y queda elegido (pin del color de la paleta); el punto pulsante es donde está el iPhone ahora.
// Las teselas son de OpenStreetMap (con su atribución). OpenStreetMap rechaza las teselas sin Referer y la app manda «no-referrer»: solo estas peticiones
// llevan el origen (`referrerPolicy: 'origin'`). Leaflet pesa unos 150 kB y se baja solo al abrir esta pestaña.
@Component({
  selector: 'app-mapa-ubicacion',
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,   // los estilos de Leaflet (y los de sus marcadores) tienen que llegar a lo que él crea
  styleUrl: './mapa-ubicacion.css',
  template: `<div class="ubic-mapa" #mapa role="application" aria-label="Mapa: toca un sitio para elegirlo"></div>`,
})
export class MapaUbicacion implements OnDestroy {
  readonly seleccion = input<Punto | null>(null);
  readonly iphone = input<Punto | null>(null);
  readonly elegido = output<Punto>();

  private readonly contenedor = viewChild<ElementRef<HTMLDivElement>>('mapa');
  private L?: typeof Leaflet;
  private mapa?: Leaflet.Map;
  private pin?: Leaflet.Marker;
  private yo?: Leaflet.Marker;
  private observador?: ResizeObserver;
  private centrado = false;
  private ultimoClic: Punto | null = null;

  constructor() {
    effect(() => {
      const el = this.contenedor()?.nativeElement;
      if (el) untracked(() => void this.crear(el));
    });
    effect(() => {
      const s = this.seleccion(), a = this.iphone();
      untracked(() => this.marcar(s, a));
    });
  }

  private async crear(el: HTMLDivElement): Promise<void> {
    this.L ??= await import('leaflet');
    if (this.mapa) return;
    const L = this.L;
    const inicio = this.iphone() ?? this.seleccion();
    this.mapa = L.map(el, { worldCopyJump: true, zoomControl: true, attributionControl: true }).setView(inicio ? [inicio.lat, inicio.lon] : [40.4, -3.7], inicio ? 13 : 4);
    L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', { maxZoom: 18, referrerPolicy: 'origin', attribution: '© <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noopener">OpenStreetMap</a>' }).addTo(this.mapa);
    this.mapa.on('click', (e: Leaflet.LeafletMouseEvent) => {
      const lon = ((((e.latlng.lng + 180) % 360) + 360) % 360) - 180;   // el mapa se repite: se vuelve al rango −180…180
      this.ultimoClic = { lat: Math.round(e.latlng.lat * 1e5) / 1e5, lon: Math.round(lon * 1e5) / 1e5 };
      this.elegido.emit(this.ultimoClic);
    });
    this.observador = new ResizeObserver(() => this.mapa?.invalidateSize());
    this.observador.observe(el);
    this.centrado = !!inicio;
    this.marcar(this.seleccion(), this.iphone());
  }

  private marcar(s: Punto | null, a: Punto | null): void {
    const L = this.L, mapa = this.mapa;
    if (!L || !mapa) return;
    if (s) {
      if (this.pin) this.pin.setLatLng([s.lat, s.lon]);
      else this.pin = L.marker([s.lat, s.lon], { icon: L.divIcon({ className: 'ubic-pin', html: '<i></i>', iconSize: [26, 26], iconAnchor: [13, 26] }), keyboard: false, title: 'Sitio elegido' }).addTo(mapa);
      // un sitio elegido desde la lista o pegado lleva el mapa hasta allí; uno tocado en el mapa no lo mueve
      const clic = this.ultimoClic;
      if (!(clic && clic.lat === s.lat && clic.lon === s.lon)) mapa.flyTo([s.lat, s.lon], Math.max(mapa.getZoom(), 12), { duration: 0.8 });
    } else if (this.pin) { this.pin.remove(); this.pin = undefined; }
    if (a) {
      if (this.yo) this.yo.setLatLng([a.lat, a.lon]);
      else {
        this.yo = L.marker([a.lat, a.lon], { icon: L.divIcon({ className: 'ubic-yo', html: '<i></i>', iconSize: [22, 22], iconAnchor: [11, 11] }), keyboard: false, interactive: false, title: 'Dónde está el iPhone' }).addTo(mapa);
        if (!s && !this.centrado) { mapa.setView([a.lat, a.lon], 13); this.centrado = true; }
      }
    } else if (this.yo) { this.yo.remove(); this.yo = undefined; }
  }

  ngOnDestroy(): void {
    this.observador?.disconnect();
    this.mapa?.remove();
  }
}
