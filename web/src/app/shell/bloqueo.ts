import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { pedirCredencial, soportaPasskeys } from '../core/passkey';
import { SesionService } from '../core/sesion.service';
import { SistemaService } from '../inicio/sistema.service';
import { PerfilService } from '../core/perfil.service';

// Pantalla de bloqueo: con «pedir Face ID al volver» activado, al salir de la app el servidor bloquea la sesión y no contesta a nada
// hasta que se desbloquea con el passkey (o, de respaldo, con la contraseña). Esta capa tapa la app mientras tanto.
// Cuatro estados que se notan en el escáner de la foto: esperando, comprobando (línea que barre y anillo rápido), abierto (verde) y fallo (rojo).
@Component({
  selector: 'app-bloqueo',
  template: `
    @if (sesion.bloqueada()) {
      <div class="velo" role="alertdialog" aria-modal="true" aria-labelledby="bloqueo-titulo" aria-describedby="bloqueo-desc">
        <div class="fondo" aria-hidden="true"><i class="orbe o1"></i><i class="orbe o2"></i><i class="malla"></i></div>
        <div class="tarjeta" [class.tiembla]="temblor()" [attr.data-fase]="fase()">
          <p class="etiqueta">
            <svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M7 11V8a5 5 0 0 1 10 0v3M5 11h14v10H5z"/></svg>
            Bloqueada
          </p>
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
            <img [src]="perfil.foto()" width="104" height="104" alt="">
            <i class="barrido"></i>
            <span class="insignia" aria-hidden="true">
              @switch (fase()) {
                @case ('ok') { <svg class="icon" viewBox="0 0 24 24"><path d="M5 12.5l4.2 4.2L19 7"/></svg> }
                @case ('fallo') { <svg class="icon" viewBox="0 0 24 24"><path d="M6 6l12 12M18 6 6 18"/></svg> }
                @default { <svg class="icon" viewBox="0 0 24 24"><path d="M7 11V8a5 5 0 0 1 10 0v3M5 11h14v10H5z"/></svg> }
              }
            </span>
          </div>
          <p class="reloj" aria-hidden="true">{{ hora() }}</p>
          <p class="fecha" aria-hidden="true">{{ fecha() }}</p>
          <h2 id="bloqueo-titulo">Hola{{ nombre() ? ', ' + nombre() : '' }}</h2>
          <p id="bloqueo-desc" class="aviso" [class.aviso--mal]="fase() === 'fallo'" role="status" aria-live="polite">{{ mensaje() }}</p>

          @if (conPasskey) {
            <button class="cara" type="button" (click)="conPasskeyDesbloquear()" [disabled]="ocupado() || exito()">
              <svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M4 8V6a2 2 0 0 1 2-2h2M16 4h2a2 2 0 0 1 2 2v2M20 16v2a2 2 0 0 1-2 2h-2M8 20H6a2 2 0 0 1-2-2v-2M9 10v1M15 10v1M12 10v4h-1M9 16c1.5 1.3 4.5 1.3 6 0"/></svg>
              <span>{{ ocupado() ? 'Comprobando…' : 'Desbloquear con Face ID' }}</span>
            </button>
          }
          @if (!clave()) {
            <button class="enlace" type="button" (click)="clave.set(true)">Usar la contraseña</button>
          } @else {
            <form (submit)="conClave($event)" novalidate>
              <input type="text" autocomplete="username" hidden>
              <input class="entrada" type="password" autocomplete="current-password" placeholder="Contraseña" aria-label="Contraseña" [value]="password()" (input)="password.set($any($event.target).value)">
              <input class="entrada" inputmode="numeric" autocomplete="one-time-code" maxlength="12" placeholder="Código de dos pasos (si lo tienes)" aria-label="Código de dos pasos" [value]="codigo()" (input)="codigo.set($any($event.target).value)">
              <button class="button" type="submit" [disabled]="ocupado() || exito()">Desbloquear</button>
            </form>
          }
          <button class="enlace enlace--suave" type="button" (click)="salir()">Cerrar sesión</button>
        </div>
      </div>
    }
  `,
  styles: `
    .velo { position: fixed; inset: 0; z-index: 10000; display: grid; place-items: center; padding: 24px; overflow: hidden; background: var(--fondo); animation: entra var(--dur-3, 420ms) var(--ease-out, ease-out) both; }
    .fondo { position: absolute; inset: 0; pointer-events: none; }
    .orbe { position: absolute; border-radius: 50%; filter: blur(70px); opacity: .55; animation: flota 14s ease-in-out infinite alternate; }
    .o1 { width: 52vmax; height: 52vmax; left: -14vmax; top: -18vmax; background: color-mix(in srgb, var(--brand) 55%, transparent); }
    .o2 { width: 44vmax; height: 44vmax; right: -12vmax; bottom: -16vmax; background: color-mix(in srgb, var(--acento) 35%, #3355ff); animation-duration: 19s; animation-direction: alternate-reverse; }
    .malla { position: absolute; inset: 0; background-image: linear-gradient(color-mix(in srgb, var(--texto) 6%, transparent) 1px, transparent 1px), linear-gradient(90deg, color-mix(in srgb, var(--texto) 6%, transparent) 1px, transparent 1px); background-size: 46px 46px; mask-image: radial-gradient(circle at 50% 45%, #000 0, transparent 70%); }
    .tarjeta { position: relative; display: grid; gap: 10px; justify-items: center; width: min(92vw, 390px); padding: 24px 26px 20px; text-align: center; border-radius: 32px; border: 1px solid var(--vidrio-borde); background: var(--brillo), var(--vidrio-fuerte); backdrop-filter: blur(26px) saturate(1.5); box-shadow: 0 30px 80px rgba(0, 0, 0, .35); transition: box-shadow 500ms ease, border-color 500ms ease; animation: sube var(--dur-4, 700ms) var(--ease-out, ease-out) both; }
    .tarjeta[data-fase='ok'] { border-color: color-mix(in srgb, #3fb96a 60%, transparent); box-shadow: 0 30px 90px color-mix(in srgb, #3fb96a 28%, transparent); }
    .tiembla { animation: tiembla .45s ease; }
    .etiqueta { display: inline-flex; align-items: center; gap: 6px; margin: 0; padding: 5px 12px; border-radius: 999px; font: 600 .7rem/1 var(--font-code); letter-spacing: .14em; text-transform: uppercase; color: var(--soft); border: 1px solid var(--vidrio-borde); }
    .etiqueta .icon { width: 13px; height: 13px; }
    .icon { fill: none; stroke: currentColor; stroke-width: 1.8; stroke-linecap: round; stroke-linejoin: round; }

    /* escáner: retícula con esquinas, un anillo que gira y una línea que barre la foto mientras se comprueba */
    .escaner { --c: var(--brand); position: relative; display: grid; place-items: center; width: 156px; height: 156px; margin: 6px 0 2px; }
    .escaner[data-fase='ok'] { --c: #3fb96a; } .escaner[data-fase='fallo'] { --c: var(--bad); }
    .escaner__retic { position: absolute; inset: 0; width: 100%; height: 100%; fill: none; }
    .anillo { stroke: color-mix(in srgb, var(--texto) 10%, transparent); stroke-width: 2; }
    .progreso { stroke: var(--c); stroke-width: 3; stroke-linecap: round; stroke-dasharray: 90 350; transform-origin: 80px 80px; filter: drop-shadow(0 0 5px var(--c)); animation: gira 9s linear infinite; transition: stroke 400ms ease; }
    .escaner[data-fase='escaneando'] .progreso { animation-duration: 1.1s; stroke-dasharray: 150 290; }
    .escaner[data-fase='ok'] .progreso, .escaner[data-fase='fallo'] .progreso { animation: none; stroke-dasharray: 440 0; }
    .esquinas path { stroke: color-mix(in srgb, var(--c) 80%, transparent); stroke-width: 3; stroke-linecap: round; transition: stroke 400ms ease; }
    .escaner[data-fase='escaneando'] .esquinas { animation: aprieta 1.1s ease-in-out infinite alternate; transform-origin: 80px 80px; }
    .escaner img { position: relative; width: 104px; height: 104px; border-radius: 50%; object-fit: cover; border: 3px solid color-mix(in srgb, var(--c) 70%, transparent); box-shadow: 0 0 0 6px color-mix(in srgb, var(--c) 14%, transparent); transition: border-color 400ms ease, box-shadow 400ms ease; }
    .escaner[data-fase='escaneando'] img { animation: late .9s ease-in-out infinite; }
    .barrido { position: absolute; left: 26px; top: 26px; width: 104px; height: 104px; border-radius: 50%; overflow: hidden; pointer-events: none; opacity: 0; transition: opacity 200ms; }
    .escaner[data-fase='escaneando'] .barrido { opacity: 1; }
    .barrido::before { content: ''; position: absolute; left: 0; right: 0; top: 0; height: 3px; background: linear-gradient(90deg, transparent, var(--c), transparent); box-shadow: 0 0 14px var(--c); animation: barre 1.1s ease-in-out infinite alternate; }
    .insignia { position: absolute; right: 12px; bottom: 12px; display: grid; place-items: center; width: 34px; height: 34px; border-radius: 50%; background: var(--c); color: var(--on-brand); border: 3px solid var(--fondo); transition: background 400ms ease; animation: pop .4s var(--ease-out, ease-out); }
    .escaner[data-fase='ok'] .insignia, .escaner[data-fase='fallo'] .insignia { color: #fff; }
    .insignia .icon { width: 17px; height: 17px; stroke-width: 2.2; }

    .reloj { margin: 4px 0 0; font: var(--peso-ligero) clamp(2.6rem, 12vw, 3.4rem)/1 var(--font-text); letter-spacing: -0.03em; font-variant-numeric: tabular-nums; }
    .fecha { margin: 0; font-size: .86rem; color: var(--soft); text-transform: capitalize; }
    h2 { margin: 6px 0 0; font: 600 1.15rem/1.2 var(--font-text); }
    .aviso { margin: 0 0 6px; min-height: 2.6em; color: var(--soft); font-size: .92rem; max-width: 30ch; }
    .aviso--mal { color: var(--bad); }
    .cara { display: inline-flex; align-items: center; justify-content: center; gap: 12px; width: 100%; min-height: 56px; border: 0; border-radius: 999px; background: var(--brand); color: var(--on-brand); font: 700 1rem/1 var(--font-text); cursor: pointer; box-shadow: 0 10px 30px color-mix(in srgb, var(--brand) 40%, transparent); transition: transform var(--dur-1, 160ms) var(--ease-out, ease-out), box-shadow var(--dur-1, 160ms); }
    .cara:hover:not(:disabled) { transform: translateY(-2px); }
    .cara:active:not(:disabled) { transform: scale(.97); }
    .cara:disabled { opacity: .75; cursor: progress; }
    .cara .icon { width: 26px; height: 26px; }
    .button { width: 100%; min-height: 50px; border: 0; border-radius: 999px; background: var(--brand); color: var(--on-brand); font: 700 16px/1 var(--font-text); cursor: pointer; }
    .button:disabled { opacity: .6; }
    .enlace { border: 0; background: none; color: var(--acento); font: 600 14px/1.2 var(--font-text); cursor: pointer; padding: 10px 8px; min-height: 40px; }
    .enlace--suave { color: var(--soft); font-weight: 500; }
    form { display: grid; gap: 10px; width: 100%; animation: sube var(--dur-2, 240ms) ease both; }
    .entrada { width: 100%; min-height: 48px; padding: 0 16px; border-radius: 14px; border: 1px solid var(--vidrio-borde); background: color-mix(in srgb, var(--texto) 6%, transparent); color: inherit; font: inherit; box-sizing: border-box; }
    .entrada:focus-visible { outline: 2px solid var(--acento); outline-offset: 1px; }
    @keyframes entra { from { opacity: 0; } }
    @keyframes sube { from { opacity: 0; transform: translateY(18px) scale(.98); } }
    @keyframes flota { to { transform: translate3d(5vmax, 4vmax, 0) scale(1.1); } }
    @keyframes gira { to { transform: rotate(360deg); } }
    @keyframes barre { to { top: calc(100% - 3px); } }
    @keyframes aprieta { to { transform: scale(.93); } }
    @keyframes pop { from { transform: scale(.3); opacity: 0; } 70% { transform: scale(1.2); } }
    @keyframes late { 50% { transform: scale(1.05); } }
    @keyframes tiembla { 20%, 60% { transform: translateX(-9px); } 40%, 80% { transform: translateX(9px); } }
    @media (prefers-reduced-motion: reduce) { .velo, .tarjeta, form, .orbe, .progreso, .esquinas, .barrido::before, .insignia, .tiembla, .escaner img { animation: none !important; } .cara { transition: none; } }
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
