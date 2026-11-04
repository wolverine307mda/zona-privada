import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

interface DiaNiebla { fecha: string; nivel: string; texto: string; puntos: number; hora: string | null; }
interface Respuesta { sinLugar?: boolean; sinDatos?: boolean; dias?: DiaNiebla[]; }

const ICONO: Record<string, string> = { 'mar-de-nubes': '☁️', niebla: '🌫️', inversion: '🌁', nada: '·' };
const NOMBRE: Record<string, string> = { 'mar-de-nubes': 'Mar de nubes', niebla: 'Niebla', inversion: 'Inversión térmica', nada: 'Nada especial' };

// Los próximos amaneceres con niebla, inversión térmica o mar de nubes (temperatura, rocío, viento, nubes bajas y temperatura a 1.500 m).
@Component({
  selector: 'app-niebla',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrls: ['../gastos/comun.css'],
  template: `
    @if (datos()?.dias; as dias) {
      <section class="caja">
        <header class="caja__cab"><h3>Niebla y mar de nubes</h3><span class="sub">los próximos amaneceres</span></header>
        <ul class="dias">
          @for (d of dias; track d.fecha) {
            <li class="dia" [class.dia--si]="d.puntos > 0">
              <span class="dia__icono" aria-hidden="true">{{ icono(d.nivel) }}</span>
              <div class="dia__texto">
                <strong>{{ dia(d.fecha) }} · {{ nombre(d.nivel) }}{{ d.hora ? ' a las ' + d.hora : '' }}</strong>
                <small>{{ d.texto }}</small>
              </div>
            </li>
          }
        </ul>
      </section>
    }
  `,
  styles: `
    .dias { list-style: none; margin: 0; padding: 0; display: grid; gap: 8px; }
    .dia { display: grid; grid-template-columns: 40px minmax(0, 1fr); gap: 12px; align-items: center; padding: 10px 12px; border-radius: 14px; border: 1px solid var(--vidrio-borde); opacity: .7; }
    .dia--si { opacity: 1; border-color: color-mix(in srgb, var(--brand) 50%, transparent); background: color-mix(in srgb, var(--brand) 8%, transparent); }
    .dia__icono { display: grid; place-items: center; width: 40px; height: 40px; border-radius: 12px; background: color-mix(in srgb, var(--texto) 8%, transparent); font-size: 1.2rem; }
    .dia__texto { display: grid; gap: 2px; min-width: 0; }
    .dia__texto small { color: var(--soft); font-size: .78rem; }
  `,
})
export class Niebla implements OnInit {
  private readonly http = inject(HttpClient);
  protected readonly datos = signal<Respuesta | null>(null);
  ngOnInit(): void { firstValueFrom(this.http.get<Respuesta>('/api/luz/niebla')).then((d) => this.datos.set(d)).catch(() => {}); }
  protected icono(n: string): string { return ICONO[n] ?? '·'; }
  protected nombre(n: string): string { return NOMBRE[n] ?? n; }
  protected dia(f: string): string { return new Date(f + 'T12:00:00').toLocaleDateString('es-ES', { weekday: 'long', day: 'numeric' }); }
}
