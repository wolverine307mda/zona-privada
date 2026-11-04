import { HttpClient } from '@angular/common/http';
import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Esqueleto } from '../ui';

interface Punto { hora: string; ms: number | null; enLinea: boolean; caidas: number; muestras: number; }
interface Datos { puntos: Punto[]; resumen: { muestras: number; caidas: number; disponibilidad: number | null; mediaMs: number | null; maximaMs: number | null }; }

const RANGOS = [{ h: 24, nombre: '24 h' }, { h: 168, nombre: '7 días' }, { h: 720, nombre: '30 días' }];

// La gráfica de latencia de un aparato (el router): una línea con los milisegundos y bandas rojas donde no contestó.
@Component({
  selector: 'app-grafica-red',
  imports: [Esqueleto],
  template: `
    <div class="rangos" role="group" aria-label="Periodo de la gráfica">
      @for (r of rangos; track r.h) { <button type="button" [attr.aria-pressed]="horas() === r.h" (click)="ver(r.h)">{{ r.nombre }}</button> }
    </div>
    @if (datos(); as d) {
      @if (d.puntos.length > 1) {
        <svg viewBox="0 0 300 90" preserveAspectRatio="none" role="img" [attr.aria-label]="'Latencia media de ' + (d.resumen.mediaMs ?? 0) + ' milisegundos y disponibilidad del ' + (d.resumen.disponibilidad ?? 0) + ' por ciento'">
          @for (b of bandas(); track $index) { <rect class="caida" [attr.x]="b.x" y="0" [attr.width]="b.w" height="90"/> }
          <polyline class="linea" [attr.points]="linea()" fill="none" vector-effect="non-scaling-stroke"/>
        </svg>
        <div class="datos">
          <span><b>{{ d.resumen.disponibilidad }} %</b> disponible</span>
          <span><b>{{ d.resumen.mediaMs ?? '–' }} ms</b> de media</span>
          <span><b>{{ d.resumen.maximaMs ?? '–' }} ms</b> máximo</span>
          <span [class.mal]="d.resumen.caidas > 0"><b>{{ d.resumen.caidas }}</b> sin respuesta</span>
        </div>
      } @else {
        <p class="nada">Aún no hay lecturas: se guarda una cada minuto. Vuelve en unos minutos.</p>
      }
    } @else { <app-esqueleto forma="grafica" alto="120px" /> }
  `,
  styles: `
    :host { display: grid; gap: 10px; padding: 10px 2px 2px; }
    .rangos { display: inline-flex; gap: 4px; padding: 3px; border-radius: 999px; border: 1px solid var(--vidrio-borde); justify-self: start; }
    .rangos button { min-height: 30px; padding: 0 12px; border: 0; border-radius: 999px; background: none; color: var(--soft); font: 600 .74rem/1 var(--font-text); cursor: pointer; }
    .rangos button[aria-pressed='true'] { background: var(--brand); color: var(--on-brand); }
    svg { width: 100%; height: 96px; border-radius: 12px; background: color-mix(in srgb, var(--texto) 4%, transparent); }
    .linea { stroke: var(--acento); stroke-width: 2; stroke-linejoin: round; stroke-linecap: round; }
    .caida { fill: color-mix(in srgb, var(--bad) 35%, transparent); }
    .datos { display: flex; flex-wrap: wrap; gap: 4px 18px; font-size: .78rem; color: var(--soft); }
    .datos b { color: var(--texto); font-variant-numeric: tabular-nums; }
    .datos .mal b { color: var(--bad); }
    .nada { margin: 0; color: var(--soft); font-size: .84rem; }
  `,
})
export class GraficaRed implements OnInit {
  readonly id = input.required<number>();
  private readonly http = inject(HttpClient);
  protected readonly rangos = RANGOS;
  protected readonly horas = signal(24);
  protected readonly datos = signal<Datos | null>(null);

  // x en 0..300 según la posición del punto; y en 4..86 (más latencia, más arriba), con el máximo del tramo como techo
  private readonly ancho = computed(() => Math.max(1, (this.datos()?.puntos.length ?? 1) - 1));
  protected readonly linea = computed(() => {
    const p = this.datos()?.puntos ?? [];
    const techo = Math.max(10, ...p.map((x) => x.ms ?? 0));
    return p.filter((x) => x.ms !== null).map((x) => `${(p.indexOf(x) / this.ancho()) * 300},${86 - (x.ms! / techo) * 80}`).join(' ');
  });
  // cada tramo sin respuesta, como una banda roja de lo que ocupa
  protected readonly bandas = computed(() => {
    const p = this.datos()?.puntos ?? [];
    const w = 300 / this.ancho();
    return p.map((x, i) => ({ x: Math.max(0, (i - 0.5) * w), w, caida: !x.enLinea })).filter((b) => b.caida).map(({ x, w }) => ({ x, w }));
  });

  ngOnInit(): void { void this.leer(); }

  protected ver(h: number): void { this.horas.set(h); this.datos.set(null); void this.leer(); }

  private async leer(): Promise<void> {
    try { this.datos.set(await firstValueFrom(this.http.get<Datos>(`/api/red/${this.id()}/historial?horas=${this.horas()}`))); } catch { this.datos.set({ puntos: [], resumen: { muestras: 0, caidas: 0, disponibilidad: null, mediaMs: null, maximaMs: null } }); }
  }
}
