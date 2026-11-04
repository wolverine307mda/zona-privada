import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { categoria } from './modelo';

interface Regla { comercio: string; categoria: string; nombre: string; veces: number; }

// Lo que la app ha aprendido de tus correcciones: «este comercio va en esta categoría». Se usa en Apple Pay, la importación y el asistente.
@Component({
  selector: 'app-reglas-categoria',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrls: ['./comun.css'],
  template: `
    @if (lista().length) {
      <section class="caja">
        <header class="caja__cab"><h3>Lo que he aprendido</h3><span class="sub">cada vez que corriges una categoría</span></header>
        <ul class="reglas">
          @for (r of lista(); track r.comercio) {
            <li>
              <span><strong>{{ r.nombre }}</strong> → {{ nombre(r.categoria) }} <small>{{ r.veces }} {{ r.veces === 1 ? 'vez' : 'veces' }}</small></span>
              <button type="button" class="enlace" (click)="olvidar(r)" [attr.aria-label]="'Olvidar la regla de ' + r.nombre">Olvidar</button>
            </li>
          }
        </ul>
      </section>
    }
  `,
  styles: `
    .reglas { list-style: none; margin: 0; padding: 0; display: grid; gap: 6px; }
    .reglas li { display: flex; justify-content: space-between; align-items: center; gap: 10px; min-height: 40px; }
    .reglas small { color: var(--soft); margin-left: 4px; }
  `,
})
export class ReglasCategoria implements OnInit {
  private readonly http = inject(HttpClient);
  protected readonly lista = signal<Regla[]>([]);

  ngOnInit(): void { void this.leer(); }

  private async leer(): Promise<void> {
    try { this.lista.set((await firstValueFrom(this.http.get<Regla[]>('/api/gastos/reglas'))).slice(0, 30)); } catch { /* sin reglas todavía */ }
  }

  protected nombre(c: string): string { return categoria(c)?.nombre ?? c; }

  protected async olvidar(r: Regla): Promise<void> {
    try { await firstValueFrom(this.http.delete(`/api/gastos/reglas/${encodeURIComponent(r.comercio)}`)); await this.leer(); } catch { /* se queda */ }
  }
}
