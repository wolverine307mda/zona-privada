import { HttpClient } from '@angular/common/http';
import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { EstadoError, Esqueleto } from '../ui';

interface Llamada {
  id: number; fecha: string; metodo: string; ruta: string; consulta: string; origen: string; ip: string;
  estado: number; duracionMs: number; error: string; dispositivo: string; lugar: string | null;
}
interface Lista { origenes: string[]; total: number; fallos: number; llamadas: Llamada[]; }
interface Detalle { id: number; peticion: string; respuesta: string; error: string; agente: string; }
interface Viva { seq: number; fecha: string; metodo: string; ruta: string; origen: string; estado: number; duracionMs: number; error: string; guardada: boolean; }

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
        <span class="sub">Atajos, agente del PC… lo que entra con un token y no desde la app</span>
      </header>

      <div class="llam__modo" role="tablist" aria-label="Qué llamadas ver">
        <button type="button" role="tab" [attr.aria-selected]="modo() === 'guardadas'" (click)="modo.set('guardadas')">
          Guardadas @if (lista(); as l) { <small>{{ l.total }}@if (l.fallos > 0) { · {{ l.fallos }} con fallo }</small> }
        </button>
        <button type="button" role="tab" [attr.aria-selected]="modo() === 'vivo'" (click)="abrirVivo()">En vivo <span class="llam__punto" aria-hidden="true"></span></button>
      </div>

      @if (modo() === 'guardadas') {
        @if (fallo()) { <app-estado-error titulo="No pude leer las llamadas" (reintentar)="cargar()" /> }
        @else if (lista() === null) { <app-esqueleto forma="linea" /><app-esqueleto forma="linea" /> }
        @else {
          <div class="llam__filtros">
            <button type="button" class="llam__chip" [class.activo]="!soloErrores()" (click)="ponerErrores(false)">Todas</button>
            <button type="button" class="llam__chip" [class.activo]="soloErrores()" (click)="ponerErrores(true)">Solo fallos</button>
            @if (lista()!.origenes.length > 1) {
              <select class="llam__origen" [value]="origen()" (change)="ponerOrigen($any($event.target).value)" aria-label="Filtrar por origen">
                <option value="">Cualquier origen</option>
                @for (o of lista()!.origenes; track o) { <option [value]="o">{{ o }}</option> }
              </select>
            }
            @if (lista()!.total > 0) { <button class="enlace llam__vaciar" type="button" (click)="vaciar()">Vaciar historial</button> }
          </div>
          <ul class="llam__lista">
            @for (l of lista()!.llamadas; track l.id) {
              <li>
                <button type="button" class="llam__fila" (click)="abrir(l)" [attr.aria-expanded]="abierta() === l.id">
                  <span class="llam__estado" [class.mal]="l.estado >= 400" [class.aviso]="l.estado >= 300 && l.estado < 400">{{ l.estado }}</span>
                  <span class="llam__texto">
                    <strong><span class="llam__metodo">{{ l.metodo }}</span> {{ l.ruta }}{{ l.consulta }}</strong>
                    <span>{{ l.origen }} · {{ l.lugar ? l.lugar + ' · ' : '' }}{{ l.ip }} · {{ l.duracionMs }} ms</span>
                    @if (l.error) { <span class="llam__error">{{ l.error }}</span> }
                  </span>
                  <time>{{ hora(l.fecha) }}</time>
                </button>
                @if (abierta() === l.id) {
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
            } @empty { <p class="nada">{{ soloErrores() ? 'Ninguna llamada ha fallado.' : 'Aún no ha entrado ninguna llamada externa.' }}</p> }
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
    .llam__modo { display: flex; gap: 6px; margin-bottom: 12px; }
    .llam__modo button { min-height: 40px; padding: 0 14px; border-radius: 999px; border: 1px solid var(--linea); background: transparent; color: inherit; font: inherit; cursor: pointer; }
    .llam__modo button[aria-selected='true'] { background: color-mix(in srgb, var(--ok) 18%, transparent); border-color: var(--ok); }
    .llam__modo small { color: var(--soft); margin-left: 4px; }
    .llam__punto { display: inline-block; width: 8px; height: 8px; border-radius: 50%; background: var(--ok); margin-left: 4px; animation: llam-late 1.6s ease-in-out infinite; }
    @keyframes llam-late { 50% { opacity: .3; } }
    @media (prefers-reduced-motion: reduce) { .llam__punto { animation: none; } }
    .llam__filtros { display: flex; flex-wrap: wrap; align-items: center; gap: 6px; margin-bottom: 8px; }
    .llam__chip { min-height: 36px; padding: 0 12px; border-radius: 999px; border: 1px solid var(--linea); background: transparent; color: inherit; font: inherit; font-size: .85rem; cursor: pointer; }
    .llam__chip.activo { border-color: var(--ok); background: color-mix(in srgb, var(--ok) 15%, transparent); }
    .llam__origen { min-height: 36px; max-width: 100%; }
    .llam__vaciar { margin-left: auto; font-size: 12px; }
    .llam__nota { margin: 0 0 8px; }
    .llam__lista { list-style: none; margin: 0; padding: 0 4px 0 0; max-height: 440px; overflow-y: auto; overscroll-behavior: contain; }
    .llam__lista li { border-bottom: 1px solid var(--linea); }
    .llam__lista li:last-child { border-bottom: 0; }
    .llam__fila { display: grid; grid-template-columns: 44px minmax(0, 1fr) auto; align-items: center; gap: 12px; width: 100%; padding: 8px 4px; background: transparent; border: 0; color: inherit; font: inherit; text-align: left; }
    button.llam__fila { cursor: pointer; }
    .llam__estado { display: grid; place-items: center; height: 28px; border-radius: 8px; font-size: 12px; font-weight: 700; background: color-mix(in srgb, var(--ok) 20%, transparent); color: var(--ok); }
    .llam__estado.aviso { background: color-mix(in srgb, var(--warn) 22%, transparent); color: var(--warn); }
    .llam__estado.mal { background: color-mix(in srgb, var(--bad) 20%, transparent); color: var(--bad); }
    .llam__texto { display: flex; flex-direction: column; gap: 2px; min-width: 0; }
    .llam__texto strong { font-weight: 500; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .llam__metodo { font-family: var(--font-code); font-size: .75rem; color: var(--soft); }
    .llam__texto > span { font-size: 12px; color: var(--soft); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .llam__texto > .llam__error, .llam__detalle .llam__error { color: var(--bad); white-space: normal; }
    .llam__fila time { font-size: 12px; color: var(--soft); white-space: nowrap; }
    .llam__detalle { padding: 4px 4px 12px 56px; font-size: .85rem; }
    .llam__detalle p { margin: 6px 0 2px; }
    .llam__detalle pre { margin: 0; padding: 8px 10px; border: 1px solid var(--linea); border-radius: 8px; max-height: 200px; overflow: auto; white-space: pre-wrap; word-break: break-word; font-family: var(--font-code); font-size: .78rem; }
    .llam__ua { color: var(--soft); font-size: .75rem; word-break: break-all; }
    @media (max-width: 600px) { .llam__detalle { padding-left: 4px; } .llam__fila { grid-template-columns: 40px minmax(0, 1fr); } .llam__fila time { grid-column: 2; } }
  `,
})
export class Llamadas implements OnInit {
  private readonly http = inject(HttpClient);
  protected readonly modo = signal<'guardadas' | 'vivo'>('guardadas');
  protected readonly lista = signal<Lista | null>(null);
  protected readonly fallo = signal(false);
  protected readonly soloErrores = signal(false);
  protected readonly origen = signal('');
  protected readonly abierta = signal<number | null>(null);
  protected readonly detalle = signal<Detalle | null>(null);

  // en vivo: solo en esta pantalla, nada se guarda
  protected readonly vivas = signal<Viva[]>([]);
  private ultimo: number | null = null;

  constructor() {
    const t = setInterval(() => { if (!document.hidden && this.modo() === 'vivo') void this.sondear(); }, 2500);
    inject(DestroyRef).onDestroy(() => clearInterval(t));
  }

  ngOnInit(): void { void this.cargar(); }

  protected async cargar(): Promise<void> {
    this.fallo.set(false);
    const q = new URLSearchParams();
    if (this.soloErrores()) q.set('errores', 'true');
    if (this.origen()) q.set('origen', this.origen());
    try { this.lista.set(await firstValueFrom(this.http.get<Lista>('/api/seguridad/llamadas?' + q))); } catch { this.fallo.set(true); }
  }

  protected ponerErrores(v: boolean): void { this.soloErrores.set(v); void this.cargar(); }
  protected ponerOrigen(v: string): void { this.origen.set(v); void this.cargar(); }

  protected async abrir(l: Llamada): Promise<void> {
    if (this.abierta() === l.id) { this.abierta.set(null); return; }
    this.abierta.set(l.id);
    this.detalle.set(null);
    try { const d = await firstValueFrom(this.http.get<Detalle>(`/api/seguridad/llamadas/${l.id}`)); if (this.abierta() === l.id) this.detalle.set(d); } catch { this.abierta.set(null); }
  }

  protected async vaciar(): Promise<void> {
    if (!confirm('¿Borrar todo el historial de llamadas externas?')) return;
    await firstValueFrom(this.http.delete('/api/seguridad/llamadas'));
    this.abierta.set(null);
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
