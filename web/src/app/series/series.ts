import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import { firstValueFrom } from 'rxjs';

interface Titulo {
  id: number; tipo: 'serie' | 'pelicula'; nombre: string; imagen: string | null; estado: 'por-ver' | 'viendo' | 'vista'; temporada: number; episodio: number; nota: number | null; plataforma: string;
  generos: string[]; minutos: number; terminada: boolean; proximo: string | null; dias: number | null; proximoCodigo: string | null; visto: string | null;
}
interface Ficha { id: number; nombre: string; imagen: string | null; plataforma: string; generos: string[]; minutos: number; terminada: boolean; proximo: { fecha: string; temporada: number; episodio: number } | null; }
interface Estadisticas { anio: number; vistasEsteAnio: number; horas: number; generos: { genero: string; veces: number }[]; notaMedia: number | null; viendo: number; porVer: number; }

const COLUMNAS: { id: Titulo['estado']; nombre: string }[] = [{ id: 'por-ver', nombre: 'Por ver' }, { id: 'viendo', nombre: 'Viendo' }, { id: 'vista', nombre: 'Vistas' }];

// Series y películas: tres columnas (por ver · viendo · vistas), el siguiente episodio con su fecha, «vi uno» con un toque y las estadísticas del año.
// Las series se buscan en TVMaze (el próximo estreno se actualiza solo cada día y te aviso la víspera y el mismo día); las películas se apuntan a mano.
@Component({
  selector: 'app-series',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrls: ['../gastos/comun.css', '../gastos/dialogos.css'],
  template: `
    <header class="cab">
      <p class="cab__sub" aria-live="polite">{{ resumen() }}</p>
      <div class="cab__acc"><button class="button button--ghost" type="button" (click)="abrirPelicula()">Apuntar película</button><button class="button" type="button" (click)="abrirBuscar()">Buscar serie</button></div>
    </header>
    @if (aviso()) { <p class="aviso" role="status">{{ aviso() }}</p> }

    @if (est(); as e) {
      <section class="caja stats"><div><span class="etq">Vistas en {{ e.anio }}</span><strong>{{ e.vistasEsteAnio }}</strong></div><div><span class="etq">Horas</span><strong>{{ e.horas }}</strong></div>
        <div><span class="etq">Nota media</span><strong>{{ e.notaMedia ?? '—' }}</strong></div><div><span class="etq">Géneros</span><strong class="chico">{{ generos(e) }}</strong></div></section>
    }

    <div class="columnas">
      @for (c of columnas; track c.id) {
        <section class="col" [attr.aria-label]="c.nombre">
          <h3>{{ c.nombre }} <small>{{ de(c.id).length }}</small></h3>
          @for (t of de(c.id); track t.id) {
            <article class="titulo">
              @if (t.imagen) { <img [src]="t.imagen" alt="" width="52" height="74" loading="lazy"> } @else { <span class="titulo__img" aria-hidden="true">{{ t.tipo === 'pelicula' ? '🎬' : '📺' }}</span> }
              <div class="titulo__cuerpo">
                <strong>{{ t.nombre }}</strong>
                <small>{{ t.plataforma || (t.tipo === 'pelicula' ? 'Película' : 'Serie') }}{{ t.visto ? ' · visto ' + t.visto : '' }}{{ t.nota ? ' · ' + t.nota + '/10' : '' }}</small>
                @if (t.proximo) { <span class="prox" [class.prox--hoy]="t.dias === 0">{{ t.proximoCodigo && t.tipo === 'serie' ? t.proximoCodigo + ' · ' : 'Estreno · ' }}{{ cuando(t) }}</span> }
                @else if (t.terminada && t.tipo === 'serie') { <span class="prox prox--fin">terminada</span> }
                <span class="acc">
                  @if (t.tipo === 'serie' && t.estado !== 'vista') { <button type="button" class="enlace" (click)="siguiente(t)">Vi uno</button> }
                  @if (t.estado === 'por-ver') { <button type="button" class="enlace" (click)="estado(t, 'viendo')">Empezar</button> }
                  @if (t.estado !== 'vista') { <button type="button" class="enlace" (click)="estado(t, 'vista')">Vista</button> }
                  @if (t.estado === 'vista' && !t.nota) { <button type="button" class="enlace" (click)="valorar(t)">Valorar</button> }
                  <button type="button" class="enlace enlace--mal" (click)="quitar(t)">{{ confirmar() === t.id ? 'Seguro' : 'Quitar' }}</button>
                </span>
              </div>
            </article>
          } @empty { <p class="sub">Nada aquí.</p> }
        </section>
      }
    </div>

    <dialog class="dlg" #dlgBuscar (click)="fuera($event, dlgBuscar)" aria-labelledby="dlg-bus">
      <form class="dlg__caja" (submit)="buscar($event)" novalidate>
        <header class="dlg__cabeza"><h2 id="dlg-bus">Buscar una serie</h2><button class="dlg__x" type="button" (click)="dlgBuscar.close()" aria-label="Cerrar"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6l12 12M18 6 6 18" /></svg></button></header>
        <div class="fila2"><input class="campo__input" type="search" placeholder="Nombre de la serie" [value]="q()" (input)="q.set($any($event.target).value)"><button class="button" type="submit" [disabled]="ocupado()">Buscar</button></div>
        @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
        <ul class="res">
          @for (f of resultados(); track f.id) {
            <li>@if (f.imagen) { <img [src]="f.imagen" alt="" width="42" height="60" loading="lazy"> } <span><strong>{{ f.nombre }}</strong><small>{{ f.plataforma }}{{ f.generos.length ? ' · ' + f.generos.slice(0, 2).join(', ') : '' }}{{ f.terminada ? ' · terminada' : '' }}{{ f.proximo ? ' · próximo ' + f.proximo.fecha : '' }}</small></span>
              <button class="button button--ghost" type="button" (click)="anadir(f)">Añadir</button></li>
          }
        </ul>
        <p class="sub pie">Datos de <a href="https://www.tvmaze.com" target="_blank" rel="noopener noreferrer">TVMaze</a>.</p>
      </form>
    </dialog>

    <dialog class="dlg" #dlgPeli (click)="fuera($event, dlgPeli)" aria-labelledby="dlg-peli">
      <form class="dlg__caja" (submit)="guardarPelicula($event)" novalidate>
        <header class="dlg__cabeza"><h2 id="dlg-peli">Apuntar una película</h2><button class="dlg__x" type="button" (click)="dlgPeli.close()" aria-label="Cerrar"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6l12 12M18 6 6 18" /></svg></button></header>
        <label class="campo"><span class="campo__etiqueta">Título</span><input class="campo__input" type="text" maxlength="120" [value]="pNombre()" (input)="pNombre.set($any($event.target).value)"></label>
        <div class="fila2">
          <label class="campo"><span class="campo__etiqueta">Estreno <em>(opcional: te aviso)</em></span><input class="campo__input" type="date" [value]="pEstreno()" (input)="pEstreno.set($any($event.target).value)"></label>
          <label class="campo"><span class="campo__etiqueta">Duración (min)</span><input class="campo__input" type="number" min="1" max="600" [value]="pMinutos()" (input)="pMinutos.set($any($event.target).value)"></label>
        </div>
        <label class="campo"><span class="campo__etiqueta">Plataforma</span><input class="campo__input" type="text" maxlength="60" [value]="pPlataforma()" (input)="pPlataforma.set($any($event.target).value)"></label>
        @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
        <footer class="dlg__pie"><button class="button" type="submit">Guardar</button></footer>
      </form>
    </dialog>
  `,
  styles: `
    .cab { display: flex; align-items: center; justify-content: space-between; gap: 12px; flex-wrap: wrap; margin-bottom: 12px; }
    .cab__sub { margin: 0; font: var(--peso-ligero, 300) 1.3rem/1.2 var(--font-text); }
    .cab__acc { display: flex; gap: 8px; flex-wrap: wrap; }
    .aviso { margin: 0 0 12px; padding: 8px 12px; border-radius: 10px; background: var(--tarjeta-2); font-size: .9rem; }
    .sub { margin: 0; color: var(--soft); font-size: .85rem; }
    .stats { display: grid; grid-template-columns: repeat(auto-fit, minmax(110px, 1fr)); gap: 12px; margin-bottom: 14px; }
    .stats div { display: flex; flex-direction: column; gap: 2px; }
    .stats strong { font: 600 1.3rem/1.2 var(--font-display, var(--font-text)); } .stats .chico { font-size: .9rem; font-weight: 500; }
    .etq { color: var(--soft); font-size: .72rem; text-transform: uppercase; letter-spacing: .06em; }
    .columnas { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 14px; align-items: start; }
    .col { display: grid; gap: 8px; } .col h3 { margin: 0; font: 600 .95rem/1.2 var(--font-text); } .col h3 small { color: var(--soft); font-weight: 400; }
    .titulo { display: grid; grid-template-columns: auto minmax(0, 1fr); gap: 10px; padding: 10px; border-radius: 14px; border: 1px solid var(--vidrio-borde); background: var(--vidrio); }
    .titulo img, .titulo__img { width: 52px; height: 74px; border-radius: 8px; object-fit: cover; background: var(--tarjeta-2); display: grid; place-items: center; font-size: 1.4rem; }
    .titulo__cuerpo { display: grid; gap: 3px; min-width: 0; align-content: start; } .titulo__cuerpo strong { overflow-wrap: anywhere; } .titulo__cuerpo small { color: var(--soft); }
    .prox { font-size: .82rem; color: var(--brand); } .prox--hoy { font-weight: 700; } .prox--fin { color: var(--soft); }
    .acc { display: flex; flex-wrap: wrap; gap: 4px 12px; }
    .enlace { background: none; border: 0; color: var(--brand); font: inherit; font-size: .85rem; cursor: pointer; min-height: 36px; padding: 0; } .enlace--mal { color: var(--bad, #ff5a5a); }
    .res { list-style: none; margin: 0; padding: 0; display: grid; gap: 8px; max-height: 46vh; overflow: auto; }
    .res li { display: grid; grid-template-columns: auto minmax(0, 1fr) auto; gap: 10px; align-items: center; } .res img { border-radius: 6px; object-fit: cover; }
    .res small { display: block; color: var(--soft); } .pie a { color: var(--brand); }
    @media (max-width: 820px) { .columnas { grid-template-columns: minmax(0, 1fr); } }
  `,
})
export class Series {
  private readonly http = inject(HttpClient);
  private readonly dlgBuscar = viewChild.required<ElementRef<HTMLDialogElement>>('dlgBuscar');
  private readonly dlgPeli = viewChild.required<ElementRef<HTMLDialogElement>>('dlgPeli');

  protected readonly columnas = COLUMNAS;
  protected readonly lista = signal<Titulo[]>([]);
  protected readonly est = signal<Estadisticas | null>(null);
  protected readonly aviso = signal('');
  protected readonly error = signal('');
  protected readonly ocupado = signal(false);
  protected readonly confirmar = signal<number | null>(null);
  protected readonly q = signal('');
  protected readonly resultados = signal<Ficha[]>([]);
  protected readonly pNombre = signal(''); protected readonly pEstreno = signal(''); protected readonly pMinutos = signal(''); protected readonly pPlataforma = signal('');
  protected readonly resumen = computed(() => {
    const l = this.lista(); const prox = l.filter((t) => t.estado !== 'vista' && t.dias !== null && t.dias >= 0).sort((a, b) => a.dias! - b.dias!)[0];
    if (l.length === 0) return 'Nada apuntado todavía';
    return prox ? `Lo siguiente: ${prox.nombre} ${prox.dias === 0 ? 'hoy' : prox.dias === 1 ? 'mañana' : `en ${prox.dias} días`}` : `${l.length} títulos`;
  });

  constructor() { void this.cargar(); }

  protected de(e: Titulo['estado']): Titulo[] { return this.lista().filter((t) => t.estado === e); }
  protected generos(e: Estadisticas): string { return e.generos.map((g) => g.genero).slice(0, 3).join(', ') || '—'; }
  protected cuando(t: Titulo): string {
    if (t.dias === null) return '';
    return t.dias === 0 ? 'hoy' : t.dias === 1 ? 'mañana' : t.dias < 0 ? `hace ${-t.dias} días` : `en ${t.dias} días (${new Date(`${t.proximo}T12:00:00`).toLocaleDateString('es-ES', { day: 'numeric', month: 'short' })})`;
  }

  private async cargar(): Promise<void> {
    try { const [l, e] = await Promise.all([firstValueFrom(this.http.get<Titulo[]>('/api/series')), firstValueFrom(this.http.get<Estadisticas>('/api/series/estadisticas'))]); this.lista.set(l); this.est.set(e); } catch { this.aviso.set('No se pudo cargar.'); }
  }

  protected async siguiente(t: Titulo): Promise<void> { try { await firstValueFrom(this.http.post(`/api/series/${t.id}/siguiente`, null)); await this.cargar(); } catch { this.aviso.set('No se pudo apuntar.'); } }
  protected async estado(t: Titulo, estado: string): Promise<void> { try { await firstValueFrom(this.http.put(`/api/series/${t.id}`, { estado })); await this.cargar(); } catch { this.aviso.set('No se pudo cambiar.'); } }
  protected async valorar(t: Titulo): Promise<void> {
    const n = Number(prompt(`¿Qué nota le pones a «${t.nombre}»? (1–10)`) ?? '');
    if (!Number.isInteger(n) || n < 1 || n > 10) return;
    try { await firstValueFrom(this.http.put(`/api/series/${t.id}`, { nota: n })); await this.cargar(); } catch { this.aviso.set('No se pudo valorar.'); }
  }
  protected async quitar(t: Titulo): Promise<void> {
    if (this.confirmar() !== t.id) { this.confirmar.set(t.id); return; }
    this.confirmar.set(null);
    try { await firstValueFrom(this.http.delete(`/api/series/${t.id}`)); await this.cargar(); } catch { this.aviso.set('No se pudo quitar.'); }
  }

  protected abrirBuscar(): void { this.q.set(''); this.resultados.set([]); this.error.set(''); this.dlgBuscar().nativeElement.showModal(); }
  protected async buscar(e: Event): Promise<void> {
    e.preventDefault(); this.error.set(''); this.ocupado.set(true);
    try { const r = await firstValueFrom(this.http.get<Ficha[]>(`/api/series/buscar?q=${encodeURIComponent(this.q())}`)); this.resultados.set(r); if (r.length === 0) this.error.set('No he encontrado nada (o TVMaze no responde).'); }
    catch { this.error.set('Escribe qué buscar.'); } finally { this.ocupado.set(false); }
  }
  protected async anadir(f: Ficha): Promise<void> {
    try { await firstValueFrom(this.http.post('/api/series', { tipo: 'serie', idExterno: f.id, estado: 'por-ver' })); this.dlgBuscar().nativeElement.close(); this.aviso.set(`«${f.nombre}» añadida.`); await this.cargar(); }
    catch (x) { this.error.set((x as HttpErrorResponse).error?.detalle ?? 'No se pudo añadir.'); }
  }

  protected abrirPelicula(): void { this.pNombre.set(''); this.pEstreno.set(''); this.pMinutos.set(''); this.pPlataforma.set(''); this.error.set(''); this.dlgPeli().nativeElement.showModal(); }
  protected async guardarPelicula(e: Event): Promise<void> {
    e.preventDefault();
    if (!this.pNombre().trim()) { this.error.set('Ponle un título.'); return; }
    try { await firstValueFrom(this.http.post('/api/series', { tipo: 'pelicula', nombre: this.pNombre().trim(), estreno: this.pEstreno() || undefined, minutos: this.pMinutos() || undefined, plataforma: this.pPlataforma() })); this.dlgPeli().nativeElement.close(); await this.cargar(); }
    catch { this.error.set('No se pudo guardar.'); }
  }

  protected fuera(e: MouseEvent, d: HTMLDialogElement): void { if (e.target === d) d.close(); }
}
