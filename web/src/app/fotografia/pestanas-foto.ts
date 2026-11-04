import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';

// Las secciones de Fotografía: el cielo de hoy, las previsiones especiales, tus sitios y diario, y las herramientas.
@Component({
  selector: 'app-pestanas-foto',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, RouterLinkActive],
  template: `
    <nav class="pf" aria-label="Secciones de fotografía">
      <a routerLink="/fotografia" routerLinkActive="is-activa" [routerLinkActiveOptions]="{ exact: true }" ariaCurrentWhenActive="page">Cielo</a>
      <a routerLink="/fotografia/previsiones" routerLinkActive="is-activa" ariaCurrentWhenActive="page">Previsiones</a>
      <a routerLink="/fotografia/sitios" routerLinkActive="is-activa" ariaCurrentWhenActive="page">Mis sitios</a>
      <a routerLink="/fotografia/herramientas" routerLinkActive="is-activa" ariaCurrentWhenActive="page">Herramientas</a>
    </nav>
  `,
  styles: `
    .pf { display: flex; gap: 6px; padding: 4px; margin: 0 0 14px; width: fit-content; max-width: 100%; overflow-x: auto; border-radius: 999px; background: color-mix(in srgb, var(--texto) 7%, transparent); scrollbar-width: none; }
    .pf::-webkit-scrollbar { display: none; }
    .pf a { flex: none; display: inline-flex; align-items: center; min-height: 40px; padding: 0 16px; border-radius: 999px; color: var(--soft); font: 600 14px/1 var(--font-text); text-decoration: none; transition: background 200ms, color 200ms; }
    .pf a.is-activa { background: var(--brand); color: var(--on-brand); }
    @media (hover: hover) { .pf a:not(.is-activa):hover { color: var(--texto); } }
    @media (prefers-reduced-motion: reduce) { .pf a { transition: none; } }
  `,
})
export class PestanasFoto {}
