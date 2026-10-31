import { Component, DestroyRef, inject, signal } from '@angular/core';
import { NavigationCancel, NavigationEnd, NavigationError, NavigationStart, Router, RouterOutlet } from '@angular/router';
import { TemaService } from './core/tema.service';

// Raíz: la pantalla activa y la carga entre pantallas. Mientras el router espera (comprobar la sesión,
// bajar el código de la pantalla) se ve una barra arriba; en la primera carga, la marca en el centro,
// así nunca queda la pantalla en blanco ni se ven dos pantallas a la vez.
@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  template: `
    @if (primera()) {
      <div class="arranque" aria-live="polite" aria-label="Cargando">
        <span class="arranque__marca">MDA</span>
        <span class="arranque__barra"><span></span></span>
      </div>
    }
    <div class="progreso" [class.activo]="cargando()" aria-hidden="true"><span></span></div>
    <router-outlet />
  `,
  styles: `
    .progreso { position: fixed; z-index: 100; top: env(safe-area-inset-top); left: 0; right: 0; height: 3px; pointer-events: none; opacity: 0; transition: opacity 300ms ease 120ms; }
    .progreso.activo { opacity: 1; transition-delay: 120ms; }
    .progreso span { display: block; height: 100%; width: 40%; border-radius: 0 3px 3px 0; background: linear-gradient(90deg, transparent, var(--brand)); box-shadow: 0 0 12px var(--brand); animation: corre 1.1s var(--ease-in-out) infinite; }
    @keyframes corre { from { transform: translateX(-100%); } to { transform: translateX(260%); } }
    .arranque { position: fixed; inset: 0; z-index: 99; display: grid; place-content: center; justify-items: center; gap: 18px; background: var(--fondo); animation: aparece 400ms ease; }
    .arranque__marca { font: 700 clamp(2.4rem, 8vw, 4rem)/1 var(--font-display); letter-spacing: -0.05em; color: var(--texto); animation: late 1.6s ease-in-out infinite; }
    .arranque__barra { width: 120px; height: 4px; border-radius: 99px; background: var(--tarjeta-2); overflow: hidden; }
    .arranque__barra span { display: block; width: 45%; height: 100%; border-radius: inherit; background: var(--brand); animation: corre 1s var(--ease-in-out) infinite; }
    @keyframes late { 50% { opacity: 0.55; transform: scale(0.97); } }
    @keyframes aparece { from { opacity: 0; } }
  `,
})
export class App {
  // arranca el tema (modo, color y cambios del sistema) para toda la app
  protected readonly tema = inject(TemaService);
  protected readonly cargando = signal(false);
  protected readonly primera = signal(true);

  constructor() {
    const sub = inject(Router).events.subscribe((e) => {
      if (e instanceof NavigationStart) this.cargando.set(true);
      else if (e instanceof NavigationEnd || e instanceof NavigationCancel || e instanceof NavigationError) {
        this.cargando.set(false);
        this.primera.set(false);
      }
    });
    inject(DestroyRef).onDestroy(() => sub.unsubscribe());
  }
}
