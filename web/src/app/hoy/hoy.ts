import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { EstadoError, Esqueleto } from '../ui';

interface Presupuesto { categoria: string; nombre: string; porcentaje: number; gastado: number; limite: number; }
interface Pendiente { id: number; nombre: string; }
interface Resumen {
  fecha: string; gastoHoy: number; gastoHoyTexto: string; presupuestos: Presupuesto[]; agenda: string;
  habitos: { total: number; hechos: number; pendientes: Pendiente[] }; salida: string | null; lineas: string[];
}

// «Hoy» en una sola pantalla pensada para abrir y mirar (en el iPhone, sin desplazarse): gasto de hoy, presupuestos que aprietan, la agenda,
// los hábitos pendientes (con un toque para apuntarlos) y la mejor salida de la semana. Se refresca al volver a la app.
@Component({
  selector: 'app-hoy',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, EstadoError, Esqueleto],
  styleUrls: ['../gastos/comun.css'],
  template: `
    <header class="cab">
      <h2 class="cab__titulo">{{ titulo() }}</h2>
      <button type="button" class="enlace" (click)="cargar()" [disabled]="cargando()" aria-label="Actualizar">Actualizar</button>
    </header>

    @if (fallo()) { <app-estado-error titulo="No pude cargar lo de hoy" (reintentar)="cargar()" /> }
    @else if (datos() === null) { <section class="caja"><app-esqueleto forma="bloque" alto="96px" /><app-esqueleto forma="bloque" alto="96px" /></section> }
    @else {
      <a class="caja gasto" routerLink="/gastos" aria-label="Gastos de hoy">
        <p class="etq">Gastado hoy</p>
        <p class="gasto__cifra">{{ datos()!.gastoHoyTexto }}</p>
        @if (datos()!.presupuestos.length) {
          <ul class="presu">
            @for (p of datos()!.presupuestos; track p.categoria) {
              <li [class.presu--pasado]="p.porcentaje >= 100">{{ p.nombre }} · {{ p.porcentaje }} %</li>
            }
          </ul>
        }
      </a>

      <a class="caja" routerLink="/agenda" aria-label="Agenda de hoy">
        <p class="etq">Agenda</p>
        <p class="agenda">{{ datos()!.agenda }}</p>
      </a>

      @if (datos()!.habitos.total > 0) {
        <section class="caja" aria-label="Hábitos de hoy">
          <header class="caja__cab"><h3>Hábitos</h3><a class="enlace" routerLink="/habitos">{{ datos()!.habitos.hechos }} de {{ datos()!.habitos.total }}</a></header>
          @for (h of datos()!.habitos.pendientes; track h.id) {
            <button type="button" class="habito" [disabled]="ocupado() === h.id" (click)="hecho(h)">
              <span class="habito__marca" aria-hidden="true"></span>{{ h.nombre }}
            </button>
          } @empty { <p class="nada">Todo hecho por hoy.</p> }
        </section>
      }

      @if (datos()!.salida) {
        <a class="caja" routerLink="/fotografia/sitios" aria-label="Mejor salida de la semana">
          <p class="etq">Mejor salida de la semana</p>
          <p class="agenda">{{ datos()!.salida }}</p>
        </a>
      }
    }
  `,
  styles: `
    .cab { display: flex; align-items: center; justify-content: space-between; gap: 12px; margin-bottom: var(--esp-4); }
    .cab__titulo { margin: 0; font: var(--peso-ligero) clamp(1.3rem, 5vw, 1.8rem)/1.1 var(--font-text); }
    .cab__titulo::first-letter { text-transform: uppercase; }
    a.caja { display: block; color: inherit; text-decoration: none; margin-bottom: var(--esp-3); }
    .caja { margin-bottom: var(--esp-3); }
    .etq { margin: 0 0 4px; color: var(--soft); font-size: .72rem; letter-spacing: .1em; text-transform: uppercase; }
    .gasto__cifra { margin: 0; font: var(--peso-ligero) 2.2rem/1.1 var(--font-text); font-variant-numeric: tabular-nums; }
    .presu { list-style: none; margin: 10px 0 0; padding: 0; display: flex; flex-wrap: wrap; gap: 6px; }
    .presu li { padding: 5px 10px; border-radius: 999px; font-size: .8rem; background: color-mix(in srgb, var(--warn) 18%, transparent); }
    .presu--pasado { background: color-mix(in srgb, var(--bad) 22%, transparent) !important; }
    .agenda { margin: 0; white-space: pre-line; line-height: 1.45; }
    .habito { display: flex; align-items: center; gap: 12px; width: 100%; min-height: 52px; padding: 0 4px; border: 0; border-bottom: 1px solid var(--linea); background: none; color: inherit; font: inherit; text-align: left; cursor: pointer; }
    .habito:last-of-type { border-bottom: 0; }
    .habito__marca { flex: none; width: 26px; height: 26px; border-radius: 50%; border: 2px solid var(--linea); }
    .habito:active .habito__marca { background: var(--ok); border-color: var(--ok); }
  `,
})
export class Hoy {
  private readonly http = inject(HttpClient);
  protected readonly datos = signal<Resumen | null>(null);
  protected readonly fallo = signal(false);
  protected readonly cargando = signal(false);
  protected readonly ocupado = signal<number | null>(null);
  protected readonly titulo = computed(() => new Date().toLocaleDateString('es-ES', { weekday: 'long', day: 'numeric', month: 'long' }));

  constructor() {
    void this.cargar();
    // al volver a la app (en el iPhone, al sacarla del segundo plano) se pone al día
    const visible = () => { if (!document.hidden) void this.cargar(); };
    document.addEventListener('visibilitychange', visible);
    inject(DestroyRef).onDestroy(() => document.removeEventListener('visibilitychange', visible));
  }

  protected async cargar(): Promise<void> {
    if (this.cargando()) return;
    this.cargando.set(true); this.fallo.set(false);
    try { this.datos.set(await firstValueFrom(this.http.get<Resumen>('/api/hoy'))); } catch { if (this.datos() === null) this.fallo.set(true); } finally { this.cargando.set(false); }
  }

  protected async hecho(h: Pendiente): Promise<void> {
    if (this.ocupado() !== null) return;
    this.ocupado.set(h.id);
    try { await firstValueFrom(this.http.put(`/api/habitos/${h.id}/marcar`, {})); this.cargando.set(false); await this.cargar(); } catch { /* se reintenta al actualizar */ } finally { this.ocupado.set(null); }
  }
}
