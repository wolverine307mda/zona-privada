import { HttpClient } from '@angular/common/http';
import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { EstadoError, Esqueleto } from '../ui';

interface Llamada {
  id: number; fecha: string; metodo: string; ruta: string; consulta: string; origen: string; ip: string;
  estado: number; duracionMs: number; error: string; dispositivo: string; lugar: string | null;
}
interface Lista { origenes: string[]; coinciden: number; total: number; fallos: number; llamadas: Llamada[]; }
interface Detalle { id: number; peticion: string; respuesta: string; error: string; agente: string; }
interface Viva { seq: number; fecha: string; metodo: string; ruta: string; origen: string; estado: number; duracionMs: number; error: string; guardada: boolean; }

type Periodo = 'hoy' | '7d' | 'todo';
type Estado = 'todas' | 'fallos' | 'ok';

// Las llamadas a la API que no vienen de la app (atajos del iPhone, agente del PC…): llevan un token en vez de la sesión.
// «Guardadas» es el historial (todo lo que falla y lo que no es del agente); «En vivo» solo enseña lo que llega mientras la tienes abierta
// y no se guarda: al salir y volver empieza vacía.
@Component({
  selector: 'app-llamadas',
  imports: [Esqueleto, EstadoError],
  styleUrls: ['../gastos/comun.css'],
  template: `
    <section class="caja">
      <header class="caja__cab">
        <h3>Llamadas externas</h3>
        <span class="sub">Lo que entra con un token y no desde la app</span>
      </header>

      <div class="llam__modo" role="tablist" aria-label="Qué llamadas ver">
        <button type="button" role="tab" [attr.aria-selected]="modo() === 'guardadas'" (click)="modo.set('guardadas')">Guardadas</button>
        <button type="button" role="tab" [attr.aria-selected]="modo() === 'vivo'" (click)="abrirVivo()">En vivo <span class="llam__punto" aria-hidden="true"></span></button>
      </div>

      @if (modo() === 'guardadas') {
        @if (fallo()) { <app-estado-error titulo="No pude leer las llamadas" (reintentar)="cargar()" /> }
        @else if (lista() === null) { <app-esqueleto forma="linea" /><app-esqueleto forma="linea" /> }
        @else {
          @let l = lista()!;
          <div class="llam__cifras">
            <span><b>{{ l.total }}</b> guardadas</span>
            <span [class.llam__cifra--mal]="l.fallos > 0"><b>{{ l.fallos }}</b> con fallo</span>
          </div>

          <div class="llam__filtros">
            <div class="llam__grupo" role="group" aria-label="Periodo">
              @for (p of periodos; track p.id) {
                <button type="button" [class.activo]="periodo() === p.id" (click)="ponerPeriodo(p.id)">{{ p.nombre }}</button>
              }
            </div>
            <div class="llam__grupo" role="group" aria-label="Resultado">
              @for (e of estados; track e.id) {
                <button type="button" [class.activo]="estado() === e.id" (click)="ponerEstado(e.id)">{{ e.nombre }}</button>
              }
            </div>
          </div>
          <input class="llam__buscar" type="search" placeholder="Buscar por ruta, origen o error" [value]="buscar()" (input)="escribir($any($event.target).value)" aria-label="Buscar en las llamadas">
          @if (l.origenes.length > 1) {
            <div class="llam__origenes" role="group" aria-label="Origen">
              <button type="button" [class.activo]="origen() === ''" (click)="ponerOrigen('')">Todos</button>
              @for (o of l.origenes; track o) { <button type="button" [class.activo]="origen() === o" (click)="ponerOrigen(o)">{{ o }}</button> }
            </div>
          }

          <div class="llam__barra">
            <span class="sub">{{ l.coinciden === l.llamadas.length ? l.coinciden : 'Las ' + l.llamadas.length + ' últimas de ' + l.coinciden }} {{ l.coinciden === 1 ? 'llamada' : 'llamadas' }}</span>
            @if (l.total > 0) {
              <button class="enlace" type="button" (click)="limpiando.set(!limpiando())" [attr.aria-expanded]="limpiando()">{{ limpiando() ? 'Cerrar' : 'Limpiar…' }}</button>
            }
          </div>
          @if (limpiando()) {
            <div class="llam__limpiar">
              @if (hayFiltro()) { <button type="button" (click)="limpiar('lo-visto')">Borrar lo que se ve ({{ l.coinciden }})</button> }
              <button type="button" (click)="limpiar('antes-de-hoy')">Borrar lo anterior a hoy</button>
              <button type="button" (click)="limpiar('buenas')">Borrar las buenas</button>
              <button type="button" class="llam__peligro" (click)="limpiar('todo')">Borrar todo</button>
            </div>
          }

          <ul class="llam__lista">
            @for (c of l.llamadas; track c.id) {
              <li>
                <button type="button" class="llam__fila" (click)="abrir(c)" [attr.aria-expanded]="abierta() === c.id">
                  <span class="llam__estado" [class.mal]="c.estado >= 400" [class.aviso]="c.estado >= 300 && c.estado < 400">{{ c.estado }}</span>
                  <span class="llam__texto">
                    <strong><span class="llam__metodo">{{ c.metodo }}</span> {{ c.ruta }}{{ c.consulta }}</strong>
                    <span>{{ c.origen }} · {{ c.lugar ? c.lugar + ' · ' : '' }}{{ c.ip }} · {{ c.duracionMs }} ms</span>
                    @if (c.error) { <span class="llam__error">{{ c.error }}</span> }
                  </span>
                  <time>{{ hora(c.fecha) }}</time>
                </button>
                @if (abierta() === c.id) {
                  <div class="llam__detalle">
                    @if (detalle(); as d) {
                      @if (d.error) { <p class="llam__error"><b>Error:</b> {{ d.error }}</p> }
                      <p><b>Petición</b></p><pre>{{ d.peticion || '(sin cuerpo)' }}</pre>
                      <p><b>Respuesta</b></p><pre>{{ d.respuesta || '(vacía)' }}</pre>
                      <p class="llam__ua">{{ d.agente || 'sin User-Agent' }}</p>
                    } @else { <app-esqueleto forma="linea" /> }
                  </div>
                }
              </li>
            } @empty { <p class="nada">{{ hayFiltro() ? 'Ninguna llamada coincide con esos filtros.' : 'Aún no ha entrado ninguna llamada externa.' }}</p> }
          </ul>
        }
      } @else {
        <p class="nada llam__nota">Se ve lo que llega mientras tienes esta pantalla abierta. No se guarda: al salir y volver empieza vacía.</p>
        <ul class="llam__lista">
          @for (v of vivas(); track v.seq) {
            <li>
              <div class="llam__fila">
                <span class="llam__estado" [class.mal]="v.estado >= 400" [class.aviso]="v.estado >= 300 && v.estado < 400">{{ v.estado }}</span>
                <span class="llam__texto">
                  <strong><span class="llam__metodo">{{ v.metodo }}</span> {{ v.ruta }}</strong>
                  <span>{{ v.origen }} · {{ v.duracionMs }} ms · {{ v.guardada ? 'se guarda' : 'no se guarda' }}</span>
                  @if (v.error) { <span class="llam__error">{{ v.error }}</span> }
                </span>
                <time>{{ hora(v.fecha) }}</time>
              </div>
            </li>
          } @empty { <p class="nada">Esperando llamadas…</p> }
        </ul>
      }
    </section>
  `,
  styles: `
    .llam__modo { display: inline-flex; padding: 3px; gap: 2px; border: 1px solid var(--linea); border-radius: 999px; margin-bottom: 14px; }
    .llam__modo button { min-height: 38px; padding: 0 16px; border-radius: 999px; border: 0; background: transparent; color: var(--soft); font: inherit; cursor: pointer; }
    .llam__modo button[aria-selected='true'] { background: color-mix(in srgb, var(--ok) 18%, transparent); color: inherit; }
    .llam__punto { display: inline-block; width: 8px; height: 8px; border-radius: 50%; background: var(--ok); margin-left: 4px; animation: llam-late 1.6s ease-in-out infinite; }
    @keyframes llam-late { 50% { opacity: .3; } }
    @media (prefers-reduced-motion: reduce) { .llam__punto { animation: none; } }

    .llam__cifras { display: flex; gap: 16px; margin-bottom: 10px; font-size: .85rem; color: var(--soft); }
    .llam__cifras b { color: inherit; font-size: 1.05rem; margin-right: 2px; }
    .llam__cifra--mal b { color: var(--bad); }

    .llam__filtros { display: flex; flex-wrap: wrap; gap: 8px; margin-bottom: 8px; }
    .llam__grupo { display: inline-flex; border: 1px solid var(--linea); border-radius: 10px; overflow: hidden; }
    .llam__grupo button { min-height: 38px; padding: 0 14px; border: 0; border-right: 1px solid var(--linea); background: transparent; color: var(--soft); font: inherit; font-size: .85rem; cursor: pointer; }
    .llam__grupo button:last-child { border-right: 0; }
    .llam__grupo button.activo { background: color-mix(in srgb, var(--ok) 16%, transparent); color: inherit; font-weight: 600; }
    .llam__buscar { width: 100%; min-height: 40px; margin-bottom: 8px; box-sizing: border-box; }
    .llam__origenes { display: flex; gap: 6px; overflow-x: auto; padding-bottom: 6px; margin-bottom: 4px; scrollbar-width: thin; }
    .llam__origenes button { flex: none; min-height: 34px; padding: 0 12px; border-radius: 999px; border: 1px solid var(--linea); background: transparent; color: var(--soft); font: inherit; font-size: .8rem; cursor: pointer; white-space: nowrap; }
    .llam__origenes button.activo { border-color: var(--ok); color: inherit; background: color-mix(in srgb, var(--ok) 14%, transparent); }

    .llam__barra { display: flex; align-items: center; justify-content: space-between; padding: 8px 0; border-top: 1px solid var(--linea); }
    .llam__limpiar { display: flex; flex-wrap: wrap; gap: 8px; padding: 10px; margin-bottom: 6px; border: 1px dashed var(--linea); border-radius: 10px; }
    .llam__limpiar button { min-height: 38px; padding: 0 12px; border-radius: 8px; border: 1px solid var(--linea); background: transparent; color: inherit; font: inherit; font-size: .85rem; cursor: pointer; }
    .llam__limpiar .llam__peligro { border-color: var(--bad); color: var(--bad); }

    .llam__nota { margin: 0 0 8px; }
    .llam__lista { list-style: none; margin: 0; padding: 0 4px 0 0; max-height: 460px; overflow-y: auto; overscroll-behavior: contain; }
    .llam__lista li { border-bottom: 1px solid var(--linea); }
    .llam__lista li:last-child { border-bottom: 0; }
    .llam__fila { display: grid; grid-template-columns: 46px minmax(0, 1fr) auto; align-items: center; gap: 12px; width: 100%; padding: 9px 4px; background: transparent; border: 0; color: inherit; font: inherit; text-align: left; }
    button.llam__fila { cursor: pointer; }
    .llam__estado { display: grid; place-items: center; height: 28px; border-radius: 8px; font-size: 12px; font-weight: 700; background: color-mix(in srgb, var(--ok) 20%, transparent); color: var(--ok); }
    .llam__estado.aviso { background: color-mix(in srgb, var(--warn) 22%, transparent); color: var(--warn); }
    .llam__estado.mal { background: color-mix(in srgb, var(--bad) 20%, transparent); color: var(--bad); }
    .llam__texto { display: flex; flex-direction: column; gap: 2px; min-width: 0; }
    .llam__texto strong { font-weight: 500; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .llam__metodo { font-family: var(--font-code); font-size: .72rem; color: var(--soft); border: 1px solid var(--linea); border-radius: 4px; padding: 0 4px; margin-right: 2px; }
    .llam__texto > span { font-size: 12px; color: var(--soft); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .llam__texto > .llam__error, .llam__detalle .llam__error { color: var(--bad); white-space: normal; }
    .llam__fila time { font-size: 12px; color: var(--soft); white-space: nowrap; }
    .llam__detalle { padding: 4px 4px 12px 58px; font-size: .85rem; }
    .llam__detalle p { margin: 6px 0 2px; }
    .llam__detalle pre { margin: 0; padding: 8px 10px; border: 1px solid var(--linea); border-radius: 8px; max-height: 200px; overflow: auto; white-space: pre-wrap; word-break: break-word; font-family: var(--font-code); font-size: .78rem; }
    .llam__ua { color: var(--soft); font-size: .75rem; word-break: break-all; }
    @media (max-width: 600px) {
      .llam__detalle { padding-left: 4px; }
      .llam__fila { grid-template-columns: 42px minmax(0, 1fr); }
      .llam__fila time { grid-column: 2; }
      .llam__grupo { flex: 1 1 100%; } .llam__grupo button { flex: 1; padding: 0 6px; }
    }
  `,
})
export class Llamadas implements OnInit {
  private readonly http = inject(HttpClient);
  protected readonly periodos: { id: Periodo; nombre: string }[] = [{ id: 'hoy', nombre: 'Hoy' }, { id: '7d', nombre: '7 días' }, { id: 'todo', nombre: 'Todo' }];
  protected readonly estados: { id: Estado; nombre: string }[] = [{ id: 'todas', nombre: 'Todas' }, { id: 'fallos', nombre: 'Con fallo' }, { id: 'ok', nombre: 'Buenas' }];

  protected readonly modo = signal<'guardadas' | 'vivo'>('guardadas');
  protected readonly lista = signal<Lista | null>(null);
  protected readonly fallo = signal(false);
  protected readonly periodo = signal<Periodo>('todo');
  protected readonly estado = signal<Estado>('todas');
  protected readonly origen = signal('');
  protected readonly buscar = signal('');
  protected readonly limpiando = signal(false);
  protected readonly abierta = signal<number | null>(null);
  protected readonly detalle = signal<Detalle | null>(null);

  // en vivo: solo en esta pantalla, nada se guarda
  protected readonly vivas = signal<Viva[]>([]);
  private ultimo: number | null = null;
  private espera: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    const t = setInterval(() => { if (!document.hidden && this.modo() === 'vivo') void this.sondear(); }, 2500);
    inject(DestroyRef).onDestroy(() => { clearInterval(t); clearTimeout(this.espera); });
  }

  ngOnInit(): void { void this.cargar(); }

  protected hayFiltro(): boolean { return this.periodo() !== 'todo' || this.estado() !== 'todas' || this.origen() !== '' || this.buscar().trim() !== ''; }

  // medianoche de hoy (hora local) como fecha ISO; 7 días = desde hace una semana
  private desde(): string | null {
    const p = this.periodo();
    if (p === 'todo') return null;
    const d = new Date();
    d.setHours(0, 0, 0, 0);
    if (p === '7d') d.setDate(d.getDate() - 6);
    return d.toISOString();
  }

  private filtros(): URLSearchParams {
    const q = new URLSearchParams();
    if (this.estado() !== 'todas') q.set('estado', this.estado());
    if (this.origen()) q.set('origen', this.origen());
    if (this.buscar().trim()) q.set('buscar', this.buscar().trim());
    const d = this.desde();
    if (d) q.set('desde', d);
    return q;
  }

  protected async cargar(): Promise<void> {
    this.fallo.set(false);
    try { this.lista.set(await firstValueFrom(this.http.get<Lista>('/api/seguridad/llamadas?' + this.filtros()))); } catch { this.fallo.set(true); }
  }

  protected ponerPeriodo(v: Periodo): void { this.periodo.set(v); void this.cargar(); }
  protected ponerEstado(v: Estado): void { this.estado.set(v); void this.cargar(); }
  protected ponerOrigen(v: string): void { this.origen.set(v); void this.cargar(); }
  protected escribir(v: string): void {
    this.buscar.set(v);
    clearTimeout(this.espera);
    this.espera = setTimeout(() => void this.cargar(), 300);
  }

  protected async abrir(l: Llamada): Promise<void> {
    if (this.abierta() === l.id) { this.abierta.set(null); return; }
    this.abierta.set(l.id);
    this.detalle.set(null);
    try { const d = await firstValueFrom(this.http.get<Detalle>(`/api/seguridad/llamadas/${l.id}`)); if (this.abierta() === l.id) this.detalle.set(d); } catch { this.abierta.set(null); }
  }

  protected async limpiar(que: 'lo-visto' | 'antes-de-hoy' | 'buenas' | 'todo'): Promise<void> {
    const q = new URLSearchParams();
    let aviso = '¿Borrar todo el historial de llamadas externas?';
    if (que === 'lo-visto') { for (const [k, v] of this.filtros()) q.set(k, v); aviso = `¿Borrar las ${this.lista()?.coinciden ?? ''} llamadas que se ven con estos filtros?`; }
    else if (que === 'buenas') { q.set('estado', 'ok'); aviso = '¿Borrar las llamadas buenas? Se quedan las que fallaron.'; }
    else if (que === 'antes-de-hoy') { const d = new Date(); d.setHours(0, 0, 0, 0); q.set('antesDe', d.toISOString()); aviso = '¿Borrar todo lo anterior a hoy?'; }
    if (!confirm(aviso)) return;
    await firstValueFrom(this.http.delete('/api/seguridad/llamadas?' + q));
    this.abierta.set(null);
    this.limpiando.set(false);
    await this.cargar();
  }

  protected async abrirVivo(): Promise<void> {
    this.modo.set('vivo');
    // al abrir se apunta por dónde va la cuenta: lo anterior no se enseña
    if (this.ultimo === null) {
      try { this.ultimo = (await firstValueFrom(this.http.get<{ ultimo: number }>('/api/seguridad/llamadas/vivo'))).ultimo; } catch { /* se reintenta al sondear */ }
    }
  }

  private async sondear(): Promise<void> {
    if (this.ultimo === null) { await this.abrirVivo(); return; }
    try {
      const r = await firstValueFrom(this.http.get<{ ultimo: number; llamadas: Viva[] }>(`/api/seguridad/llamadas/vivo?desde=${this.ultimo}`));
      this.ultimo = r.ultimo;
      if (r.llamadas.length) this.vivas.update((v) => [...r.llamadas, ...v].slice(0, 200));
      if (r.llamadas.some((l) => l.guardada)) void this.cargar();
    } catch { /* sin red un momento */ }
  }

  protected hora(iso: string): string {
    const f = new Date(iso);
    const hoy = new Date().toDateString() === f.toDateString();
    return hoy ? f.toLocaleTimeString('es-ES', { hour: '2-digit', minute: '2-digit', second: '2-digit' })
      : f.toLocaleString('es-ES', { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' });
  }
}
