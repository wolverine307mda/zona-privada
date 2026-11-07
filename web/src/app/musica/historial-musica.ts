import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

interface Top { cancionId: string; nombre: string; artistas: string; imagen: string | null; veces: number; }
interface Historial {
  periodo: string; hayDatos: boolean; desdeFecha: string | null; minutos: number; canciones: number; topCanciones: Top[]; topArtistas: { nombre: string; veces: number }[];
  horas: number[]; racha: number; artistasNuevos: string[]; cancionDeLaSemana: Top | null;
}

const PERIODOS = [{ id: 'semana', nombre: '7 días' }, { id: 'mes', nombre: 'Este mes' }, { id: 'anio', nombre: 'Este año' }, { id: 'todo', nombre: 'Todo' }];

// «Lo que escuchas»: tu propio historial (Spotify solo recuerda las últimas 50 canciones, así que la app va guardando lo que suena cada 20 minutos).
// Muestra el tiempo escuchado, lo más repetido, a qué horas escuchas, tu racha y los artistas que has descubierto este periodo.
@Component({
  selector: 'app-historial-musica',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrls: ['../gastos/comun.css'],
  template: `
    <section class="caja hist">
      <header class="caja__cab"><h3>Lo que escuchas</h3>
        <div class="selector" role="group" aria-label="Periodo">
          @for (p of periodos; track p.id) { <button type="button" [attr.aria-pressed]="periodo() === p.id" (click)="cambiar(p.id)">{{ p.nombre }}</button> }
        </div>
      </header>
      @if (fallo()) { <p class="sub">No se pudo leer el historial.</p> }
      @else if (h(); as d) {
        @if (!d.hayDatos) {
          <p class="sub">Todavía no hay historial propio: la app guarda lo que suena cada 20 minutos y aquí irán apareciendo las cuentas. Mientras tanto, «Lo más escuchado» de arriba viene de Spotify.</p>
        } @else {
          <div class="cifras">
            <div><span class="etq">Tiempo escuchado</span><strong>{{ tiempo(d.minutos) }}</strong></div>
            <div><span class="etq">Canciones</span><strong>{{ d.canciones }}</strong></div>
            <div><span class="etq">Racha</span><strong>{{ d.racha }} {{ d.racha === 1 ? 'día' : 'días' }}</strong></div>
          </div>
          @if (d.cancionDeLaSemana; as c) { <p class="semana">🎵 Canción de la semana: <b>{{ c.nombre }}</b> · {{ c.artistas }} ({{ c.veces }} {{ c.veces === 1 ? 'vez' : 'veces' }})</p> }
          <h4>A qué horas</h4>
          <div class="horas" role="img" aria-label="Escuchas por hora del día">
            @for (n of d.horas; track $index) { <i [style.height.%]="altura(n)" [title]="$index + ' h: ' + n"></i> }
          </div>
          <div class="horas__eje" aria-hidden="true"><span>0 h</span><span>6 h</span><span>12 h</span><span>18 h</span><span>23 h</span></div>
          @if (d.topCanciones.length) {
            <h4>Las más repetidas</h4>
            <ol class="top">
              @for (t of d.topCanciones; track t.cancionId) {
                <li>@if (t.imagen) { <img [src]="t.imagen" alt="" width="40" height="40" loading="lazy"> } @else { <span class="img"></span> }
                  <span class="top__texto"><strong>{{ t.nombre }}</strong><small>{{ t.artistas }}</small></span><span class="veces">{{ t.veces }}×</span></li>
              }
            </ol>
          }
          @if (d.topArtistas.length) { <p class="sub"><b>Artistas:</b> {{ artistas() }}</p> }
          @if (d.artistasNuevos.length) { <p class="sub"><b>Descubiertos:</b> {{ d.artistasNuevos.join(', ') }}</p> }
          <p class="sub pie">Historial desde el {{ fecha(d.desdeFecha) }}. <button class="enlace enlace--mal" type="button" (click)="borrar()">{{ confirmar() ? 'Seguro: borrar todo' : 'Borrar el historial' }}</button></p>
        }
      } @else { <p class="sub" aria-busy="true">Leyendo…</p> }
    </section>
  `,
  styles: `
    .hist { display: flex; flex-direction: column; gap: 10px; }
    .selector { display: inline-flex; flex-wrap: wrap; gap: 4px; }
    .selector button { border: 1px solid var(--vidrio-borde); background: none; color: var(--soft); border-radius: 999px; padding: 6px 12px; font: 500 .78rem/1 var(--font-text); min-height: 36px; cursor: pointer; }
    .selector button[aria-pressed='true'] { background: var(--brand); border-color: var(--brand); color: var(--on-brand); }
    .sub { margin: 0; color: var(--soft); font-size: .85rem; line-height: 1.5; }
    .sub b { color: var(--texto); font-weight: 600; }
    .cifras { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 12px; }
    .cifras div { display: flex; flex-direction: column; gap: 2px; }
    .cifras strong { font: 600 1.3rem/1.2 var(--font-display, var(--font-text)); }
    .etq { color: var(--soft); font-size: .72rem; text-transform: uppercase; letter-spacing: .06em; }
    .semana { margin: 0; padding: 8px 12px; border-radius: 12px; background: var(--tarjeta-2); font-size: .88rem; }
    h4 { margin: 6px 0 0; font: 500 .9rem/1.2 var(--font-text); }
    .horas { display: grid; grid-template-columns: repeat(24, minmax(0, 1fr)); gap: 3px; align-items: end; height: 70px; }
    .horas i { display: block; min-height: 3px; background: var(--brand); border-radius: 3px 3px 0 0; opacity: .85; }
    .horas__eje { display: flex; justify-content: space-between; color: var(--soft); font-size: .7rem; }
    .top { list-style: none; margin: 0; padding: 0; display: grid; gap: 6px; }
    .top li { display: grid; grid-template-columns: 40px minmax(0, 1fr) auto; gap: 10px; align-items: center; }
    .top img, .top .img { width: 40px; height: 40px; border-radius: 8px; object-fit: cover; background: var(--tarjeta-2); }
    .top__texto { display: grid; min-width: 0; }
    .top__texto strong, .top__texto small { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .top__texto small { color: var(--soft); }
    .veces { font-family: var(--font-code); color: var(--soft); }
    .pie { font-size: .78rem; }
    .enlace { background: none; border: 0; color: var(--brand); font: inherit; cursor: pointer; }
    .enlace--mal { color: var(--bad, #ff5a5a); }
  `,
})
export class HistorialMusica {
  private readonly http = inject(HttpClient);
  protected readonly periodos = PERIODOS;
  protected readonly periodo = signal('mes');
  protected readonly h = signal<Historial | null>(null);
  protected readonly fallo = signal(false);
  protected readonly confirmar = signal(false);
  protected readonly maximo = computed(() => Math.max(1, ...(this.h()?.horas ?? [1])));

  constructor() { void this.cargar(); }

  protected cambiar(p: string): void { this.periodo.set(p); void this.cargar(); }

  private async cargar(): Promise<void> {
    this.fallo.set(false);
    try { this.h.set(await firstValueFrom(this.http.get<Historial>(`/api/musica/historial?periodo=${this.periodo()}`))); } catch { this.fallo.set(true); }
  }

  protected altura(n: number): number { return n === 0 ? 4 : Math.max(8, Math.round(100 * n / this.maximo())); }
  protected tiempo(min: number): string { return min >= 120 ? `${Math.round(min / 60)} h` : `${min} min`; }
  protected artistas(): string { return this.h()!.topArtistas.slice(0, 6).map((a) => `${a.nombre} (${a.veces})`).join(', '); }
  protected fecha(iso: string | null): string { return iso ? new Date(iso).toLocaleDateString('es-ES', { day: 'numeric', month: 'long', year: 'numeric' }) : ''; }

  protected async borrar(): Promise<void> {
    if (!this.confirmar()) { this.confirmar.set(true); return; }
    this.confirmar.set(false);
    try { await firstValueFrom(this.http.delete('/api/musica/historial')); await this.cargar(); } catch { /* se queda como estaba */ }
  }
}
