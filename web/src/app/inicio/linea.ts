import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

interface Suceso { hora: string; tipo: string; titulo: string; detalle: string; }

const ICONO: Record<string, string> = { dinero: '💸', foto: '📷', pc: '🖥️', acceso: '🔓', alerta: '🚨' };
const hoyIso = (): string => new Date().toLocaleDateString('sv-SE');

// La línea del tiempo de un día: gastos, salidas de fotografía, órdenes al PC y accesos a la app, todo en orden.
@Component({
  selector: 'app-linea',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="linea tarjeta">
      <header>
        <h3>Tu día</h3>
        <div class="dias">
          <button type="button" (click)="mover(-1)" aria-label="Día anterior">‹</button>
          <span>{{ titulo() }}</span>
          <button type="button" (click)="mover(1)" [disabled]="esHoy()" aria-label="Día siguiente">›</button>
        </div>
      </header>
      @if (sucesos().length) {
        <ol>
          @for (s of sucesos(); track s.hora + s.titulo) {
            <li [attr.data-tipo]="s.tipo">
              <time>{{ hora(s.hora) }}</time>
              <span class="ico" aria-hidden="true">{{ icono(s.tipo) }}</span>
              <span class="txt"><strong>{{ s.titulo }}</strong><small>{{ s.detalle }}</small></span>
            </li>
          }
        </ol>
      } @else { <p class="vacio">{{ cargado() ? 'Nada apuntado este día.' : 'Cargando…' }}</p> }
    </section>
  `,
  styles: `
    :host { display: block; }
    .linea { padding: 16px 18px; }
    header { display: flex; align-items: center; justify-content: space-between; gap: 10px; margin-bottom: 10px; flex-wrap: wrap; }
    header h3 { margin: 0; font: 600 1rem/1.2 var(--font-display); }
    .dias { display: inline-flex; align-items: center; gap: 8px; font-size: .86rem; color: var(--soft); }
    .dias button { width: 40px; height: 40px; border-radius: 50%; border: 1px solid var(--vidrio-borde); background: none; color: var(--texto); font-size: 1.2rem; cursor: pointer; }
    .dias button:disabled { opacity: .35; cursor: default; }
    ol { list-style: none; margin: 0; padding: 0; display: grid; gap: 6px; max-height: 360px; overflow: auto; }
    li { display: grid; grid-template-columns: 44px 28px minmax(0, 1fr); gap: 8px; align-items: center; padding: 6px 4px; border-bottom: 1px solid var(--vidrio-borde); }
    li[data-tipo='alerta'] strong { color: #e5484d; }
    time { font: 600 .78rem/1 var(--font-code); color: var(--soft); }
    .txt { display: grid; min-width: 0; }
    .txt strong { font-weight: 500; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .txt small { color: var(--soft); font-size: .76rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .vacio { margin: 0; color: var(--soft); }
  `,
})
export class Linea implements OnInit {
  private readonly http = inject(HttpClient);
  protected readonly fecha = signal(hoyIso());
  protected readonly sucesos = signal<Suceso[]>([]);
  protected readonly cargado = signal(false);
  protected readonly esHoy = computed(() => this.fecha() === hoyIso());
  protected readonly titulo = computed(() => this.esHoy() ? 'Hoy' : new Date(this.fecha() + 'T12:00:00').toLocaleDateString('es-ES', { weekday: 'short', day: 'numeric', month: 'short' }));

  ngOnInit(): void { void this.leer(); }

  protected icono(t: string): string { return ICONO[t] ?? '•'; }
  protected hora(iso: string): string { return new Date(iso).toLocaleTimeString('es-ES', { hour: '2-digit', minute: '2-digit' }); }

  protected mover(d: number): void {
    const f = new Date(this.fecha() + 'T12:00:00'); f.setDate(f.getDate() + d);
    this.fecha.set(f.toLocaleDateString('sv-SE'));
    void this.leer();
  }

  private async leer(): Promise<void> {
    this.cargado.set(false);
    try { this.sucesos.set((await firstValueFrom(this.http.get<{ sucesos: Suceso[] }>('/api/inicio/linea?fecha=' + this.fecha()))).sucesos); } catch { this.sucesos.set([]); }
    this.cargado.set(true);
  }
}
