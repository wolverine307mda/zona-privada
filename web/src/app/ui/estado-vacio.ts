import { ChangeDetectionStrategy, Component, input } from '@angular/core';

// Cuando no hay nada que enseñar: un icono, qué falta y cómo empezar. Lo que pongas dentro (un botón, un enlace) es la acción.
// `icono` es el trazo SVG (viewBox 24×24, como en core/modulos.ts). Reserva un alto mínimo con `--vacio-alto` si la pantalla lo necesita para no saltar.
@Component({
  selector: 'app-estado-vacio',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span class="icono" aria-hidden="true">
      <svg class="icon" viewBox="0 0 24 24"><path [attr.d]="icono()" /></svg>
    </span>
    <h3>{{ titulo() }}</h3>
    @if (texto()) { <p>{{ texto() }}</p> }
    <div class="accion"><ng-content /></div>
  `,
  styles: `
    :host { display: grid; justify-items: center; align-content: center; gap: var(--esp-2); padding: var(--esp-6) var(--esp-4); min-height: var(--vacio-alto, 0); text-align: center; }
    .icono {
      display: grid; place-items: center; width: 64px; height: 64px; margin-bottom: var(--esp-2); border-radius: 50%;
      color: var(--acento); background: color-mix(in srgb, var(--brand) 12%, transparent); border: 1px solid color-mix(in srgb, var(--brand) 28%, transparent);
    }
    .icono .icon { width: 28px; height: 28px; }
    h3 { margin: 0; font: 500 var(--step-lead)/1.2 var(--font-text); }
    p { max-width: 38ch; color: var(--soft); }
    .accion:empty { display: none; }
    .accion { display: flex; flex-wrap: wrap; justify-content: center; gap: var(--esp-2); margin-top: var(--esp-3); }
  `,
})
export class EstadoVacio {
  readonly icono = input.required<string>();
  readonly titulo = input.required<string>();
  readonly texto = input<string | null>(null);
}
