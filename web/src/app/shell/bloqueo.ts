import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { pedirCredencial, soportaPasskeys } from '../core/passkey';
import { SesionService } from '../core/sesion.service';
import { SistemaService } from '../inicio/sistema.service';
import { PerfilService } from '../core/perfil.service';

// Pantalla de bloqueo: con «pedir Face ID al volver» activado, al salir de la app el servidor bloquea la sesión y no contesta a nada
// hasta que se desbloquea con el passkey (o, de respaldo, con la contraseña). Esta capa tapa la app mientras tanto.
// Se parece a la pantalla de bloqueo del móvil, sin tarjeta: arriba la hora, en el centro el escáner con tu foto y abajo lo que se puede hacer.
// El escáner cuenta el estado: esperando, comprobando (línea que barre y anillo rápido), abierto (verde) y fallo (rojo).
@Component({
  selector: 'app-bloqueo',
  template: `
    @if (sesion.bloqueada()) {
      <div class="velo" role="alertdialog" aria-modal="true" aria-labelledby="bloqueo-titulo" aria-describedby="bloqueo-desc">
        <div class="fondo" aria-hidden="true"><i class="orbe o1"></i><i class="orbe o2"></i><i class="malla"></i></div>
        <main class="pantalla" [class.tiembla]="temblor()" [attr.data-fase]="fase()">
          <header class="cabeza">
            <p class="etiqueta">
              <svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M7 11V8a5 5 0 0 1 10 0v3M5 11h14v10H5z"/></svg>
              Bloqueada
            </p>
            <p class="reloj" aria-hidden="true">{{ hora() }}</p>
            <p class="fecha" aria-hidden="true">{{ fecha() }}</p>
          </header>

          <section class="centro">
            <div class="escaner" [attr.data-fase]="fase()">
              <svg class="escaner__retic" viewBox="0 0 160 160" aria-hidden="true">
                <circle class="anillo" cx="80" cy="80" r="70"/>
                <circle class="progreso" cx="80" cy="80" r="70"/>
                <g class="esquinas">
                  <path d="M18 46V28a10 10 0 0 1 10-10h18"/>
                  <path d="M18 46V28a10 10 0 0 1 10-10h18" transform="rotate(90 80 80)"/>
                  <path d="M18 46V28a10 10 0 0 1 10-10h18" transform="rotate(180 80 80)"/>
                  <path d="M18 46V28a10 10 0 0 1 10-10h18" transform="rotate(270 80 80)"/>
                </g>
              </svg>
              <img [src]="perfil.foto()" width="120" height="120" alt="">
              <i class="barrido"></i>
              <span class="insignia" aria-hidden="true">
                @switch (fase()) {
                  @case ('ok') { <svg class="icon" viewBox="0 0 24 24"><path d="M5 12.5l4.2 4.2L19 7"/></svg> }
                  @case ('fallo') { <svg class="icon" viewBox="0 0 24 24"><path d="M6 6l12 12M18 6 6 18"/></svg> }
                  @default { <svg class="icon" viewBox="0 0 24 24"><path d="M7 11V8a5 5 0 0 1 10 0v3M5 11h14v10H5z"/></svg> }
                }
              </span>
            </div>
            <h2 id="bloqueo-titulo">Hola{{ nombre() ? ', ' + nombre() : '' }}</h2>
            <p id="bloqueo-desc" class="aviso" [class.aviso--mal]="fase() === 'fallo'" role="status" aria-live="polite">{{ mensaje() }}</p>
          </section>

          <footer class="acciones">
            @if (conPasskey) {
              <button class="cara" type="button" (click)="conPasskeyDesbloquear()" [disabled]="ocupado() || exito()">
                <svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M4 8V6a2 2 0 0 1 2-2h2M16 4h2a2 2 0 0 1 2 2v2M20 16v2a2 2 0 0 1-2 2h-2M8 20H6a2 2 0 0 1-2-2v-2M9 10v1M15 10v1M12 10v4h-1M9 16c1.5 1.3 4.5 1.3 6 0"/></svg>
                <span>{{ ocupado() ? 'Comprobando…' : 'Desbloquear con Face ID' }}</span>
              </button>
            }
            @if (!clave()) {
              <button class="otra" type="button" (click)="clave.set(true)">Usar la contraseña</button>
            } @else {
              <form (submit)="conClave($event)" novalidate>
                <input type="text" autocomplete="username" hidden>
                <input class="entrada" type="password" autocomplete="current-password" placeholder="Contraseña" aria-label="Contraseña" [value]="password()" (input)="password.set($any($event.target).value)">
                <input class="entrada" inputmode="numeric" autocomplete="one-time-code" maxlength="12" placeholder="Código de dos pasos (si lo tienes)" aria-label="Código de dos pasos" [value]="codigo()" (input)="codigo.set($any($event.target).value)">
                <button class="otra otra--llena" type="submit" [disabled]="ocupado() || exito()">Desbloquear</button>
              </form>
            }
            <button class="enlace" type="button" (click)="salir()">Cerrar sesión</button>
          </footer>
        </main>
      </div>
    }
  `,
  styles: `
    /* capa fija a pantalla completa: el relleno respeta la muesca y la barra de inicio del iPhone; si no cabe (teclado, pantalla baja) se desplaza */
    .velo { position: fixed; inset: 0; z-index: 10000; display: flex; justify-content: center; overflow-x: hidden; overflow-y: auto; box-sizing: border-box; padding: max(12px, env(safe-area-inset-top)) 24px max(16px, env(safe-area-inset-bottom)); background: var(--fondo); animation: entra var(--dur-3, 420ms) var(--ease-out, ease-out) both; }
    .fondo { position: fixed; inset: 0; pointer-events: none; }
    .orbe { position: absolute; border-radius: 50%; filter: blur(70px); opacity: .42; animation: flota 14s ease-in-out infinite alternate; }
    .o1 { width: 52vmax; height: 52vmax; left: -14vmax; top: -18vmax; background: color-mix(in srgb, var(--brand) 55%, transparent); }
    .o2 { width: 44vmax; height: 44vmax; right: -12vmax; bottom: -16vmax; background: color-mix(in srgb, var(--acento) 35%, #3355ff); animation-duration: 19s; animation-direction: alternate-reverse; }
    .malla { position: absolute; inset: 0; background-image: linear-gradient(color-mix(in srgb, var(--texto) 5%, transparent) 1px, transparent 1px), linear-gradient(90deg, color-mix(in srgb, var(--texto) 5%, transparent) 1px, transparent 1px); background-size: 46px 46px; mask-image: radial-gradient(circle at 50% 45%, #000 0, transparent 70%); }

    /* tres bandas: la hora, el escáner (se lleva el hueco que sobra) y las acciones; el ancho nunca pasa de 360 px ni del de la pantalla */
    .pantalla { position: relative; align-self: stretch; flex: 0 1 360px; min-width: 0; min-height: max-content; display: grid; grid-template-columns: minmax(0, 1fr); grid-template-rows: auto minmax(min-content, 1fr) auto; justify-items: center; gap: 12px; text-align: center; animation: sube var(--dur-4, 700ms) var(--ease-out, ease-out) both; }
    .tiembla { animation: tiembla .45s ease; }
    .icon { fill: none; stroke: currentColor; stroke-width: 1.8; stroke-linecap: round; stroke-linejoin: round; }

    .cabeza { display: grid; justify-items: center; gap: 4px; }
    .etiqueta { display: inline-flex; align-items: center; gap: 6px; margin: 0 0 4px; padding: 6px 12px; border-radius: 999px; font: 600 .7rem/1 var(--font-code); letter-spacing: .14em; text-transform: uppercase; color: var(--soft); background: color-mix(in srgb, var(--texto) 7%, transparent); }
    .etiqueta .icon { width: 13px; height: 13px; }
    .reloj { margin: 0; font: var(--peso-ligero) clamp(3rem, 14vw, 3.8rem)/1 var(--font-text); letter-spacing: -0.04em; font-variant-numeric: tabular-nums; }
    .fecha { margin: 0; font-size: .95rem; color: var(--soft); }
    .fecha::first-letter { text-transform: uppercase; }

    .centro { display: grid; align-content: center; justify-items: center; gap: 6px; }

    /* escáner: retícula con esquinas, un anillo que gira y una línea que barre la foto mientras se comprueba */
    .escaner { --c: var(--brand); --e: 156px; --f: 106px; position: relative; display: grid; place-items: center; width: var(--e); height: var(--e); margin-bottom: 6px; }
    .escaner[data-fase='ok'] { --c: var(--ok); } .escaner[data-fase='fallo'] { --c: var(--bad); }
    .escaner__retic { position: absolute; inset: 0; width: 100%; height: 100%; fill: none; }
    .anillo { stroke: color-mix(in srgb, var(--texto) 10%, transparent); stroke-width: 2; }
    .progreso { stroke: var(--c); stroke-width: 3; stroke-linecap: round; stroke-dasharray: 90 350; transform-origin: 80px 80px; filter: drop-shadow(0 0 5px var(--c)); animation: gira 9s linear infinite; transition: stroke 400ms ease; }
    .escaner[data-fase='escaneando'] .progreso { animation-duration: 1.1s; stroke-dasharray: 150 290; }
    .escaner[data-fase='ok'] .progreso, .escaner[data-fase='fallo'] .progreso { animation: none; stroke-dasharray: 440 0; }
    .esquinas path { stroke: color-mix(in srgb, var(--c) 80%, transparent); stroke-width: 3; stroke-linecap: round; transition: stroke 400ms ease; }
    .escaner[data-fase='escaneando'] .esquinas { animation: aprieta 1.1s ease-in-out infinite alternate; transform-origin: 80px 80px; }
    .escaner img { position: relative; width: var(--f); height: var(--f); border-radius: 50%; object-fit: cover; border: 3px solid color-mix(in srgb, var(--c) 70%, transparent); box-shadow: 0 0 0 7px color-mix(in srgb, var(--c) 14%, transparent); transition: border-color 400ms ease, box-shadow 400ms ease; }
    .escaner[data-fase='escaneando'] img { animation: late .9s ease-in-out infinite; }
    .barrido { position: absolute; inset: calc((var(--e) - var(--f)) / 2); border-radius: 50%; overflow: hidden; pointer-events: none; opacity: 0; transition: opacity 200ms; }
    .escaner[data-fase='escaneando'] .barrido { opacity: 1; }
    .barrido::before { content: ''; position: absolute; left: 0; right: 0; top: 0; height: 3px; background: linear-gradient(90deg, transparent, var(--c), transparent); box-shadow: 0 0 14px var(--c); animation: barre 1.1s ease-in-out infinite alternate; }
    .insignia { position: absolute; right: 14px; bottom: 14px; display: grid; place-items: center; width: 36px; height: 36px; border-radius: 50%; background: var(--c); color: var(--on-brand); border: 3px solid var(--fondo); transition: background 400ms ease; animation: pop .4s var(--ease-out, ease-out); }
    .escaner[data-fase='ok'] .insignia, .escaner[data-fase='fallo'] .insignia { color: #fff; }
    .insignia .icon { width: 18px; height: 18px; stroke-width: 2.2; }

    h2 { margin: 0; font: 600 1.35rem/1.2 var(--font-text); }
    .aviso { margin: 0; min-height: 2.7em; color: var(--soft); font-size: .95rem; line-height: 1.35; max-width: 28ch; }
    .aviso--mal { color: var(--bad); }

    /* acciones: Face ID grande, la contraseña como segunda opción y «Cerrar sesión» apartado */
    .acciones { display: grid; grid-template-columns: minmax(0, 1fr); gap: 10px; width: 100%; }
    .cara { display: inline-flex; align-items: center; justify-content: center; gap: 12px; width: 100%; min-height: 58px; border: 0; border-radius: 999px; background: var(--brand); color: var(--on-brand); font: 700 1.02rem/1 var(--font-text); cursor: pointer; box-shadow: 0 10px 30px color-mix(in srgb, var(--brand) 38%, transparent); transition: transform var(--dur-1, 160ms) var(--ease-out, ease-out), box-shadow var(--dur-1, 160ms); }
    .cara:hover:not(:disabled) { transform: translateY(-2px); }
    .cara:active:not(:disabled) { transform: scale(.97); }
    .cara:disabled { opacity: .75; cursor: progress; }
    .cara .icon { width: 26px; height: 26px; }
    .otra { width: 100%; min-height: 52px; border: 1px solid var(--vidrio-borde); border-radius: 999px; background: color-mix(in srgb, var(--texto) 6%, transparent); color: var(--texto); font: 600 .98rem/1 var(--font-text); cursor: pointer; }
    .otra--llena { border-color: transparent; background: var(--brand); color: var(--on-brand); font-weight: 700; }
    .otra:disabled { opacity: .6; }
    .enlace { justify-self: center; min-height: 44px; padding: 10px 14px; border: 0; background: none; color: var(--soft); font: 500 .9rem/1.2 var(--font-text); cursor: pointer; }
    form { display: grid; grid-template-columns: minmax(0, 1fr); gap: 10px; width: 100%; animation: sube var(--dur-2, 240ms) ease both; }
    .entrada { width: 100%; min-height: 50px; padding: 0 16px; border-radius: 16px; border: 1px solid var(--vidrio-borde); background: color-mix(in srgb, var(--texto) 6%, transparent); color: inherit; font: inherit; font-size: 16px; box-sizing: border-box; }
    .entrada:focus-visible, .otra:focus-visible, .cara:focus-visible, .enlace:focus-visible { outline: 2px solid var(--acento); outline-offset: 2px; }
    @keyframes entra { from { opacity: 0; } }
    @keyframes sube { from { opacity: 0; transform: translateY(18px) scale(.98); } }
    @keyframes flota { to { transform: translate3d(5vmax, 4vmax, 0) scale(1.1); } }
    @keyframes gira { to { transform: rotate(360deg); } }
    @keyframes barre { to { top: calc(100% - 3px); } }
    @keyframes aprieta { to { transform: scale(.93); } }
    @keyframes pop { from { transform: scale(.3); opacity: 0; } 70% { transform: scale(1.2); } }
    @keyframes late { 50% { transform: scale(1.05); } }
    @keyframes tiembla { 20%, 60% { transform: translateX(-9px); } 40%, 80% { transform: translateX(9px); } }
    /* pantalla baja (móvil en horizontal, teclado abierto): escáner y reloj más pequeños para no tener que desplazar */
    @media (max-height: 820px) { .escaner { --e: 132px; --f: 90px; } .reloj { font-size: 2.8rem; } .pantalla { gap: 8px; } .aviso { min-height: 0; } .cara { min-height: 52px; } .otra { min-height: 46px; } }
    @media (max-height: 640px) { .escaner { --e: 108px; --f: 74px; } .reloj { font-size: 2.4rem; } .centro { gap: 2px; } }
    @media (prefers-reduced-motion: reduce) { .velo, .pantalla, form, .orbe, .progreso, .esquinas, .barrido::before, .insignia, .tiembla, .escaner img { animation: none !important; } .cara { transition: none; } }
  `,
})
export class Bloqueo {
  protected readonly sesion = inject(SesionService);
  protected readonly perfil = inject(PerfilService);
  protected readonly hora = signal(Bloqueo.formato());
  protected readonly fecha = signal(Bloqueo.dia());
  protected readonly temblor = signal(false);
  protected readonly nombre = computed(() => this.perfil.datos()?.nombre?.trim().split(' ')[0] ?? '');
  private static formato(): string { return new Date().toLocaleTimeString('es-ES', { hour: '2-digit', minute: '2-digit' }); }
  private static dia(): string { return new Date().toLocaleDateString('es-ES', { weekday: 'long', day: 'numeric', month: 'long' }); }
  private readonly http = inject(HttpClient);
  private readonly sistema = inject(SistemaService);
  protected readonly conPasskey = soportaPasskeys();
  protected readonly ocupado = signal(false);
  protected readonly exito = signal(false);
  protected readonly clave = signal(!soportaPasskeys());
  protected readonly password = signal('');
  protected readonly codigo = signal('');
  protected readonly error = signal('');
  private intentoAutomatico = false;

  // estado que se ve en el escáner
  protected readonly fase = computed(() => (this.exito() ? 'ok' : this.ocupado() ? 'escaneando' : this.error() ? 'fallo' : 'espera'));
  protected readonly mensaje = computed(() => {
    switch (this.fase()) {
      case 'ok': return 'Todo en orden. Entrando…';
      case 'escaneando': return 'Comprobando que eres tú…';
      case 'fallo': return this.error();
      default: return 'Tu zona privada se bloqueó al salir. Confirma que eres tú para seguir.';
    }
  });

  constructor() {
    // al salir de la app se avisa al servidor (sendBeacon sobrevive al cierre); al volver se pregunta si sigue abierta
    const alCambiar = () => {
      if (document.hidden) { if (this.sesion.bloqueoSegundos()) navigator.sendBeacon('/api/sesion/segundo-plano'); }
      else void this.sesion.refrescar();
    };
    document.addEventListener('visibilitychange', alCambiar);
    const reloj = setInterval(() => { this.hora.set(Bloqueo.formato()); this.fecha.set(Bloqueo.dia()); }, 10_000);
    inject(DestroyRef).onDestroy(() => clearInterval(reloj));
    inject(DestroyRef).onDestroy(() => document.removeEventListener('visibilitychange', alCambiar));

    effect(() => {
      const bloqueada = this.sesion.bloqueada();
      untracked(() => {
        if (!bloqueada) { this.intentoAutomatico = false; this.exito.set(false); return; }
        // cortado el tiempo real: la conexión que ya estaba abierta no vuelve a recibir datos mientras esté bloqueada
        void this.sistema.parar();
        // se intenta Face ID solo; si el sistema pide un toque del usuario, queda el botón
        if (this.conPasskey && !this.intentoAutomatico && !document.hidden) { this.intentoAutomatico = true; void this.conPasskeyDesbloquear(true); }
      });
    });
  }

  protected async conPasskeyDesbloquear(silencioso = false): Promise<void> {
    // el intento automático (al volver) no ocupa nada: si el sistema no enseña el diálogo, el botón y la contraseña siguen libres
    if (!silencioso && this.ocupado()) return;
    if (!silencioso) this.ocupado.set(true);
    this.error.set('');
    try {
      const opciones = await firstValueFrom(this.http.post<unknown>('/api/sesion/desbloqueo/opciones', null));
      const credencial = await pedirCredencial(opciones);
      await firstValueFrom(this.http.post('/api/sesion/desbloqueo', { credencial }));
      this.abierta();
    } catch (e) {
      const cancelado = e instanceof DOMException && (e.name === 'NotAllowedError' || e.name === 'AbortError');
      if (!cancelado && !silencioso) { this.error.set('Ese passkey no sirve. Usa la contraseña.'); this.sacudir(); }
    } finally { if (!silencioso) this.ocupado.set(false); }
  }

  protected async conClave(ev: Event): Promise<void> {
    ev.preventDefault();
    if (this.ocupado() || !this.password()) return;
    this.ocupado.set(true); this.error.set('');
    try {
      await firstValueFrom(this.http.post('/api/sesion/desbloqueo-clave', { password: this.password(), codigo: this.codigo() || null }));
      this.abierta();
    } catch (e) {
      const x = e as HttpErrorResponse;
      this.error.set(x.status === 429 ? 'Demasiados intentos: espera un rato.' : (x.error?.detail ?? 'Contraseña o código incorrectos.'));
      this.sacudir();
    } finally { this.ocupado.set(false); }
  }

  private sacudir(): void {
    this.temblor.set(false); setTimeout(() => this.temblor.set(true)); setTimeout(() => this.temblor.set(false), 500);
    navigator.vibrate?.([40, 60, 40]);
  }

  private abierta(): void {
    this.password.set(''); this.codigo.set('');
    this.exito.set(true);
    navigator.vibrate?.(30);
    // un instante para ver el verde antes de recargar; las pantallas se cargan de nuevo con la sesión abierta
    setTimeout(() => { this.sesion.bloqueada.set(false); location.reload(); }, 650);
  }

  protected async salir(): Promise<void> {
    await this.sesion.salir();
    location.href = '/entrar';
  }
}
