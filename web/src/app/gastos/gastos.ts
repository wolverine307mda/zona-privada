import { Component, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { DialogoFijo } from './dialogo-fijo';
import { DialogoMovimiento } from './dialogo-movimiento';
import { GastosService } from './gastos.service';
import { mesLargo } from './modelo';

// Página de gastos: el mes que se mira, los botones de apuntar y las pestañas
// (resumen, movimientos, fijos, gasolina, estadísticas). Los diálogos viven aquí para toda la sección.
@Component({
  selector: 'app-gastos',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, DialogoMovimiento, DialogoFijo],
  template: `
    <header class="cab">
      <div class="mes">
        <button class="mes__flecha" type="button" (click)="g.moverMes(-1)" aria-label="Mes anterior">
          <svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M15 6l-6 6 6 6" /></svg>
        </button>
        <h2 class="mes__nombre" aria-live="polite">{{ titulo() }}</h2>
        <button class="mes__flecha" type="button" (click)="g.moverMes(1)" aria-label="Mes siguiente">
          <svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M9 6l6 6-6 6" /></svg>
        </button>
        @if (!g.esMesActual()) { <button class="mes__hoy" type="button" (click)="g.irAHoy()">Hoy</button> }
      </div>
      <div class="acciones">
        <button class="button button--ghost accion-ingreso" type="button" (click)="g.editandoMovimiento.set({ tipo: 'Ingreso' })">
          <svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M12 19V5M6 11l6-6 6 6" /></svg> Ingreso
        </button>
        <button class="button" type="button" (click)="g.editandoMovimiento.set({ tipo: 'Gasto' })">
          <svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M12 5v14M5 12h14" /></svg> Gasto
        </button>
      </div>
    </header>

    <nav class="pestanas" aria-label="Secciones de gastos">
      <a routerLink="/gastos" routerLinkActive="is-activa" [routerLinkActiveOptions]="{ exact: true }" ariaCurrentWhenActive="page">Resumen</a>
      <a routerLink="/gastos/movimientos" routerLinkActive="is-activa" ariaCurrentWhenActive="page">Movimientos</a>
      <a routerLink="/gastos/fijos" routerLinkActive="is-activa" ariaCurrentWhenActive="page">Fijos y suscripciones</a>
      <a routerLink="/gastos/gasolina" routerLinkActive="is-activa" ariaCurrentWhenActive="page">Gasolina</a>
      <a routerLink="/gastos/estadisticas" routerLinkActive="is-activa" ariaCurrentWhenActive="page">Estadísticas</a>
    </nav>

    <router-outlet />

    <app-dialogo-movimiento />
    <app-dialogo-fijo />
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: 20px; }
    .cab { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 16px; }
    .mes { display: flex; align-items: center; gap: 10px; }
    .mes__nombre { min-width: 0; font: 700 clamp(1.5rem, 1rem + 2vw, 2.6rem)/1 var(--font-display); text-transform: uppercase; letter-spacing: -0.045em; }
    .mes__flecha { display: grid; place-items: center; width: 40px; height: 40px; border-radius: 12px; border: 1px solid var(--vidrio-borde); background: var(--vidrio); backdrop-filter: var(--desenfoque); color: var(--texto); cursor: pointer; transition: border-color 160ms ease, transform 140ms var(--ease-out); }
    .mes__flecha:active { transform: scale(0.92); }
    @media (hover: hover) { .mes__flecha:hover { border-color: var(--brand); } }
    .mes__hoy { min-height: 32px; padding: 0 12px; border-radius: 999px; border: 0; background: color-mix(in srgb, var(--brand) 16%, transparent); color: var(--acento); font-weight: 500; cursor: pointer; }
    .acciones { display: flex; gap: 10px; }
    .acciones .button { min-height: 46px; padding: 0 20px; }
    .acciones .icon { width: 18px; height: 18px; }
    .accion-ingreso { border-color: color-mix(in srgb, var(--ok) 50%, var(--linea)); }
    .pestanas { display: flex; gap: 4px; padding: 4px; border-radius: 14px; background: var(--vidrio); backdrop-filter: var(--desenfoque); border: 1px solid var(--vidrio-borde); overflow-x: auto; scrollbar-width: none; }
    .pestanas a { flex: none; padding: 10px 16px; border-radius: 10px; color: var(--soft); text-decoration: none; font-weight: 500; white-space: nowrap; transition: background-color 160ms ease, color 160ms ease; }
    .pestanas a.is-activa { background: var(--brand); color: var(--on-brand); }
    @media (hover: hover) { .pestanas a:not(.is-activa):hover { color: var(--texto); background: var(--tarjeta-2); } }
    @media (max-width: 600px) { .acciones { width: 100%; } .acciones .button { flex: 1; } }
  `,
})
export class Gastos {
  protected readonly g = inject(GastosService);
  protected readonly titulo = computed(() => mesLargo(this.g.anio(), this.g.mes()));
}
