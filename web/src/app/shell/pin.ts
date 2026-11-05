import { Component, HostListener, effect, inject, signal, untracked } from '@angular/core';
import { PinService } from '../core/pin.service';

const TECLAS = ['1', '2', '3', '4', '5', '6', '7', '8', '9'];

// Teclado del PIN de Seguridad: cuatro casillas que se rellenan número a número, teclado grande para el dedo (y el teclado físico),
// sacudida si falla, destello al acertar y cuenta atrás si se bloquea la sesión tras cinco fallos.
@Component({
  selector: 'app-pin',
  template: `
    @if (pin.abierto()) {
      <div class="velo" role="dialog" aria-modal="true" aria-labelledby="pin-titulo" (click)="cancelar()">
        <div class="tarjeta" [class.tiembla]="temblor()" [class.acierto]="acierto()" (click)="$event.stopPropagation()">
          <span class="candado" aria-hidden="true"><svg class="icon" viewBox="0 0 24 24"><path d="M7 11V8a5 5 0 0 1 10 0v3M5 11h14v10H5zM12 15v2"/></svg></span>
          <h2 id="pin-titulo">PIN de Seguridad</h2>
          <p class="aviso" [class.aviso--mal]="mal()" role="status" aria-live="polite">{{ mensaje() }}</p>
          <div class="casillas" aria-hidden="true">
            @for (i of cuatro; track i) { <i [class.llena]="digitos().length > i" [class.activa]="digitos().length === i && !bloqueado()"></i> }
          </div>
          <div class="teclado" role="group" aria-label="Teclado numérico">
            @for (t of teclas; track t) { <button type="button" (click)="pulsar(t)" [disabled]="bloqueado() || ocupado()">{{ t }}</button> }
            <button type="button" class="suave" (click)="cancelar()">Cancelar</button>
            <button type="button" (click)="pulsar('0')" [disabled]="bloqueado() || ocupado()">0</button>
            <button type="button" class="suave" (click)="borrar()" [disabled]="bloqueado() || ocupado() || !digitos().length" aria-label="Borrar la última cifra">
              <svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M21 5H9l-6 7 6 7h12zM15 9.5l-4 5M11 9.5l4 5"/></svg>
            </button>
          </div>
        </div>
      </div>
    }
  `,
  styles: `
    .velo { position: fixed; inset: 0; z-index: 9000; display: grid; place-items: center; padding: calc(var(--seguro-arriba) + 8px) 20px 20px; background: color-mix(in srgb, #000 55%, transparent); backdrop-filter: blur(10px); animation: aparece var(--dur-2, 240ms) ease-out both; }
    .tarjeta { display: grid; gap: 12px; justify-items: center; width: min(92vw, 340px); padding: 28px 22px 22px; text-align: center; border-radius: 30px; border: 1px solid var(--vidrio-borde); background: var(--brillo), var(--vidrio-fuerte); backdrop-filter: blur(26px) saturate(1.5); box-shadow: 0 30px 80px rgba(0, 0, 0, .4); animation: sube var(--dur-3, 420ms) var(--ease-out, ease-out) both; }
    .tiembla { animation: tiembla .45s ease; }
    .candado { display: grid; place-items: center; width: 54px; height: 54px; border-radius: 50%; background: color-mix(in srgb, var(--brand) 18%, transparent); color: var(--brand); transition: background var(--dur-2, 240ms), color var(--dur-2, 240ms), transform var(--dur-2, 240ms); }
    .acierto .candado { background: var(--brand); color: var(--on-brand); transform: scale(1.12); }
    .icon { width: 24px; height: 24px; fill: none; stroke: currentColor; stroke-width: 1.8; stroke-linecap: round; stroke-linejoin: round; }
    h2 { margin: 0; font: 600 1.1rem/1.2 var(--font-text); }
    .aviso { margin: 0; min-height: 2.6em; color: var(--soft); font-size: .88rem; max-width: 28ch; }
    .aviso--mal { color: var(--bad); }
    .casillas { display: flex; gap: 14px; margin: 4px 0 6px; }
    .casillas i { width: 16px; height: 16px; border-radius: 50%; border: 2px solid color-mix(in srgb, var(--texto) 35%, transparent); transition: transform var(--dur-1, 160ms), background var(--dur-1, 160ms), border-color var(--dur-1, 160ms); }
    .casillas i.activa { border-color: var(--brand); }
    .casillas i.llena { background: var(--brand); border-color: var(--brand); transform: scale(1.15); }
    .acierto .casillas i { background: #3fb96a; border-color: #3fb96a; }
    .teclado { display: grid; grid-template-columns: repeat(3, 1fr); gap: 10px; width: 100%; }
    .teclado button { display: grid; place-items: center; min-height: 58px; border: 1px solid var(--vidrio-borde); border-radius: 20px; background: color-mix(in srgb, var(--texto) 6%, transparent); color: inherit; font: 600 1.4rem/1 var(--font-display); cursor: pointer; transition: transform var(--dur-1, 160ms), background var(--dur-1, 160ms), border-color var(--dur-1, 160ms); -webkit-tap-highlight-color: transparent; }
    .teclado button:hover:not(:disabled), .teclado button:focus-visible { border-color: var(--brand); background: color-mix(in srgb, var(--brand) 14%, transparent); }
    .teclado button:active:not(:disabled) { transform: scale(.94); }
    .teclado button:disabled { opacity: .4; }
    .teclado .suave { font: 600 .84rem/1 var(--font-text); color: var(--soft); background: none; border-color: transparent; }
    @keyframes aparece { from { opacity: 0; } }
    @keyframes sube { from { opacity: 0; transform: translateY(18px) scale(.97); } }
    @keyframes tiembla { 20% { transform: translateX(-9px); } 40% { transform: translateX(8px); } 60% { transform: translateX(-5px); } 80% { transform: translateX(3px); } }
    @media (prefers-reduced-motion: reduce) { .velo, .tarjeta, .tiembla { animation: none; } }
  `,
})
export class Pin {
  protected readonly pin = inject(PinService);
  protected readonly teclas = TECLAS;
  protected readonly cuatro = [0, 1, 2, 3];
  protected readonly digitos = signal('');
  protected readonly mensaje = signal('');
  protected readonly mal = signal(false);
  protected readonly temblor = signal(false);
  protected readonly acierto = signal(false);
  protected readonly ocupado = signal(false);
  protected readonly bloqueado = signal(false);

  constructor() {
    // cada vez que se abre, se empieza de cero
    effect(() => {
      if (!this.pin.abierto()) return;
      untracked(() => { this.digitos.set(''); this.mensaje.set('Escribe tus cuatro cifras.'); this.mal.set(false); this.acierto.set(false); this.bloqueado.set(false); this.ocupado.set(false); });
    });
  }

  @HostListener('document:keydown', ['$event'])
  protected tecla(e: KeyboardEvent): void {
    if (!this.pin.abierto() || e.ctrlKey || e.metaKey || e.altKey) return;
    if (/^\d$/.test(e.key)) { e.preventDefault(); this.pulsar(e.key); }
    else if (e.key === 'Backspace') { e.preventDefault(); this.borrar(); }
    else if (e.key === 'Escape') { e.preventDefault(); this.cancelar(); }
  }

  protected pulsar(n: string): void {
    if (this.bloqueado() || this.ocupado() || this.digitos().length >= 4) return;
    this.mal.set(false);
    this.digitos.update((d) => d + n);
    if (this.digitos().length === 4) void this.comprobar();
  }

  protected borrar(): void { if (!this.ocupado()) this.digitos.update((d) => d.slice(0, -1)); }
  protected cancelar(): void { this.pin.terminar(false); }

  private async comprobar(): Promise<void> {
    this.ocupado.set(true);
    const r = await this.pin.probar(this.digitos());
    if (r.ok) {
      this.acierto.set(true); this.mensaje.set('Listo');
      setTimeout(() => this.pin.terminar(true), 380);
      return;
    }
    this.ocupado.set(false);
    this.digitos.set('');
    this.mal.set(true);
    this.bloqueado.set(!!r.bloqueado);
    this.mensaje.set(r.bloqueado ? (r.detalle ?? 'Demasiados intentos.') : `${r.detalle ?? 'PIN incorrecto.'}${r.restantes !== undefined ? ` Te quedan ${r.restantes}.` : ''}`);
    this.temblor.set(true);
    setTimeout(() => this.temblor.set(false), 480);
  }
}
