import { ChangeDetectionStrategy, Component, booleanAttribute, input, model } from '@angular/core';

// El interruptor grande en píldora para encender o apagar cosas. Úsalo con `[(activo)]`. `etiqueta` es su nombre accesible («Luz del salón»);
// el texto visible lo pones tú al lado. El botón mide 44 px de alto como mínimo (la pista, 34). Con `ocupado` no se puede volver a pulsar
// (mientras la orden está en camino) y la bola late; `desactivado` lo apaga del todo. El estado se ve por la posición de la bola, no solo por el color.
@Component({
  selector: 'app-interruptor',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button type="button" role="switch" [attr.aria-checked]="activo()" [attr.aria-label]="etiqueta()" [attr.aria-busy]="ocupado() || null"
      [disabled]="desactivado()" (click)="alternar()">
      <span class="pista"><i class="bola"></i></span>
    </button>
  `,
  styles: `
    :host { display: inline-flex; }
    button {
      appearance: none; display: grid; place-items: center; min-width: 64px; min-height: var(--tactil); padding: 0; border: 0; background: none;
      border-radius: var(--radio-pildora); cursor: pointer; touch-action: manipulation;
    }
    button:disabled { cursor: not-allowed; opacity: 0.45; }
    button[aria-busy='true'] { cursor: progress; pointer-events: none; }
    .pista {
      position: relative; width: 60px; height: 34px; border-radius: var(--radio-pildora); border: 1px solid var(--vidrio-borde);
      background: color-mix(in srgb, var(--texto) 16%, var(--tarjeta-2)); box-shadow: var(--sombra-interior);
      transition: background-color var(--dur-2) var(--ease-out);
    }
    .bola {
      position: absolute; top: 3px; left: 3px; width: 26px; height: 26px; border-radius: 50%; background: #fff; box-shadow: var(--sombra-pulsa);
      transition: transform var(--dur-3) var(--ease-expo), width var(--dur-1) var(--ease-out);
    }
    button[aria-checked='true'] .pista { background: var(--brand); border-color: transparent; }
    button[aria-checked='true'] .bola { transform: translateX(26px); }
    /* al pulsar la bola se estira un poco, como en iOS */
    button:active:not(:disabled) .bola { width: 32px; }
    button[aria-checked='true']:active:not(:disabled) .bola { transform: translateX(20px); }
    button[aria-busy='true'] .bola { animation: late 1.1s ease-in-out infinite; }
    @keyframes late { 50% { opacity: 0.6; } }
  `,
})
export class Interruptor {
  readonly activo = model(false);
  readonly etiqueta = input.required<string>();
  readonly ocupado = input(false, { transform: booleanAttribute });
  readonly desactivado = input(false, { transform: booleanAttribute });

  protected alternar(): void {
    if (this.ocupado() || this.desactivado()) return;
    this.activo.update((v) => !v);
  }
}
