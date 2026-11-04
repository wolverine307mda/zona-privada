import { ChangeDetectionStrategy, Component, booleanAttribute, input, output } from '@angular/core';

// Cuando algo falla al cargar: qué pasó y un «Reintentar». `reintentar` avisa a la pantalla, que vuelve a pedir los datos;
// mientras tanto pon `ocupado` y el botón se queda quieto (no se puede pulsar dos veces). Es un aviso (role="alert"): el lector de pantalla lo lee al aparecer.
@Component({
  selector: 'app-estado-error',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="aviso" role="alert">
      <span class="icono" aria-hidden="true">
        <svg class="icon" viewBox="0 0 24 24"><path d="M12 9v4M12 17h.01M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0z" /></svg>
      </span>
      <h3>{{ titulo() }}</h3>
      @if (mensaje()) { <p>{{ mensaje() }}</p> }
    </div>
    <button type="button" class="button button--sm button--ghost" [attr.aria-busy]="ocupado() || null" (click)="pulsar()">Reintentar</button>
  `,
  styles: `
    :host { display: grid; justify-items: center; align-content: center; gap: var(--esp-3); padding: var(--esp-6) var(--esp-4); min-height: var(--error-alto, 0); text-align: center; }
    .aviso { display: grid; justify-items: center; gap: var(--esp-2); }
    .icono {
      display: grid; place-items: center; width: 64px; height: 64px; margin-bottom: var(--esp-2); border-radius: 50%;
      color: var(--bad); background: color-mix(in srgb, var(--bad) 12%, transparent); border: 1px solid color-mix(in srgb, var(--bad) 30%, transparent);
    }
    .icono .icon { width: 28px; height: 28px; }
    h3 { margin: 0; font: 500 var(--step-lead)/1.2 var(--font-text); }
    p { max-width: 42ch; color: var(--soft); }
  `,
})
export class EstadoError {
  readonly titulo = input('No se ha podido cargar');
  readonly mensaje = input<string | null>(null);
  readonly ocupado = input(false, { transform: booleanAttribute });
  readonly reintentar = output<void>();

  protected pulsar(): void { if (!this.ocupado()) this.reintentar.emit(); }
}
