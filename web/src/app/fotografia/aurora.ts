import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

interface Kp { hora: string; kp: number; }
interface Prevision { minKp: number | null; kpMaximo: number; horaMaxima: string | null; posible: boolean; resumen: string; horas: Kp[]; }
interface Respuesta { sinLugar?: boolean; sinDatos?: boolean; lugar?: string; prevision?: Prevision; }

// ¿Habrá aurora? El Kp previsto por el NOAA (barras por tramos de 3 horas) contra el que hace falta desde tu latitud.
@Component({
  selector: 'app-aurora',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrls: ['../gastos/comun.css'],
  template: `
    @if (datos()?.prevision; as p) {
      <section class="caja">
        <header class="caja__cab"><h3>Aurora boreal</h3><span class="sub">Kp previsto por el NOAA</span></header>
        <p class="estado" [class.estado--si]="p.posible">{{ p.posible ? '🌌 ' : '' }}{{ p.resumen }}</p>
        <div class="barras" role="img" [attr.aria-label]="'Kp previsto, máximo ' + p.kpMaximo">
          @for (h of p.horas.slice(0, 24); track h.hora) {
            <span class="barra" [class.barra--ok]="p.minKp !== null && h.kp >= p.minKp" [style.height.%]="(h.kp / 9) * 100" [title]="fecha(h.hora) + ' · Kp ' + h.kp"></span>
          }
        </div>
        @if (p.minKp !== null) { <small class="nota">Las barras de color alcanzan el Kp que hace falta en tu latitud ({{ p.minKp }}).</small> }
      </section>
    }
  `,
  styles: `
    .estado { margin: 0 0 12px; color: var(--soft); }
    .estado--si { color: var(--texto); font-weight: 600; }
    .barras { display: flex; align-items: flex-end; gap: 3px; height: 70px; }
    .barra { flex: 1; min-height: 3px; border-radius: 3px 3px 0 0; background: color-mix(in srgb, var(--texto) 22%, transparent); }
    .barra--ok { background: var(--brand); }
    .nota { display: block; margin-top: 8px; color: var(--soft); font-size: .76rem; }
  `,
})
export class Aurora implements OnInit {
  private readonly http = inject(HttpClient);
  protected readonly datos = signal<Respuesta | null>(null);
  ngOnInit(): void { firstValueFrom(this.http.get<Respuesta>('/api/luz/aurora')).then((d) => this.datos.set(d)).catch(() => {}); }
  protected fecha(h: string): string { return new Date(h).toLocaleString('es-ES', { weekday: 'short', hour: '2-digit', minute: '2-digit' }); }
}
