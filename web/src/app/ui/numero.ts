import { ChangeDetectionStrategy, Component, booleanAttribute, computed, effect, input, signal, untracked } from '@angular/core';

// Lógica pura del número animado (se prueba en numero.spec.ts).
export const suavizar = (t: number): number => 1 - Math.pow(1 - Math.min(1, Math.max(0, t)), 4); // frena al llegar
export const interpolar = (desde: number, hasta: number, t: number): number => desde + (hasta - desde) * suavizar(t);
export const formatear = (v: number, decimales: number, idioma = 'es-ES'): string =>
  v.toLocaleString(idioma, { minimumFractionDigits: decimales, maximumFractionDigits: decimales });

const sinMovimiento = (): boolean => typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;

// Un número que, cuando cambia, rueda hasta el valor nuevo en vez de aparecer de golpe. La primera vez se pinta ya en su sitio (no cuenta desde 0)
// y con «movimiento reducido» salta directo. Las cifras son tabulares para que el ancho no baile mientras ruedan.
// Los lectores de pantalla no oyen la animación: leen solo el valor final, y solo se anuncia al cambiar si pones `anunciar`.
@Component({
  selector: 'app-numero',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span aria-hidden="true">{{ texto() }}</span><span class="sr" [attr.aria-live]="anunciar() ? 'polite' : null">{{ final() }}</span>`,
  styles: `:host { font-variant-numeric: tabular-nums; }`,
})
export class Numero {
  readonly valor = input.required<number>();
  readonly decimales = input(0);
  /** Milisegundos que tarda en rodar; 0 = sin animación. */
  readonly duracion = input(600);
  readonly anunciar = input(false, { transform: booleanAttribute });

  private readonly mostrado = signal<number | null>(null);
  protected readonly texto = computed(() => formatear(this.mostrado() ?? this.valor(), this.decimales()));
  protected readonly final = computed(() => formatear(this.valor(), this.decimales()));

  constructor() {
    effect((limpiar) => {
      const destino = this.valor();
      const duracion = this.duracion();
      const desde = untracked(this.mostrado);
      if (desde === null || desde === destino || duracion <= 0 || sinMovimiento()) { this.mostrado.set(destino); return; }
      const t0 = performance.now();
      let cuadro = 0;
      const paso = (ahora: number) => {
        const t = (ahora - t0) / duracion;
        this.mostrado.set(t >= 1 ? destino : interpolar(desde, destino, t));
        if (t < 1) cuadro = requestAnimationFrame(paso);
      };
      cuadro = requestAnimationFrame(paso);
      limpiar(() => cancelAnimationFrame(cuadro));
    });
  }
}
