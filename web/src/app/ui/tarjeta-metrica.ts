import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { Esqueleto } from './esqueleto';
import { Numero, formatear } from './numero';
import { Tarjeta } from './tarjeta';
import { alturas, destacadas, sentido, textoVariacion } from './barras';

const FLECHAS = { baja: 'M12 5v14M6 13l6 6 6-6', sube: 'M12 19V5M6 11l6-6 6 6', igual: 'M5 12h14' };

// La tarjeta negra de la métrica principal (consumo, batería, gasto del mes): título con icono, el número grande y ligero con su unidad,
// un chip con la variación («−18 % vs ayer») y una mini gráfica de barras donde las más altas llevan rayado en el color de acento.
// Con `cargando` reserva el mismo hueco con esqueleto (la tarjeta no cambia de alto al llegar los datos); sin `valor` y sin cargar pone «—».
// El número rueda al actualizarse (app-numero). La variación se lee con signo, flecha y texto: no depende del color.
@Component({
  selector: 'app-tarjeta-metrica',
  imports: [Tarjeta, Esqueleto, Numero],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-tarjeta variante="negra" radio="xl" [orden]="orden()">
      <header>
        @if (icono()) { <span class="icono" aria-hidden="true"><svg class="icon" viewBox="0 0 24 24"><path [attr.d]="icono()" /></svg></span> }
        <h3>{{ titulo() }}</h3>
      </header>
      <div class="chip-hueco">
        @if (!cargando() && variacion() !== null) {
          <p class="chip"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path [attr.d]="flecha()" /></svg><span>{{ textoVar() }} {{ referencia() }}</span></p>
        }
      </div>
      <div class="valor">
        @if (cargando()) { <app-esqueleto forma="bloque" alto="var(--step-cifra)" ancho="55%" /> }
        @else if (valor() === null) { <p class="cifra">—<span class="sr">sin datos</span></p> }
        @else { <p class="cifra"><app-numero [valor]="valor()!" [decimales]="decimales()" />@if (unidad()) { <small>{{ unidad() }}</small> }</p> }
      </div>
      <div class="grafica">
        @if (cargando()) {
          <app-esqueleto forma="grafica" alto="96px" />
          @if (etiquetas().length) { <div class="eje" aria-hidden="true">&nbsp;</div> } <!-- reserva la fila de horas para que la tarjeta no cambie de alto al llegar los datos -->
        }
        @else if (barras().length) {
          <div class="barras" role="img" [attr.aria-label]="descripcion()">
            @for (h of altos(); track $index) { <i [class.destacada]="resaltadas().includes($index)" [style.height.%]="h" [style.--i]="$index"></i> }
          </div>
          @if (etiquetas().length) { <div class="eje" aria-hidden="true">@for (e of etiquetas(); track $index) { <span>{{ e }}</span> }</div> }
        }
      </div>
    </app-tarjeta>
  `,
  styles: `
    :host { display: block; }
    header { display: flex; align-items: center; gap: var(--esp-3); }
    .icono { display: grid; place-items: center; width: 44px; height: 44px; border-radius: 50%; background: color-mix(in srgb, var(--texto) 9%, transparent); }
    h3 { margin: 0; font: 500 var(--step-lead)/1.2 var(--font-text); }
    .chip-hueco { min-height: 30px; margin-top: var(--esp-3); }
    .chip {
      display: inline-flex; align-items: center; gap: 6px; padding: 4px 12px 4px 8px; border-radius: var(--radio-pildora);
      background: color-mix(in srgb, var(--texto) 10%, transparent); font-size: var(--step-small); line-height: 1.2;
    }
    .chip .icon { width: 16px; height: 16px; }
    .valor { min-height: var(--step-cifra); margin-top: var(--esp-4); }
    .valor .cifra { white-space: nowrap; }
    .grafica { margin-top: var(--esp-5); }
    .barras { display: flex; align-items: flex-end; gap: 5px; height: 96px; }
    .barras i {
      flex: 1; min-width: 0; border-radius: 4px 4px 2px 2px; background: color-mix(in srgb, var(--texto) 16%, transparent);
      transform-origin: bottom; animation: crece var(--dur-4) var(--ease-expo) backwards; animation-delay: calc(var(--i, 0) * 25ms);
    }
    .barras i.destacada { background: repeating-linear-gradient(135deg, var(--brand) 0 2px, transparent 2px 6px); box-shadow: inset 0 0 0 1px var(--brand); }
    .eje { display: flex; justify-content: space-between; margin-top: var(--esp-2); color: var(--soft); font-size: var(--step-small); font-variant-numeric: tabular-nums; }
    @keyframes crece { from { transform: scaleY(0); } }
  `,
})
export class TarjetaMetrica {
  readonly titulo = input.required<string>();
  readonly valor = input<number | null>(null);
  readonly unidad = input('');
  readonly decimales = input(1);
  /** Cambio respecto a la referencia, en %: −18 baja, 4,5 sube. */
  readonly variacion = input<number | null>(null);
  readonly decimalesVariacion = input(0);
  readonly referencia = input('vs ayer');
  readonly barras = input<readonly number[]>([]);
  readonly etiquetas = input<readonly string[]>([]);
  /** Cuántas barras (las más altas) llevan rayado. */
  readonly rayadas = input(2);
  readonly icono = input<string | null>(null);
  readonly cargando = input(false);
  readonly orden = input(0);

  protected readonly altos = computed(() => alturas(this.barras()));
  protected readonly resaltadas = computed(() => destacadas(this.barras(), this.rayadas()));
  protected readonly textoVar = computed(() => textoVariacion(this.variacion() ?? 0, this.decimalesVariacion()));
  protected readonly flecha = computed(() => FLECHAS[sentido(this.variacion() ?? 0, this.decimalesVariacion())]);
  protected readonly descripcion = computed(() => {
    const b = this.barras();
    return b.length ? `Gráfica de ${b.length} barras, de ${formatear(Math.min(...b), this.decimales())} a ${formatear(Math.max(...b), this.decimales())}` : '';
  });
}
