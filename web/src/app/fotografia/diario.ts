import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

interface Punto { id: number; nombre: string; }
interface Salida { id: number; fecha: string; puntoId: number | null; tipo: string; prevision: number | null; resultado: number; nota: string; }
interface Calibracion { tipo: string; salidas: number; diferencia: number; texto: string; }

const TIPOS = [{ v: 'amanecer', n: 'Amanecer' }, { v: 'atardecer', n: 'Atardecer' }, { v: 'noche', n: 'Noche' }];

// Diario de salidas: anota qué prometía la previsión y cómo salió de verdad; con unas cuantas salidas dice cuánto fiarte de la nota de cada tipo de luz.
@Component({
  selector: 'app-diario-salidas',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrls: ['../gastos/comun.css'],
  template: `
    <section class="caja">
      <header class="caja__cab"><h3>Diario de salidas</h3><span class="sub">lo que prometía el cielo y lo que salió</span></header>
      @if (error()) { <p class="error" role="alert">{{ error() }}</p> }

      @for (c of calibracion(); track c.tipo) {
        <p class="calibra"><b>{{ nombreTipo(c.tipo) }}</b> ({{ c.salidas }} salidas): {{ c.texto }}</p>
      }

      <div class="rejilla">
        <label>Fecha<input type="date" [value]="fecha()" (input)="fecha.set($any($event.target).value)"></label>
        <label>Luz<select (change)="tipo.set($any($event.target).value)">@for (t of tipos; track t.v) { <option [value]="t.v" [selected]="t.v === tipo()">{{ t.n }}</option> }</select></label>
        <label>Sitio<select (change)="puntoId.set($any($event.target).value)"><option value="">—</option>@for (p of puntos(); track p.id) { <option [value]="p.id">{{ p.nombre }}</option> }</select></label>
        <label>Nota que daba la app (1-10)<input type="number" inputmode="numeric" min="1" max="10" [value]="prevision()" (input)="prevision.set($any($event.target).value)"></label>
      </div>
      <div class="estrellas" role="radiogroup" aria-label="Cómo salió, de 1 a 5">
        <span>¿Cómo salió?</span>
        @for (n of [1, 2, 3, 4, 5]; track n) {
          <button type="button" role="radio" [attr.aria-checked]="resultado() === n" [class.on]="n <= resultado()" (click)="resultado.set(n)" [attr.aria-label]="n + ' de 5'">★</button>
        }
      </div>
      <label class="ancho">Nota<input type="text" maxlength="500" placeholder="Qué pasó, qué cambiarías…" [value]="nota()" (input)="nota.set($any($event.target).value)"></label>
      <div class="acciones"><button type="button" class="boton" (click)="guardar()" [disabled]="!valido()">Apuntar salida</button></div>

      @if (salidas().length) {
        <ul class="salidas">
          @for (s of salidas().slice(0, 8); track s.id) {
            <li>
              <span class="salidas__est" aria-hidden="true">{{ '★'.repeat(s.resultado) }}</span>
              <div class="salidas__texto">
                <strong>{{ dia(s.fecha) }} · {{ nombreTipo(s.tipo) }}{{ nombrePunto(s.puntoId) }}</strong>
                <small>{{ s.prevision !== null ? 'prevista ' + s.prevision + '/10 · ' : '' }}{{ s.nota }}</small>
              </div>
              <button type="button" class="enlace" (click)="borrar(s)" [attr.aria-label]="'Borrar la salida del ' + dia(s.fecha)">Borrar</button>
            </li>
          }
        </ul>
      }
    </section>
  `,
  styles: `
    .calibra { margin: 0 0 10px; padding: 10px 12px; border-radius: 14px; background: color-mix(in srgb, var(--brand) 10%, transparent); font-size: .88rem; }
    .rejilla { display: grid; grid-template-columns: repeat(auto-fit, minmax(140px, 1fr)); gap: 10px; }
    label { display: grid; gap: 4px; font-size: .78rem; color: var(--soft); }
    .ancho { margin-top: 10px; }
    input, select { min-height: 40px; border-radius: 12px; border: 1px solid var(--vidrio-borde); background: color-mix(in srgb, var(--texto) 5%, transparent); color: var(--texto); padding: 0 12px; font: 500 .95rem/1 var(--font-text); }
    .estrellas { display: flex; align-items: center; gap: 4px; margin-top: 10px; color: var(--soft); font-size: .84rem; }
    .estrellas span { margin-right: 6px; }
    .estrellas button { width: 40px; height: 40px; border: 0; background: none; font-size: 1.5rem; line-height: 1; color: color-mix(in srgb, var(--texto) 25%, transparent); cursor: pointer; }
    .estrellas button.on { color: var(--brand); }
    .acciones { display: flex; justify-content: flex-end; margin-top: 10px; }
    .salidas { list-style: none; margin: 14px 0 0; padding: 0; display: grid; gap: 6px; }
    .salidas li { display: grid; grid-template-columns: 64px minmax(0, 1fr) auto; gap: 10px; align-items: center; padding: 8px 10px; border-radius: 14px; border: 1px solid var(--vidrio-borde); }
    .salidas__est { color: var(--brand); letter-spacing: -1px; font-size: .9rem; }
    .salidas__texto { display: grid; gap: 2px; min-width: 0; }
    .salidas__texto small { color: var(--soft); font-size: .78rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  `,
})
export class DiarioSalidas implements OnInit {
  private readonly http = inject(HttpClient);
  protected readonly tipos = TIPOS;
  protected readonly salidas = signal<Salida[]>([]);
  protected readonly calibracion = signal<Calibracion[]>([]);
  protected readonly puntos = signal<Punto[]>([]);
  protected readonly error = signal('');
  protected readonly fecha = signal(new Date().toLocaleDateString('sv-SE'));
  protected readonly tipo = signal('noche');
  protected readonly puntoId = signal('');
  protected readonly prevision = signal('');
  protected readonly resultado = signal(0);
  protected readonly nota = signal('');
  protected readonly valido = computed(() => this.fecha() !== '' && this.resultado() > 0);

  ngOnInit(): void {
    void this.leer();
    firstValueFrom(this.http.get<Punto[]>('/api/luz/puntos')).then((p) => this.puntos.set(p)).catch(() => {});
  }

  private async leer(): Promise<void> {
    try {
      const r = await firstValueFrom(this.http.get<{ salidas: Salida[]; calibracion: Calibracion[] }>('/api/luz/salidas'));
      this.salidas.set(r.salidas); this.calibracion.set(r.calibracion);
    } catch { /* sin diario todavía */ }
  }

  protected nombreTipo(t: string): string { return TIPOS.find((x) => x.v === t)?.n ?? t; }
  protected nombrePunto(id: number | null): string { const p = this.puntos().find((x) => x.id === id); return p ? ' · ' + p.nombre : ''; }
  protected dia(f: string): string { return new Date(f + 'T12:00:00').toLocaleDateString('es-ES', { day: 'numeric', month: 'short' }); }

  protected async guardar(): Promise<void> {
    this.error.set('');
    try {
      await firstValueFrom(this.http.post('/api/luz/salidas', {
        fecha: this.fecha(), tipo: this.tipo(), puntoId: this.puntoId() ? Number(this.puntoId()) : null,
        prevision: this.prevision() ? Number(this.prevision()) : null, resultado: this.resultado(), nota: this.nota(),
      }));
      this.resultado.set(0); this.nota.set(''); this.prevision.set('');
      await this.leer();
    } catch (e) {
      const err = (e as HttpErrorResponse).error as { errors?: Record<string, string[]> } | undefined;
      this.error.set(Object.values(err?.errors ?? {})[0]?.[0] ?? 'No se pudo apuntar.');
    }
  }

  protected async borrar(s: Salida): Promise<void> {
    if (!confirm('¿Borrar esta salida del diario?')) return;
    try { await firstValueFrom(this.http.delete(`/api/luz/salidas/${s.id}`)); await this.leer(); } catch { this.error.set('No se pudo borrar.'); }
  }
}
