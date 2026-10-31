import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, effect, inject, signal, untracked } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { pedirCredencial, soportaPasskeys } from '../core/passkey';
import { SesionService } from '../core/sesion.service';
import { SistemaService } from '../inicio/sistema.service';

// Pantalla de bloqueo: con «pedir Face ID al volver» activado, al salir de la app el servidor bloquea la sesión y no contesta a nada
// hasta que se desbloquea con el passkey (o, de respaldo, con la contraseña). Esta capa tapa la app mientras tanto.
@Component({
  selector: 'app-bloqueo',
  template: `
    @if (sesion.bloqueada()) {
      <div class="velo" role="alertdialog" aria-modal="true" aria-labelledby="bloqueo-titulo">
        <div class="tarjeta">
          <svg class="icon candado" viewBox="0 0 24 24" aria-hidden="true"><path d="M7 11V8a5 5 0 0 1 10 0v3M5 11h14v10H5zM12 15v2"/></svg>
          <h2 id="bloqueo-titulo">Zona privada bloqueada</h2>
          <p>Desbloquéala para seguir.</p>
          @if (conPasskey) {
            <button class="button" type="button" (click)="conPasskeyDesbloquear()" [disabled]="ocupado()">{{ ocupado() ? 'Comprobando…' : 'Desbloquear con Face ID' }}</button>
          }
          @if (!clave()) {
            <button class="enlace" type="button" (click)="clave.set(true)">Usar la contraseña</button>
          } @else {
            <form (submit)="conClave($event)" novalidate>
              <input type="text" autocomplete="username" hidden>
              <input class="entrada" type="password" autocomplete="current-password" placeholder="Contraseña" [value]="password()" (input)="password.set($any($event.target).value)">
              <input class="entrada" inputmode="numeric" autocomplete="one-time-code" maxlength="12" placeholder="Código de dos pasos (si lo tienes)" [value]="codigo()" (input)="codigo.set($any($event.target).value)">
              <button class="button" type="submit" [disabled]="ocupado()">Desbloquear</button>
            </form>
          }
          @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
          <button class="enlace" type="button" (click)="salir()">Cerrar sesión</button>
        </div>
      </div>
    }
  `,
  styles: `
    .velo { position: fixed; inset: 0; z-index: 10000; display: grid; place-items: center; padding: 24px; background: color-mix(in srgb, var(--fondo) 94%, transparent); backdrop-filter: blur(24px) saturate(1.4); }
    .tarjeta { display: grid; gap: 14px; justify-items: center; width: min(92vw, 360px); text-align: center; }
    .candado { width: 56px; height: 56px; fill: none; stroke: var(--acento); stroke-width: 1.6; stroke-linecap: round; stroke-linejoin: round; }
    h2 { margin: 0; font: 700 1.3rem/1.1 var(--font-display); text-transform: uppercase; letter-spacing: -0.02em; }
    p { margin: 0; color: var(--soft); }
    .button { width: 100%; min-height: 50px; border: 0; border-radius: 999px; background: var(--brand); color: var(--on-brand); font: 700 16px/1 var(--font-text); cursor: pointer; }
    .button:disabled { opacity: .6; }
    .enlace { border: 0; background: none; color: var(--acento); font: 600 14px/1.2 var(--font-text); cursor: pointer; padding: 6px; }
    form { display: grid; gap: 10px; width: 100%; }
    .entrada { width: 100%; min-height: 46px; padding: 0 14px; border-radius: 12px; border: 1px solid var(--vidrio-borde); background: color-mix(in srgb, var(--texto) 6%, transparent); color: inherit; font: inherit; }
    .error { color: var(--bad); }
  `,
})
export class Bloqueo {
  protected readonly sesion = inject(SesionService);
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
      if (!cancelado && !silencioso) this.error.set('Ese passkey no sirve. Usa la contraseña.');
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
    } finally { this.ocupado.set(false); }
  }

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
