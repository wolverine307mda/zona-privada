import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, ElementRef, OnDestroy, ViewEncapsulation, inject, signal, viewChild } from '@angular/core';
import type * as Leaflet from 'leaflet';
import { firstValueFrom } from 'rxjs';
import { cargarLeaflet } from '../core/leaflet';

interface Punto { tipo: 'equipo' | 'atajo'; nombre: string; lat: number; lon: number; pais: string; ciudad: string; confianza: boolean; sospechoso: boolean; visto: string | null; }
interface Respuesta { disponible: boolean; aproximada: boolean; puntos: Punto[]; }

// Dónde están tus equipos y las redes desde las que se usan tus atajos, según la IP (ubicación aproximada: con una VPN o en el móvil puede salir lejos).
// Verde = de confianza, naranja = equipo conocido, rojo = marcado «no lo reconozco», azul = red de un atajo. Detrás del PIN de Seguridad.
@Component({
  selector: 'app-mapa-equipos',
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  styleUrl: '../gastos/mapa-gasto.css',
  template: `
    <section class="caja">
      <header class="caja__cab"><h3>Mapa de accesos</h3><span class="sub">ubicación aproximada, por IP</span></header>
      @if (!abierto()) {
        <button class="button button--ghost" type="button" (click)="abrir()">Ver el mapa</button>
      } @else if (fallo()) { <p class="sub">No se pudo cargar el mapa.</p> }
      @else if (datos() && !datos()!.disponible) { <p class="sub">Aún no hay base de geolocalización: se baja sola cada mes (Seguridad › Vigilancia).</p> }
      @else if (datos() && datos()!.puntos.length === 0) { <p class="sub">Ningún equipo ni atajo tiene una ubicación que pintar todavía.</p> }
      @if (abierto() && datos()?.puntos?.length) {
        <div class="mapa-gasto" #mapa role="application" aria-label="Mapa de equipos y redes de los atajos"></div>
        <p class="leyenda"><i class="p p--ok"></i> de confianza <i class="p p--eq"></i> conocido <i class="p p--mal"></i> no lo reconozco <i class="p p--atajo"></i> red de un atajo</p>
      }
    </section>
  `,
  styles: `
    .leyenda { display: flex; flex-wrap: wrap; gap: 6px 14px; align-items: center; color: var(--soft); font-size: .78rem; margin: 6px 0 0; }
    .leyenda .p { display: inline-block; width: 10px; height: 10px; border-radius: 50%; margin-right: -6px; }
    .p--ok { background: #2fbf71; } .p--eq { background: #ff7a1f; } .p--mal { background: #ff4d4d; } .p--atajo { background: #4aa3ff; }
  `,
})
export class MapaEquipos implements OnDestroy {
  private readonly http = inject(HttpClient);
  private readonly contenedor = viewChild<ElementRef<HTMLDivElement>>('mapa');
  protected readonly abierto = signal(false);
  protected readonly datos = signal<Respuesta | null>(null);
  protected readonly fallo = signal(false);
  private mapa?: Leaflet.Map;

  protected async abrir(): Promise<void> {
    this.abierto.set(true); this.fallo.set(false);
    try { this.datos.set(await firstValueFrom(this.http.get<Respuesta>('/api/seguridad/equipos/mapa'))); } catch { this.fallo.set(true); return; }
    // el contenedor aparece con el siguiente ciclo de pintado
    setTimeout(() => void this.pintar());
  }

  private color(p: Punto): string { return p.tipo === 'atajo' ? '#4aa3ff' : p.sospechoso ? '#ff4d4d' : p.confianza ? '#2fbf71' : '#ff7a1f'; }

  private async pintar(): Promise<void> {
    const el = this.contenedor()?.nativeElement, d = this.datos();
    if (!el || !d?.puntos.length) return;
    const L = await cargarLeaflet();
    this.mapa?.remove();
    this.mapa = L.map(el, { worldCopyJump: true }).setView([40.4, -3.7], 3);
    L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
      maxZoom: 18, referrerPolicy: 'origin',
      attribution: '© <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noopener">OpenStreetMap</a>',
    }).addTo(this.mapa);
    for (const p of d.puntos) {
      const c = this.color(p);
      const ventana = document.createElement('div');
      ventana.className = 'mapa-gasto__ventana';
      const t = document.createElement('strong'); t.textContent = p.nombre; ventana.appendChild(t);   // textContent: los nombres vienen de la red, nunca como HTML
      const s = document.createElement('span');
      s.textContent = `${p.tipo === 'atajo' ? 'Atajo' : 'Equipo'} · ${p.ciudad ? p.ciudad + ', ' : ''}${p.pais}${p.sospechoso ? ' · NO RECONOCIDO' : ''}`;
      ventana.appendChild(s);
      L.circleMarker([p.lat, p.lon], { radius: 9, color: c, weight: 2, fillColor: c, fillOpacity: 0.45 }).bindPopup(ventana).addTo(this.mapa);
    }
    this.mapa.fitBounds(L.latLngBounds(d.puntos.map((p) => [p.lat, p.lon] as [number, number])), { padding: [40, 40], maxZoom: 10 });
  }

  ngOnDestroy(): void { this.mapa?.remove(); }
}
