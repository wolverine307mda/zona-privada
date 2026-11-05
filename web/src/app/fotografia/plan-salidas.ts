import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { EstadoError, Esqueleto } from '../ui';

interface Salida {
  puntoId: number | null; punto: string; orientacion: string; nota: number; orienta: string | null; tipo: string; titulo: string;
  inicio: string; fin: string; detalle: string; motivo: string | null; notaPrevision: number;
}

// Las mejores salidas de la semana entre todos tus sitios (o tu lugar de siempre si aún no has guardado ninguno): la nota viene de la previsión
// de nubes y luna y se ajusta un punto según hacia dónde mira el sitio. Es una estimación, no una promesa: el diario de salidas la calibra.
@Component({
  selector: 'app-plan-salidas',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Esqueleto, EstadoError],
  styleUrls: ['../gastos/comun.css'],
  template: `
    <section class="caja">
      <header class="caja__cab"><h3>Mejores salidas de la semana</h3><span class="sub">nota de 1 a 10 según nubes, luna y hacia dónde mira cada sitio</span></header>
      @if (fallo()) { <app-estado-error titulo="No pude preparar el plan" (reintentar)="cargar()" /> }
      @else if (lista() === null) { <app-esqueleto forma="bloque" alto="72px" /><app-esqueleto forma="bloque" alto="72px" /> }
      @else {
        <ol class="plan">
          @for (s of lista()!; track s.inicio + s.punto) {
            <li>
              <span class="plan__nota" [class.plan__nota--alta]="s.nota >= 8" [attr.aria-label]="'Nota ' + s.nota + ' de 10'">{{ s.nota }}</span>
              <div class="plan__texto">
                <strong>{{ dia(s.inicio) }} · {{ hora(s.inicio) }} · {{ s.punto }}</strong>
                <small>{{ s.titulo }}{{ s.motivo ? ' · ' + s.motivo : '' }}</small>
                @if (s.orienta) { <small class="plan__orienta">{{ s.punto }} {{ s.orienta }}</small> }
              </div>
              <span class="plan__faltan">{{ faltan(s) }}</span>
            </li>
          } @empty { <p class="nada">Sin una previsión clara para los próximos días: vuelve a mirar más tarde.</p> }
        </ol>
      }
    </section>
  `,
  styles: `
    .plan { list-style: none; margin: 0; padding: 0; display: grid; gap: 8px; }
    .plan li { display: grid; grid-template-columns: 44px minmax(0, 1fr) auto; gap: 12px; align-items: center; padding: 10px 12px; border-radius: 14px; border: 1px solid var(--vidrio-borde); background: color-mix(in srgb, var(--texto) 5%, transparent); }
    .plan__nota { display: grid; place-items: center; width: 44px; height: 44px; border-radius: 50%; font: 600 1.1rem/1 var(--font-text); font-variant-numeric: tabular-nums; border: 2px solid var(--linea); }
    .plan__nota--alta { border-color: var(--ok); color: var(--ok); }
    .plan__texto { display: grid; gap: 2px; min-width: 0; }
    .plan__texto strong { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .plan__texto small { color: var(--soft); font-size: .78rem; }
    .plan__orienta { color: var(--texto) !important; opacity: .8; }
    .plan__faltan { font: 600 .78rem/1 var(--font-code); color: var(--acento); white-space: nowrap; }
  `,
})
export class PlanSalidas implements OnInit {
  private readonly http = inject(HttpClient);
  protected readonly lista = signal<Salida[] | null>(null);
  protected readonly fallo = signal(false);

  ngOnInit(): void { void this.cargar(); }

  protected async cargar(): Promise<void> {
    this.fallo.set(false);
    try { this.lista.set(await firstValueFrom(this.http.get<Salida[]>('/api/luz/puntos/plan'))); } catch { this.fallo.set(true); }
  }

  protected dia(iso: string): string { return new Date(iso).toLocaleDateString('es-ES', { weekday: 'short', day: 'numeric', month: 'short' }); }
  protected hora(iso: string): string { return new Date(iso).toLocaleTimeString('es-ES', { hour: '2-digit', minute: '2-digit' }); }
  protected faltan(s: Salida): string {
    const h = (new Date(s.inicio).getTime() - Date.now()) / 3_600_000;
    if (h <= 0) return 'ahora';
    return h < 24 ? `en ${Math.max(1, Math.round(h))} h` : `en ${Math.round(h / 24)} d`;
  }
}
