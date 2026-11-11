import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ICONOS } from '../core/iconos';

interface Proximo { id: number; nombre: string; apodo: string; dias: number; edad: number | null; }

// «Próximos cumpleaños»: los de las próximas dos semanas, en una línea. Sin nadie cerca no se pinta nada.
@Component({
  selector: 'app-cumples-chip',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  template: `
    @if (lista().length) {
      <a class="cumples" routerLink="/personas" aria-label="Próximos cumpleaños">
        <span class="cumples__ico"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path [attr.d]="iconos.tarta" /></svg></span>
        <span class="cumples__texto">
          @for (p of lista().slice(0, 3); track p.id) { <span><strong>{{ p.apodo || p.nombre }}</strong> {{ cuando(p.dias) }}{{ p.edad ? ' (' + p.edad + ')' : '' }}</span> }
        </span>
      </a>
    }
  `,
  styles: `
    :host { display: block; }
    .cumples { display: flex; align-items: center; gap: 12px; padding: 10px 14px; border-radius: var(--radio-l, 16px); border: 1px solid var(--vidrio-borde); background: var(--vidrio); color: var(--texto); text-decoration: none; }
    .cumples__ico { display: grid; place-items: center; } .cumples__ico .icon { width: 20px; height: 20px; stroke-width: 1.5; color: var(--acento); }
    .cumples__texto { display: flex; flex-wrap: wrap; gap: 4px 14px; font-size: .9rem; }
    .cumples__texto span { white-space: nowrap; }
  `,
})
export class CumplesChip {
  private readonly http = inject(HttpClient);
  protected readonly iconos = ICONOS;
  protected readonly lista = signal<Proximo[]>([]);

  constructor() { void this.cargar(); }

  private async cargar(): Promise<void> {
    try { this.lista.set(await firstValueFrom(this.http.get<Proximo[]>('/api/personas/proximos?dias=14'))); } catch { this.lista.set([]); }
  }

  protected cuando(d: number): string { return d === 0 ? 'hoy' : d === 1 ? 'mañana' : `en ${d} días`; }
}
