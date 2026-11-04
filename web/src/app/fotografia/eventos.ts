import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

interface EventoCielo { tipo: string; nombre: string; pico: string; detalle: string; zhr: number | null; lunaPct: number; cielo: string; nubes: number | null; }

const ICONO: Record<string, string> = { lluvia: '☄️', 'eclipse-solar': '🌘', 'eclipse-lunar': '🌖' };

// Lo que viene en el cielo en los próximos meses: lluvias de meteoros (con la luna de esa noche) y eclipses.
@Component({
  selector: 'app-eventos-cielo',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrls: ['../gastos/comun.css'],
  template: `
    @if (lista().length) {
      <section class="caja">
        <header class="caja__cab"><h3>Lo que viene en el cielo</h3><span class="sub">meteoros y eclipses de los próximos meses</span></header>
        <ul class="eventos">
          @for (e of lista(); track e.nombre + e.pico) {
            <li class="evento">
              <span class="evento__icono" aria-hidden="true">{{ icono(e.tipo) }}</span>
              <div class="evento__texto">
                <strong>{{ e.nombre }}</strong>
                <small>{{ cuando(e) }}{{ e.zhr ? ' · hasta ' + e.zhr + ' por hora' : '' }}</small>
                <small>{{ e.detalle }}</small>
                <small class="evento__luna">🌙 {{ e.lunaPct }} % de luna{{ e.cielo ? ' · ' + e.cielo : '' }}{{ e.nubes !== null ? ' · nubes ' + e.nubes + ' %' : '' }}</small>
              </div>
              <span class="evento__faltan">{{ faltan(e) }}</span>
            </li>
          }
        </ul>
      </section>
    }
  `,
  styles: `
    .eventos { list-style: none; margin: 0; padding: 0; display: grid; gap: 8px; }
    .evento { display: grid; grid-template-columns: 40px minmax(0, 1fr) auto; gap: 12px; align-items: start; padding: 10px 12px; border-radius: 14px; border: 1px solid var(--vidrio-borde); background: color-mix(in srgb, var(--texto) 4%, transparent); }
    .evento__icono { display: grid; place-items: center; width: 40px; height: 40px; border-radius: 12px; background: color-mix(in srgb, var(--brand) 16%, transparent); font-size: 1.2rem; }
    .evento__texto { display: grid; gap: 2px; min-width: 0; }
    .evento__texto small { color: var(--soft); font-size: .78rem; }
    .evento__luna { color: var(--texto) !important; opacity: .8; }
    .evento__faltan { font: 600 .78rem/1 var(--font-code); color: var(--acento); white-space: nowrap; padding-top: 4px; }
  `,
})
export class EventosCielo implements OnInit {
  private readonly http = inject(HttpClient);
  protected readonly lista = signal<EventoCielo[]>([]);

  ngOnInit(): void { firstValueFrom(this.http.get<EventoCielo[]>('/api/luz/eventos')).then((l) => this.lista.set(l.slice(0, 6))).catch(() => {}); }

  protected icono(t: string): string { return ICONO[t] ?? '✨'; }
  protected cuando(e: EventoCielo): string {
    const f = new Date(e.pico);
    return e.tipo === 'lluvia'
      ? `noche del ${f.toLocaleDateString('es-ES', { day: 'numeric', month: 'long' })}, mejor de madrugada`
      : f.toLocaleString('es-ES', { day: 'numeric', month: 'long', year: 'numeric', hour: '2-digit', minute: '2-digit' });
  }
  protected faltan(e: EventoCielo): string {
    const d = Math.ceil((new Date(e.pico).getTime() - Date.now()) / 86_400_000);
    return d <= 0 ? 'hoy' : d === 1 ? 'mañana' : `en ${d} días`;
  }
}
