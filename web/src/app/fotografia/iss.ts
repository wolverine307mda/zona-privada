import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { EstadoError, Esqueleto } from '../ui';

interface Pasada { inicio: string; maximo: string; fin: string; alturaMax: number; azInicio: number; azMax: number; azFin: number; desdeRumbo: string; hastaRumbo: string; nubes: number | null; }
interface RespuestaIss { sinLugar?: boolean; sinDatos?: boolean; lugar?: string; tleEdadDias?: number | null; pasadas?: Pasada[]; }

// Cuándo se ve pasar la Estación Espacial Internacional desde tu lugar: una estrella muy brillante y que no parpadea que cruza el cielo en unos minutos.
// Cada pasada lleva su mapa del cielo (el centro es lo que tienes encima; el borde, el horizonte; arriba, el norte).
@Component({
  selector: 'app-iss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Esqueleto, EstadoError],
  styleUrls: ['../gastos/comun.css'],
  template: `
    @if (!sinLugar()) {
      <section class="caja">
        <header class="caja__cab"><h3>La Estación Espacial</h3><span class="sub">pasadas visibles en los próximos 10 días</span></header>
        @if (fallo()) { <app-estado-error titulo="No pude calcular las pasadas" (reintentar)="cargar()" /> }
        @else if (datos() === null) { <app-esqueleto forma="bloque" alto="84px" /> }
        @else if (datos()!.sinDatos) { <p class="nada">Sin los datos orbitales de CelesTrak ahora mismo: se reintenta solo.</p> }
        @else {
          <ul class="pasadas">
            @for (p of pasadas(); track p.inicio) {
              <li>
                <svg class="cielo" viewBox="-50 -50 100 100" role="img" [attr.aria-label]="'Sale por el ' + p.desdeRumbo + ', sube hasta ' + p.alturaMax + ' grados y se esconde por el ' + p.hastaRumbo">
                  <circle r="46" class="borde" /><circle r="30.7" class="anillo" /><circle r="15.3" class="anillo" />
                  <text y="-39" class="n">N</text>
                  <path [attr.d]="trazo(p)" class="ruta" />
                  <circle [attr.cx]="x(p.azInicio, 10)" [attr.cy]="y(p.azInicio, 10)" r="2.2" class="ini" />
                </svg>
                <div class="datos">
                  <strong>{{ dia(p.inicio) }} · {{ hora(p.inicio) }}</strong>
                  <small>Sale por el {{ p.desdeRumbo }}, sube hasta {{ p.alturaMax }}° y se esconde por el {{ p.hastaRumbo }} · dura {{ minutos(p) }}</small>
                  <small class="calidad" [class.buena]="p.alturaMax >= 40">{{ p.alturaMax >= 60 ? 'Muy buena' : p.alturaMax >= 40 ? 'Buena' : 'Baja sobre el horizonte' }}{{ p.nubes !== null ? ' · nubes ' + p.nubes + ' %' : '' }}</small>
                </div>
                <span class="faltan">{{ faltan(p) }}</span>
              </li>
            } @empty { <p class="nada">No hay pasadas visibles en los próximos 10 días desde {{ datos()!.lugar }}. Suelen venir por rachas de unas semanas.</p> }
          </ul>
          @if (datos()!.tleEdadDias != null && datos()!.tleEdadDias! > 14) { <p class="nada">Los datos orbitales tienen {{ entero(datos()!.tleEdadDias!) }} días: la hora puede variar unos minutos.</p> }
        }
      </section>
    }
  `,
  styles: `
    .pasadas { list-style: none; margin: 0; padding: 0; display: grid; gap: 8px; }
    .pasadas li { display: grid; grid-template-columns: 64px minmax(0, 1fr) auto; gap: 12px; align-items: center; padding: 10px 12px; border-radius: 14px; border: 1px solid var(--vidrio-borde); background: color-mix(in srgb, var(--texto) 5%, transparent); }
    .cielo { width: 64px; height: 64px; }
    .borde { fill: color-mix(in srgb, var(--brand) 8%, transparent); stroke: var(--linea); stroke-width: 1.5; }
    .anillo { fill: none; stroke: var(--linea); stroke-width: .8; stroke-dasharray: 2 3; }
    .n { fill: var(--soft); font: 600 9px var(--font-code); text-anchor: middle; }
    .ruta { fill: none; stroke: var(--brand); stroke-width: 2.4; stroke-linecap: round; }
    .ini { fill: var(--brand); }
    .datos { display: grid; gap: 2px; min-width: 0; }
    .datos small { color: var(--soft); font-size: .78rem; }
    .calidad.buena { color: var(--ok); }
    .faltan { font: 600 .78rem/1 var(--font-code); color: var(--acento); white-space: nowrap; }
  `,
})
export class Iss implements OnInit {
  private readonly http = inject(HttpClient);
  protected readonly datos = signal<RespuestaIss | null>(null);
  protected readonly fallo = signal(false);
  protected readonly sinLugar = signal(false);
  protected readonly pasadas = () => (this.datos()?.pasadas ?? []).slice(0, 4);

  ngOnInit(): void { void this.cargar(); }

  protected async cargar(): Promise<void> {
    this.fallo.set(false);
    try {
      const r = await firstValueFrom(this.http.get<RespuestaIss>('/api/luz/iss'));
      this.sinLugar.set(!!r.sinLugar);
      this.datos.set(r);
    } catch { this.fallo.set(true); }
  }

  // un punto del cielo en el mapa (radio 46 = horizonte; el centro = lo que está justo encima); el azimut sube en el sentido de las agujas del reloj desde el norte
  protected x(az: number, alt: number): number { return Math.sin((az * Math.PI) / 180) * 46 * (90 - alt) / 90; }
  protected y(az: number, alt: number): number { return -Math.cos((az * Math.PI) / 180) * 46 * (90 - alt) / 90; }
  protected trazo(p: Pasada): string {
    return `M ${this.x(p.azInicio, 10).toFixed(1)} ${this.y(p.azInicio, 10).toFixed(1)} Q ${this.x(p.azMax, p.alturaMax * 1.35 > 90 ? 90 : p.alturaMax * 1.35).toFixed(1)} ${this.y(p.azMax, p.alturaMax * 1.35 > 90 ? 90 : p.alturaMax * 1.35).toFixed(1)} ${this.x(p.azFin, 10).toFixed(1)} ${this.y(p.azFin, 10).toFixed(1)}`;
  }

  protected entero(n: number): number { return Math.round(n); }
  protected dia(iso: string): string { return new Date(iso).toLocaleDateString('es-ES', { weekday: 'short', day: 'numeric', month: 'short' }); }
  protected hora(iso: string): string { return new Date(iso).toLocaleTimeString('es-ES', { hour: '2-digit', minute: '2-digit' }); }
  protected minutos(p: Pasada): string { const m = Math.max(1, Math.round((new Date(p.fin).getTime() - new Date(p.inicio).getTime()) / 60_000)); return `${m} min`; }
  protected faltan(p: Pasada): string {
    const h = (new Date(p.inicio).getTime() - Date.now()) / 3_600_000;
    if (h < 1) return h <= 0 ? 'ahora' : `en ${Math.max(1, Math.round(h * 60))} min`;
    return h < 24 ? `en ${Math.round(h)} h` : `en ${Math.round(h / 24)} d`;
  }
}
