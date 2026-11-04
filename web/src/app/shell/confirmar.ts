import { HttpErrorResponse } from '@angular/common/http';
import { Component, HostListener, effect, inject, signal, untracked } from '@angular/core';
import { ConfirmarService } from '../core/confirmar.service';
import { Reautenticar } from '../core/reautenticar';

// «Confirma que eres tú»: Face ID (o huella) y, de respaldo, la contraseña con el código de dos pasos. Si sale bien, el servidor deja
// la sesión confirmada 2 minutos. Se abre con ConfirmarService.pedir() desde cualquier pantalla.
@Component({
  selector: 'app-confirmar',
  template: `
    @if (c.abierto()) {
      <div class="velo" role="dialog" aria-modal="true" aria-labelledby="conf-titulo" (click)="cancelar()">
        <form class="tarjeta" (submit)="conClave($event)" (click)="$event.stopPropagation()" novalidate>
          <span class="candado" [class.escaneando]="ocupado()" aria-hidden="true"><svg class="icon" viewBox="0 0 24 24"><path d="M4 8V6a2 2 0 0 1 2-2h2M16 4h2a2 2 0 0 1 2 2v2M20 16v2a2 2 0 0 1-2 2h-2M8 20H6a2 2 0 0 1-2-2v-2M9 10v1M15 10v1M12 10v4h-1M9 16c1.5 1.3 4.5 1.3 6 0"/></svg></span>
          <h2 id="conf-titulo">Confirma que eres tú</h2>
          <p class="aviso">{{ c.motivo() }}</p>
          @if (reauth.conPasskey) {
            <button class="principal" type="button" (click)="conCara()" [disabled]="ocupado()">{{ ocupado() ? 'Comprobando…' : 'Confirmar con Face ID' }}</button>
            @if (!clave()) { <button class="enlace" type="button" (click)="clave.set(true)">Usar la contraseña</button> }
          }
          @if (clave() || !reauth.conPasskey) {
            <input type="text" autocomplete="username" hidden>
            <input class="entrada" type="password" autocomplete="current-password" placeholder="Tu contraseña" aria-label="Tu contraseña" [value]="password()" (input)="password.set($any($event.target).value)">
            <input class="entrada" inputmode="numeric" autocomplete="one-time-code" maxlength="12" placeholder="Código de dos pasos (si lo tienes)" aria-label="Código de dos pasos" [value]="codigo()" (input)="codigo.set($any($event.target).value)">
            <button class="principal" type="submit" [disabled]="ocupado() || !password()">Confirmar</button>
          }
          @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
          <button class="enlace" type="button" (click)="cancelar()">Cancelar</button>
        </form>
      </div>
    }
  `,
  styles: `
    .velo { position: fixed; inset: 0; z-index: 9100; display: grid; place-items: center; padding: 20px; background: color-mix(in srgb, #000 55%, transparent); backdrop-filter: blur(10px); animation: aparece var(--dur-2, 240ms) ease-out both; }
    .tarjeta { display: grid; gap: 12px; justify-items: center; width: min(92vw, 360px); padding: 28px 22px 20px; text-align: center; border-radius: 30px; border: 1px solid var(--vidrio-borde); background: var(--brillo), var(--vidrio-fuerte); backdrop-filter: blur(26px) saturate(1.5); box-shadow: 0 30px 80px rgba(0, 0, 0, .4); animation: sube var(--dur-3, 420ms) var(--ease-out, ease-out) both; }
    .candado { display: grid; place-items: center; width: 60px; height: 60px; border-radius: 50%; background: color-mix(in srgb, var(--brand) 18%, transparent); color: var(--brand); }
    .escaneando { animation: late .9s ease-in-out infinite; }
    .icon { width: 28px; height: 28px; fill: none; stroke: currentColor; stroke-width: 1.8; stroke-linecap: round; stroke-linejoin: round; }
    h2 { margin: 0; font: 600 1.1rem/1.2 var(--font-text); }
    .aviso { margin: 0 0 4px; color: var(--soft); font-size: .9rem; max-width: 30ch; }
    .principal { width: 100%; min-height: 52px; border: 0; border-radius: 999px; background: var(--brand); color: var(--on-brand); font: 700 .98rem/1 var(--font-text); cursor: pointer; box-shadow: 0 10px 28px color-mix(in srgb, var(--brand) 38%, transparent); }
    .principal:disabled { opacity: .55; }
    .entrada { width: 100%; box-sizing: border-box; min-height: 46px; padding: 8px 14px; border-radius: 14px; border: 1px solid var(--vidrio-borde); background: color-mix(in srgb, var(--texto) 6%, transparent); color: inherit; font: inherit; }
    .enlace { border: 0; background: none; color: var(--acento); font: 600 .86rem/1.2 var(--font-text); cursor: pointer; padding: 8px 10px; min-height: 40px; }
    .error { margin: 0; color: var(--bad); font-size: .88rem; }
    @keyframes aparece { from { opacity: 0; } }
    @keyframes sube { from { opacity: 0; transform: translateY(18px) scale(.97); } }
    @keyframes late { 50% { transform: scale(1.1); } }
    @media (prefers-reduced-motion: reduce) { .velo, .tarjeta, .escaneando { animation: none; } }
  `,
})
export class Confirmar {
  protected readonly c = inject(ConfirmarService);
  protected readonly reauth = inject(Reautenticar);
  protected readonly clave = signal(false);
  protected readonly password = signal('');
  protected readonly codigo = signal('');
  protected readonly error = signal('');
  protected readonly ocupado = signal(false);

  constructor() {
    effect(() => {
      if (!this.c.abierto()) return;
      untracked(() => { this.clave.set(false); this.password.set(''); this.codigo.set(''); this.error.set(''); this.ocupado.set(false); });
    });
  }

  @HostListener('document:keydown.escape')
  protected cancelar(): void { if (this.c.abierto()) this.c.terminar(false); }

  protected async conCara(): Promise<void> {
    if (this.ocupado()) return;
    this.ocupado.set(true); this.error.set('');
    try { if (await this.reauth.confirmarConPasskey()) this.c.terminar(true); }
    catch (e) { this.error.set((e as HttpErrorResponse).status === 401 ? 'Ese passkey no sirve. Usa la contraseña.' : 'No se pudo confirmar con Face ID.'); this.clave.set(true); }
    finally { this.ocupado.set(false); }
  }

  protected async conClave(ev: Event): Promise<void> {
    ev.preventDefault();
    if (this.ocupado() || !this.password()) return;
    this.ocupado.set(true); this.error.set('');
    try { await this.reauth.confirmarConClave(this.password(), this.codigo()); this.c.terminar(true); }
    catch (e) { const x = e as HttpErrorResponse; this.error.set(x.error?.detail ?? x.error?.title ?? 'No se pudo confirmar.'); }
    finally { this.ocupado.set(false); }
  }
}
