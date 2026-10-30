import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MODULOS } from '../core/modulos';

// Página de una sección que aún no está hecha: qué será y qué traerá.
@Component({
  selector: 'app-pronto',
  imports: [RouterLink],
  template: `
    @if (modulo(); as m) {
      <section class="pronto tarjeta">
        <div class="pronto__icono" aria-hidden="true">
          <svg class="icon" viewBox="0 0 24 24"><path [attr.d]="m.icono" /></svg>
        </div>
        <p class="pronto__chip">Próximamente</p>
        <h2 class="pronto__titulo">{{ m.titulo }}</h2>
        <p class="pronto__resumen">{{ m.resumen }}</p>
        @if (m.planes?.length) {
          <ol class="pronto__planes">
            @for (p of m.planes; track p; let i = $index) {
              <li><code>{{ (i + 1).toString().padStart(2, '0') }}</code><span>{{ p }}</span></li>
            }
          </ol>
        }
        <a class="button button--ghost" routerLink="/inicio">Volver al inicio</a>
      </section>
    }
  `,
  styles: `
    .pronto { position: relative; overflow: hidden; display: flex; flex-direction: column; align-items: flex-start; gap: 16px; padding: clamp(28px, 5vw, 56px); max-width: 760px; }
    .pronto::after {
      content: ''; position: absolute; right: -60px; top: -60px; width: 260px; height: 260px; border-radius: 50%; pointer-events: none;
      background: radial-gradient(circle, color-mix(in srgb, var(--brand) 30%, transparent) 1.4px, transparent 1.7px) 0 0 / 14px 14px;
      mask: radial-gradient(circle, #000 30%, transparent 70%);
    }
    .pronto__icono { display: grid; place-items: center; width: 64px; height: 64px; border-radius: 18px; background: var(--brand); color: var(--on-brand); box-shadow: 5px 5px 0 color-mix(in srgb, var(--brand) 40%, var(--texto)); }
    .pronto__icono .icon { width: 30px; height: 30px; }
    .pronto__chip { margin-top: 8px; padding: 4px 12px; border-radius: 999px; font-size: 12px; font-weight: 500; letter-spacing: 0.06em; text-transform: uppercase; color: var(--acento); background: color-mix(in srgb, var(--brand) 14%, transparent); }
    .pronto__titulo { font: 700 clamp(2rem, 1.2rem + 3vw, 3.6rem)/0.95 var(--font-display); text-transform: uppercase; letter-spacing: -0.05em; }
    .pronto__resumen { color: var(--soft); font-size: var(--step-lead); }
    .pronto__planes { display: flex; flex-direction: column; gap: 10px; margin: 8px 0 12px; width: 100%; }
    .pronto__planes li { display: flex; gap: 14px; align-items: baseline; padding: 12px 14px; border-radius: 12px; background: var(--tarjeta-2); }
    .pronto__planes code { color: var(--acento); font-size: var(--step-small); }
  `,
})
export class Pronto {
  readonly id = input<string>('');
  protected readonly modulo = computed(() => MODULOS.find((m) => m.id === this.id()) ?? MODULOS[0]);
}
