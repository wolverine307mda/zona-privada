import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { pedirCredencial, soportaPasskeys } from '../core/passkey';
import { SesionService } from '../core/sesion.service';
import { SistemaService } from '../inicio/sistema.service';
import { PerfilService } from '../core/perfil.service';

// Pantalla de bloqueo: con «pedir Face ID al volver» activado, al salir de la app el servidor bloquea la sesión y no contesta a nada
// hasta que se desbloquea con el passkey (o, de respaldo, con la contraseña). Esta capa tapa la app mientras tanto.
@Component({
  selector: 'app-bloqueo',
  template: `
    @if (sesion.bloqueada()) {
      <div class="velo" role="alertdialog" aria-modal="true" aria-labelledby="bloqueo-titulo" aria-describedby="bloqueo-desc">
        <div class="fondo" aria-hidden="true"><i class="orbe o1"></i><i class="orbe o2"></i><i class="malla"></i></div>
        <div class="tarjeta" [class.tiembla]="temblor()">
          <div class="avatar" [class.escaneando]="ocupado()">
            <i class="onda"></i><i class="onda onda--2"></i>
            <img [src]="perfil.foto()" width="104" height="104" alt="">
            <span class="candado" aria-hidden="true"><svg class="icon" viewBox="0 0 24 24"><path d="M7 11V8a5 5 0 0 1 10 0v3M5 11h14v10H5z"/></svg></span>
          </div>
          <p class="reloj" aria-hidden="true">{{ hora() }}</p>
          <h2 id="bloqueo-titulo">Hola{{ nombre() ? ', ' + nombre() : '' }}</h2>
          <p id="bloqueo-desc" class="aviso">Tu zona privada se bloqueó al salir. Confirma que eres tú para seguir.</p>

          @if (conPasskey) {
            <button class="cara" type="button" (click)="conPasskeyDesbloquear()" [disabled]="ocupado()">
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
              <button class="button" type="submit" [disabled]="ocupado()">Desbloquear</button>
            </form>
          }
          @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
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
    .tarjeta { position: relative; display: grid; gap: 14px; justify-items: center; width: min(92vw, 380px); padding: 34px 26px 22px; text-align: center; border-radius: 28px; border: 1px solid var(--vidrio-borde); background: var(--brillo), var(--vidrio-fuerte); backdrop-filter: blur(26px) saturate(1.5); box-shadow: 0 30px 80px rgba(0, 0, 0, .35); animation: sube var(--dur-4, 700ms) var(--ease-out, ease-out) both; }
    .tiembla { animation: tiembla .45s ease; }
    .avatar { position: relative; width: 104px; height: 104px; margin-bottom: 4px; }
    .avatar img { position: relative; width: 104px; height: 104px; border-radius: 50%; object-fit: cover; border: 3px solid color-mix(in srgb, var(--brand) 70%, transparent); box-shadow: 0 0 0 6px color-mix(in srgb, var(--brand) 14%, transparent); }
    .onda { position: absolute; inset: 0; border-radius: 50%; border: 2px solid var(--brand); opacity: 0; animation: onda 3.2s ease-out infinite; }
    .onda--2 { animation-delay: 1.6s; }
    .escaneando .onda { animation-duration: 1.1s; }
    .escaneando .onda--2 { animation-delay: .55s; }
    .escaneando img { animation: late .9s ease-in-out infinite; }
    .candado { position: absolute; right: -4px; bottom: -2px; display: grid; place-items: center; width: 34px; height: 34px; border-radius: 50%; background: var(--brand); color: var(--on-brand); border: 3px solid var(--fondo); }
    .candado .icon { width: 17px; height: 17px; fill: none; stroke: currentColor; stroke-width: 2; stroke-linecap: round; stroke-linejoin: round; }
    .reloj { margin: 0; font: 700 clamp(2.4rem, 11vw, 3.2rem)/1 var(--font-display); letter-spacing: -0.03em; font-variant-numeric: tabular-nums; }
    h2 { margin: 0; font: 600 1.15rem/1.2 var(--font-text); }
    .aviso { margin: 0 0 6px; color: var(--soft); font-size: .92rem; max-width: 30ch; }
    .cara { display: inline-flex; align-items: center; justify-content: center; gap: 12px; width: 100%; min-height: 56px; border: 0; border-radius: 999px; background: var(--brand); color: var(--on-brand); font: 700 1rem/1 var(--font-text); cursor: pointer; box-shadow: 0 10px 30px color-mix(in srgb, var(--brand) 40%, transparent); transition: transform var(--dur-1, 160ms) var(--ease-out, ease-out), box-shadow var(--dur-1, 160ms); }
    .cara:hover:not(:disabled) { transform: translateY(-2px); }
    .cara:active:not(:disabled) { transform: scale(.97); }
    .cara:disabled { opacity: .75; cursor: progress; }
    .cara .icon { width: 26px; height: 26px; fill: none; stroke: currentColor; stroke-width: 1.8; stroke-linecap: round; stroke-linejoin: round; }
    .button { width: 100%; min-height: 50px; border: 0; border-radius: 999px; background: var(--brand); color: var(--on-brand); font: 700 16px/1 var(--font-text); cursor: pointer; }
    .button:disabled { opacity: .6; }
    .enlace { border: 0; background: none; color: var(--acento); font: 600 14px/1.2 var(--font-text); cursor: pointer; padding: 10px 8px; min-height: 40px; }
    .enlace--suave { color: var(--soft); font-weight: 500; }
    form { display: grid; gap: 10px; width: 100%; animation: sube var(--dur-2, 240ms) ease both; }
    .entrada { width: 100%; min-height: 48px; padding: 0 16px; border-radius: 14px; border: 1px solid var(--vidrio-borde); background: color-mix(in srgb, var(--texto) 6%, transparent); color: inherit; font: inherit; box-sizing: border-box; }
    .entrada:focus-visible { outline: 2px solid var(--acento); outline-offset: 1px; }
    .error { margin: 0; color: var(--bad); font-size: .9rem; }
    @keyframes entra { from { opacity: 0; } }
    @keyframes sube { from { opacity: 0; transform: translateY(18px) scale(.98); } }
    @keyframes flota { to { transform: translate3d(5vmax, 4vmax, 0) scale(1.1); } }
    @keyframes onda { 0% { opacity: .7; transform: scale(1); } 100% { opacity: 0; transform: scale(1.9); } }
    @keyframes late { 50% { transform: scale(1.05); } }
    @keyframes tiembla { 20%, 60% { transform: translateX(-9px); } 40%, 80% { transform: translateX(9px); } }
    @media (prefers-reduced-motion: reduce) { .velo, .tarjeta, form, .orbe, .onda, .tiembla, .escaneando img { animation: none; } .cara { transition: none; } .onda { opacity: 0; } }
  `,
})
export class Bloqueo {
  protected readonly sesion = inject(SesionService);
  protected readonly perfil = inject(PerfilService);
  protected readonly hora = signal(Bloqueo.formato());
  protected readonly temblor = signal(false);
  protected readonly nombre = computed(() => this.perfil.datos()?.nombre?.trim().split(' ')[0] ?? '');
  private static formato(): string { return new Date().toLocaleTimeString('es-ES', { hour: '2-digit', minute: '2-digit' }); }
  private readonly http = inject(HttpClient);
  private readonly sistema = inject(SistemaService);
  protected readonly conPasskey = soportaPasskeys();
  protected readonly ocupado = signal(false);
  protected readonly clave = signal(!soportaPasskeys());
  protected readonly password = signal('');
  protected readonly codigo = signal('');
  protected readonly error = signal('');
  private intentoAutomatico = false;

  constructor() {
    // al salir de la app se avisa al servidor (sendBeacon sobrevive al cierre); al volver se pregunta si sigue abierta
    const alCambiar = () => {
      if (document.hidden) { if (this.sesion.bloqueoSegundos()) navigator.sendBeacon('/api/sesion/segundo-plano'); }
      else void this.sesion.refrescar();
    };
    document.addEventListener('visibilitychange', alCambiar);
    const reloj = setInterval(() => this.hora.set(Bloqueo.formato()), 10_000);
    inject(DestroyRef).onDestroy(() => clearInterval(reloj));
    inject(DestroyRef).onDestroy(() => document.removeEventListener('visibilitychange', alCambiar));

    effect(() => {
      const bloqueada = this.sesion.bloqueada();
      untracked(() => {
        if (!bloqueada) { this.intentoAutomatico = false; return; }
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

  private sacudir(): void { this.temblor.set(false); setTimeout(() => this.temblor.set(true)); setTimeout(() => this.temblor.set(false), 500); }

  private abierta(): void {
    this.password.set(''); this.codigo.set('');
    this.sesion.bloqueada.set(false);
    location.reload(); // las pantallas se cargan de nuevo con la sesión abierta
  }

  protected async salir(): Promise<void> {
    await this.sesion.salir();
    location.href = '/entrar';
  }
}
