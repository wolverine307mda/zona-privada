import { ChangeDetectionStrategy, Component, ElementRef, booleanAttribute, computed, input, model, viewChild } from '@angular/core';
import { Numero, formatear } from './numero';
import { ajustar, anguloDeFraccion, decimalesDe, fraccion, fraccionDePunto, marcasDelArco, trazoArco, valorDeFraccion } from './arco';

// Lienzo del dibujo (viewBox) y su centro; el arco va de `ini` a `fin` grados con 0° arriba (por defecto, 220° abiertos hacia arriba).
const ANCHO = 320, ALTO = 232, CX = 160, CY = 150, R_EXT = 132, R_INT = 112;
let siguiente = 0;

// Control radial: un arco de marcas finas con el valor enorme y ligero en el centro y botones − y + a los lados. Para temperatura, brillo, volumen…
// Úsalo con `[(valor)]`. Se maneja de tres formas: arrastrando sobre el arco, con − y + (44 px o más) y con el teclado
// (flechas ±paso, AvPág/RePág ±10 pasos, Inicio/Fin). Es un deslizador accesible (role="slider") con el valor leído con su unidad.
// Las marcas hasta el valor son del color de acento y brilla debajo un resplandor; el número rueda al cambiar (ver numero.ts).
@Component({
  selector: 'app-control-radial',
  imports: [Numero],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="control" [class.is-desactivado]="desactivado()">
      <svg #lienzo class="arco" [attr.viewBox]="'0 0 ' + ancho + ' ' + alto" role="slider" [attr.aria-label]="etiqueta()"
        [attr.aria-valuemin]="min()" [attr.aria-valuemax]="max()" [attr.aria-valuenow]="valor()" [attr.aria-valuetext]="textoValor()"
        [attr.aria-disabled]="desactivado() || null" [attr.tabindex]="desactivado() ? -1 : 0"
        (pointerdown)="empezar($event)" (pointermove)="mover($event)" (pointerup)="acabar()" (pointercancel)="acabar()" (keydown)="teclado($event)">
        <defs><filter [attr.id]="idFiltro" x="-30%" y="-30%" width="160%" height="160%"><feGaussianBlur stdDeviation="9" /></filter></defs>
        @if (f() > 0) { <path class="resplandor" [attr.d]="resplandor()" [attr.filter]="'url(#' + idFiltro + ')'" /> }
        @for (m of marcas(); track $index) {
          <line [attr.x1]="m.x1" [attr.y1]="m.y1" [attr.x2]="m.x2" [attr.y2]="m.y2" [class.activa]="m.activa" />
        }
      </svg>
      <p class="centro cifra">
        <app-numero [valor]="valor()" [decimales]="decimales()" [duracion]="160" />@if (unidad()) { <small>{{ unidad() }}</small> }
      </p>
      <button type="button" class="lado lado--menos" [attr.aria-label]="'Bajar ' + etiqueta()" [disabled]="desactivado() || valor() <= min()" (click)="sumar(-1)">
        <svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M5 12h14" /></svg>
      </button>
      <button type="button" class="lado lado--mas" [attr.aria-label]="'Subir ' + etiqueta()" [disabled]="desactivado() || valor() >= max()" (click)="sumar(1)">
        <svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M12 5v14M5 12h14" /></svg>
      </button>
    </div>
  `,
  styles: `
    :host { display: block; width: 100%; max-width: 380px; margin-inline: auto; }
    .control { position: relative; aspect-ratio: 320 / 232; container-type: inline-size; }
    .arco { position: absolute; inset: 0; width: 100%; height: 100%; touch-action: none; cursor: pointer; border-radius: var(--radio-l); -webkit-user-select: none; user-select: none; }
    .arco:focus-visible { outline-offset: 2px; }
    line { stroke: color-mix(in srgb, var(--texto) 26%, transparent); stroke-width: 1.6; stroke-linecap: round; transition: stroke var(--dur-2) var(--ease-out); }
    line.activa { stroke: var(--brand); stroke-width: 2.4; }
    .resplandor { fill: none; stroke: var(--brand); stroke-width: 18; stroke-linecap: round; opacity: 0.34; }
    .centro { position: absolute; left: 50%; top: 66%; translate: -50% -50%; font-size: clamp(2.6rem, 19cqw, 4.8rem); pointer-events: none; white-space: nowrap; }
    .lado {
      position: absolute; display: grid; place-items: center; width: 52px; height: 52px; padding: 0; border-radius: 50%; cursor: pointer; translate: -50% -50%;
      color: var(--texto); background: color-mix(in srgb, var(--texto) 14%, var(--tarjeta-2)); border: 1px solid var(--vidrio-borde); box-shadow: var(--sombra-pulsa);
      transition: transform var(--dur-1) var(--ease-out), background-color var(--dur-2) var(--ease-out);
    }
    .lado:active:not(:disabled) { transform: scale(0.92); }
    .lado:disabled { opacity: 0.4; cursor: not-allowed; }
    .lado--menos { left: 9.5%; top: 87%; }
    .lado--mas { left: 90.5%; top: 87%; }
    .is-desactivado .arco { cursor: not-allowed; }
    .is-desactivado { opacity: 0.55; }
    @media (hover: hover) { .lado:hover:not(:disabled) { background: color-mix(in srgb, var(--texto) 22%, var(--tarjeta-2)); } }
  `,
})
export class ControlRadial {
  readonly valor = model(0);
  readonly min = input(0);
  readonly max = input(100);
  readonly paso = input(1);
  readonly unidad = input('');
  /** Nombre accesible del control («Temperatura del salón»). */
  readonly etiqueta = input.required<string>();
  readonly desactivado = input(false, { transform: booleanAttribute });
  readonly ini = input(-110);
  readonly fin = input(110);
  readonly cantidadMarcas = input(61);

  protected readonly ancho = ANCHO;
  protected readonly alto = ALTO;
  protected readonly idFiltro = `radial-desenfoque-${++siguiente}`;
  private readonly lienzo = viewChild.required<ElementRef<SVGSVGElement>>('lienzo');
  private arrastrando = false;

  protected readonly f = computed(() => fraccion(this.valor(), this.min(), this.max()));
  protected readonly decimales = computed(() => decimalesDe(this.paso()));
  protected readonly textoValor = computed(() => formatear(this.valor(), this.decimales()) + (this.unidad() ? ` ${this.unidad()}` : ''));
  protected readonly marcas = computed(() => marcasDelArco(this.cantidadMarcas(), this.ini(), this.fin(), CX, CY, R_INT, R_EXT, this.f()));
  protected readonly resplandor = computed(() => trazoArco(CX, CY, R_INT - 4, this.ini(), anguloDeFraccion(this.f(), this.ini(), this.fin())));

  protected sumar(veces: number): void {
    if (this.desactivado()) return;
    this.valor.set(ajustar(this.valor() + veces * this.paso(), this.min(), this.max(), this.paso()));
  }

  protected empezar(e: PointerEvent): void {
    if (this.desactivado()) return;
    this.arrastrando = true;
    (e.currentTarget as Element).setPointerCapture(e.pointerId);
    this.poner(e);
    e.preventDefault();
  }
  protected mover(e: PointerEvent): void { if (this.arrastrando) this.poner(e); }
  protected acabar(): void { this.arrastrando = false; }

  private poner(e: PointerEvent): void {
    const r = this.lienzo().nativeElement.getBoundingClientRect();
    if (!r.width || !r.height) return;
    const f = fraccionDePunto(((e.clientX - r.left) * ANCHO) / r.width, ((e.clientY - r.top) * ALTO) / r.height, CX, CY, this.ini(), this.fin());
    this.valor.set(valorDeFraccion(f, this.min(), this.max(), this.paso()));
  }

  protected teclado(e: KeyboardEvent): void {
    if (this.desactivado()) return;
    const gran = 10;
    const acciones: Record<string, () => void> = {
      ArrowRight: () => this.sumar(1), ArrowUp: () => this.sumar(1), ArrowLeft: () => this.sumar(-1), ArrowDown: () => this.sumar(-1),
      PageUp: () => this.sumar(gran), PageDown: () => this.sumar(-gran), Home: () => this.valor.set(this.min()), End: () => this.valor.set(this.max()),
    };
    const accion = acciones[e.key];
    if (accion) { accion(); e.preventDefault(); }
  }
}
