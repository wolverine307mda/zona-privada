import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

export type FormaEsqueleto = 'linea' | 'bloque' | 'circulo' | 'grafica';

// Alturas fijas de las barras de la gráfica de carga (no cambian entre cargas, para que no «baile»).
const BARRAS = [38, 62, 46, 78, 54, 90, 66, 48, 72, 58, 84, 44];

// El hueco que se pinta mientras llegan los datos, con un barrido de luz del color de acento. Reserva el alto final: ponle el mismo `alto`
// que tendrá el contenido y la pantalla no salta al llegar. Es decorativo (aria-hidden): marca `aria-busy="true"` en el contenedor que carga.
//  · linea: `lineas` renglones de texto (el último, más corto) · bloque: un rectángulo de `alto` × `ancho`
//  · circulo: un círculo de `alto` (y `ancho`) · grafica: barras que suben desde abajo, en `alto`.
// Con «movimiento reducido» el barrido se cambia por un latido lento (ver styles.css, que lo deja pasar).
@Component({
  selector: 'app-esqueleto',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { 'aria-hidden': 'true', '[attr.data-forma]': 'forma()' },
  template: `
    @switch (forma()) {
      @case ('linea') {
        @for (n of renglones(); track n) { <i class="pieza" [style.width]="n === renglones().length && n > 1 ? '60%' : ancho()" [style.height]="alto() ?? '12px'"></i> }
      }
      @case ('grafica') {
        <div class="grafica" [style.height]="alto() ?? '120px'">
          @for (h of barras; track $index) { <i class="pieza" [style.height.%]="h"></i> }
        </div>
      }
      @default { <i class="pieza" [style.width]="ancho() ?? (forma() === 'circulo' ? alto() ?? '48px' : '100%')" [style.height]="alto() ?? (forma() === 'circulo' ? '48px' : '96px')"></i> }
    }
  `,
  styles: `
    :host { display: grid; gap: 10px; }
    :host([data-forma='circulo']) { width: max-content; }
    .pieza {
      display: block; border-radius: 6px;
      background: linear-gradient(100deg, var(--esq-base) 30%, var(--esq-luz) 50%, var(--esq-base) 70%) 0 0 / 300% 100%;
      animation: brilla 1.5s ease-in-out infinite;
    }
    :host([data-forma='bloque']) .pieza { border-radius: var(--radio-m); }
    :host([data-forma='circulo']) .pieza { border-radius: 50%; }
    .grafica { display: flex; align-items: flex-end; gap: 6px; }
    .grafica .pieza { flex: 1; border-radius: 4px 4px 2px 2px; }
    @keyframes brilla { from { background-position: 100% 0; } to { background-position: 0 0; } }
    @keyframes late { 50% { opacity: 0.55; } }
    @media (prefers-reduced-motion: reduce) { .pieza { background-position: 50% 0; animation: late 2.4s ease-in-out infinite; } }
  `,
})
export class Esqueleto {
  readonly forma = input<FormaEsqueleto>('linea');
  /** Alto en CSS («64px», «6rem»…). Si no se pone, cada forma trae el suyo. */
  readonly alto = input<string | null>(null);
  readonly ancho = input<string | null>(null);
  /** Solo para `linea`: cuántos renglones. */
  readonly lineas = input(3);

  protected readonly barras = BARRAS;
  protected readonly renglones = computed(() => Array.from({ length: Math.max(1, this.lineas()) }, (_, i) => i + 1));
}
