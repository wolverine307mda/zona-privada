import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { posicionSol } from '../core/astro';
import { CieloConfig } from '../core/cielo-config';
import { CieloVivo } from '../entrar/cielo-vivo';
import { SistemaService } from '../inicio/sistema.service';

interface Tarea { id: number; titulo: string; vencida: boolean; hoy: boolean; vence: string | null; prioridad: number; }
interface Papel { id: number; nombre: string; dias: number; estado: 'bien' | 'pronto' | 'caducado'; }
interface Servicio { id: number; nombre: string; activo: boolean; estado: '?' | 'ok' | 'caido'; }
interface Tiempo { lugar?: { nombre: string } | null; ahora?: { temperatura: number; sensacion: number; texto: string; humedad: number; viento: number }; dias?: { maxima: number; minima: number; lluvia: number }[]; }

// Sala de control / modo pared: una pantalla para dejar en una tablet o un móvil viejo apoyado en la pared. Reloj enorme sobre el cielo de verdad, el tiempo, el PC, lo que tienes por hacer,
// los papeles que caducan y si tus servicios siguen en pie. Mantiene la pantalla encendida (donde el navegador lo permite) y se atenúa de noche. Esc o la X para salir.
@Component({
  selector: 'app-pared',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CieloVivo],
  host: { '(document:keydown.escape)': 'salir()' },
  template: `
    <div class="pared" [class.pared--noche]="noche()">
      <app-cielo-vivo />
      <div class="velo" aria-hidden="true"></div>
      <button class="salir" type="button" (click)="salir()" aria-label="Salir del modo pared"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6l12 12M18 6 6 18" /></svg></button>

      <main class="contenido">
        <section class="reloj" aria-label="Hora">
          <p class="hora">{{ hora() }}</p>
          <p class="fecha">{{ fecha() }}</p>
        </section>

        <div class="tarjetas">
          @if (tiempo()?.ahora; as a) {
            <section class="tarjeta" aria-label="Tiempo">
              <h2>{{ tiempo()?.lugar?.nombre || 'Tiempo' }}</h2>
              <p class="grande">{{ redondo(a.temperatura) }}°</p>
              <p class="chico">{{ a.texto }} · sensación {{ redondo(a.sensacion) }}°</p>
              @if (tiempo()?.dias?.[0]; as d) { <p class="chico">Hoy {{ redondo(d.minima) }}° / {{ redondo(d.maxima) }}° · lluvia {{ d.lluvia }} %</p> }
            </section>
          }

          <section class="tarjeta" aria-label="Tu PC">
            <h2>Tu PC</h2>
            @if (sistema.actual(); as m) {
              <div class="barras">
                <div><span>CPU</span><i [style.--v]="m.cpu"></i><b>{{ redondo(m.cpu) }} %</b></div>
                <div><span>RAM</span><i [style.--v]="memoria()"></i><b>{{ redondo(memoria()) }} %</b></div>
                <div><span>Disco</span><i [style.--v]="disco()"></i><b>{{ redondo(disco()) }} %</b></div>
              </div>
            } @else { <p class="chico">Sin datos del PC.</p> }
          </section>

          <section class="tarjeta" aria-label="Por hacer">
            <h2>Por hacer</h2>
            @for (t of tareas(); track t.id) { <p class="linea" [class.linea--mal]="t.vencida" [class.linea--hoy]="t.hoy">{{ t.titulo }}</p> } @empty { <p class="chico">Nada pendiente 🎉</p> }
          </section>

          <section class="tarjeta" aria-label="Papeles y servicios">
            <h2>Atento a</h2>
            @for (p of papeles(); track p.id) { <p class="linea" [class.linea--mal]="p.estado === 'caducado'" [class.linea--hoy]="p.estado === 'pronto'">{{ p.nombre }} · {{ cuando(p.dias) }}</p> }
            @if (caidos().length) { <p class="linea linea--mal">Caído: {{ caidos().join(', ') }}</p> }
            @else if (vigilados() > 0) { <p class="linea">{{ vigilados() }} servicio{{ vigilados() === 1 ? '' : 's' }} en pie</p> }
            @if (!papeles().length && !caidos().length && vigilados() === 0) { <p class="chico">Todo en orden.</p> }
          </section>
        </div>
      </main>
    </div>
  `,
  styles: `
    :host { position: fixed; inset: 0; z-index: 9000; display: block; }
    .pared { position: absolute; inset: 0; overflow: hidden; background: #05070f; color: #fff; transition: filter 2s; }
    .pared--noche { filter: brightness(.55); }
    .velo { position: absolute; inset: 0; z-index: 1; background: linear-gradient(to bottom, rgb(0 0 0 / .35), rgb(0 0 0 / .1) 40%, rgb(0 0 0 / .45)); }
    .salir { position: absolute; z-index: 3; top: max(14px, env(safe-area-inset-top)); right: 14px; width: 44px; height: 44px; border-radius: 50%; border: 0; background: rgb(255 255 255 / .14); color: #fff; display: grid; place-items: center; cursor: pointer; opacity: .55; }
    .salir:hover, .salir:focus-visible { opacity: 1; }
    .salir svg { width: 22px; height: 22px; fill: none; stroke: currentColor; stroke-width: 2; stroke-linecap: round; }
    .contenido { position: relative; z-index: 2; height: 100%; display: grid; grid-template-rows: auto 1fr; gap: clamp(12px, 3vh, 32px); padding: max(22px, env(safe-area-inset-top)) clamp(16px, 4vw, 56px) max(22px, env(safe-area-inset-bottom)); overflow: auto; }
    .reloj { text-align: center; }
    .hora { margin: 0; font: 200 clamp(4.5rem, 20vmin, 13rem)/1 var(--font-text); letter-spacing: -.04em; text-shadow: 0 2px 30px rgb(0 0 0 / .45); font-variant-numeric: tabular-nums; }
    .fecha { margin: 0; font: 300 clamp(1.1rem, 3vmin, 2rem)/1.2 var(--font-text); text-shadow: 0 1px 12px rgb(0 0 0 / .5); }
    .tarjetas { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(240px, 100%), 1fr)); gap: clamp(10px, 1.6vw, 20px); align-content: start; }
    .tarjeta { padding: clamp(14px, 2vw, 24px); border-radius: 22px; background: rgb(8 12 24 / .5); backdrop-filter: blur(14px); border: 1px solid rgb(255 255 255 / .12); display: grid; gap: 6px; align-content: start; }
    .tarjeta h2 { margin: 0 0 4px; font: 500 .72rem/1 var(--font-code); letter-spacing: .14em; text-transform: uppercase; color: rgb(255 255 255 / .6); }
    .grande { margin: 0; font: 200 clamp(2.6rem, 7vmin, 5rem)/1 var(--font-text); }
    .chico { margin: 0; color: rgb(255 255 255 / .72); font-size: clamp(.85rem, 1.8vmin, 1.05rem); }
    .linea { margin: 0; font-size: clamp(.95rem, 2vmin, 1.2rem); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .linea--hoy { color: #ffc46b; }
    .linea--mal { color: #ff7d73; }
    .barras { display: grid; gap: 10px; }
    .barras div { display: grid; grid-template-columns: 3.2rem 1fr 3.4rem; align-items: center; gap: 10px; font-size: .95rem; }
    .barras span { color: rgb(255 255 255 / .65); }
    .barras b { font-weight: 500; text-align: right; font-variant-numeric: tabular-nums; }
    .barras i { height: 8px; border-radius: 99px; background: rgb(255 255 255 / .14); position: relative; overflow: hidden; }
    .barras i::after { content: ''; position: absolute; inset: 0 auto 0 0; width: calc(var(--v) * 1%); max-width: 100%; background: linear-gradient(90deg, #4ee3a0, #ffc46b 70%, #ff7d73); border-radius: inherit; }
    @media (prefers-reduced-motion: reduce) { .pared { transition: none; } }
  `,
})
export class Pared {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly config = inject(CieloConfig);
  protected readonly sistema = inject(SistemaService);

  private readonly ahora = signal(new Date());
  protected readonly tiempo = signal<Tiempo | null>(null);
  protected readonly tareas = signal<Tarea[]>([]);
  protected readonly papeles = signal<Papel[]>([]);
  protected readonly servicios = signal<Servicio[]>([]);

  protected readonly hora = computed(() => this.ahora().toLocaleTimeString('es-ES', { hour: '2-digit', minute: '2-digit' }));
  protected readonly fecha = computed(() => { const f = this.ahora().toLocaleDateString('es-ES', { weekday: 'long', day: 'numeric', month: 'long' }); return f.charAt(0).toUpperCase() + f.slice(1); });
  protected readonly memoria = computed(() => { const m = this.sistema.actual(); return m && m.memoriaTotal ? (100 * m.memoriaUsada) / m.memoriaTotal : 0; });
  protected readonly disco = computed(() => { const m = this.sistema.actual(); return m && m.discoTotal ? (100 * m.discoUsado) / m.discoTotal : 0; });
  protected readonly caidos = computed(() => this.servicios().filter((s) => s.activo && s.estado === 'caido').map((s) => s.nombre));
  protected readonly vigilados = computed(() => this.servicios().filter((s) => s.activo).length);
  // de noche (Sol 6° o más bajo el horizonte) la pantalla se atenúa para no deslumbrar
  protected readonly noche = computed(() => { const l = this.config.lugar(); return (posicionSol(this.ahora(), l.lat, l.lon).altura * 180) / Math.PI < -6; });

  private bloqueo: { release: () => Promise<void> } | null = null;

  constructor() {
    void this.sistema.empezar();
    void this.cargar();
    const reloj = setInterval(() => this.ahora.set(new Date()), 5_000);
    const datos = setInterval(() => void this.cargar(), 60_000);
    const pantalla = () => { if (!document.hidden) void this.mantenerDespierta(); };
    document.addEventListener('visibilitychange', pantalla);
    void this.mantenerDespierta();
    inject(DestroyRef).onDestroy(() => {
      clearInterval(reloj); clearInterval(datos); document.removeEventListener('visibilitychange', pantalla);
      void this.bloqueo?.release().catch(() => {});
    });
  }

  // Cada panel carga por su cuenta: si uno falla, los demás siguen
  private async cargar(): Promise<void> {
    const pedir = <T>(url: string) => firstValueFrom(this.http.get<T>(url)).catch(() => null);
    const [t, ta, pa, se] = await Promise.all([
      pedir<Tiempo>('/api/tiempo'), pedir<Tarea[]>('/api/tareas'), pedir<{ papeles: Papel[] }>('/api/papeles'), pedir<Servicio[]>('/api/servicios'),
    ]);
    this.tiempo.set(t); if (ta) this.tareas.set(ta.slice(0, 6)); if (pa) this.papeles.set(pa.papeles.filter((p) => p.estado !== 'bien').slice(0, 4)); if (se) this.servicios.set(se);
  }

  // Que la pantalla no se apague (la Wake Lock API: iOS 16.4+ y los navegadores de escritorio); si no se puede, no pasa nada
  private async mantenerDespierta(): Promise<void> {
    try {
      const nav = navigator as Navigator & { wakeLock?: { request: (t: 'screen') => Promise<{ release: () => Promise<void> }> } };
      this.bloqueo = (await nav.wakeLock?.request('screen')) ?? null;
    } catch { this.bloqueo = null; }
  }

  protected redondo(n: number): string { return Math.round(n).toString(); }
  protected cuando(dias: number): string { return dias < 0 ? `caducó hace ${-dias} d` : dias === 0 ? 'caduca hoy' : `en ${dias} d`; }
  protected salir(): void { void this.router.navigateByUrl('/inicio'); }
}
