import { ChangeDetectionStrategy, Component, input } from '@angular/core';

export type EstadoEtiqueta = 'ok' | 'aviso' | 'mal';

const TEXTO_ESTADO: Record<EstadoEtiqueta, string> = { ok: 'correcto', aviso: 'atención', mal: 'problema' };

// Píldora de vidrio con icono + nombre + dato («Puerta · Cerrada»), para anclar a un punto del objeto 3D protagonista.
// Con `x` e `y` (en % del contenedor, que debe ser `position: relative`) se coloca sola, centrada en ese punto.
// Sin ellas es una píldora normal. El estado se dice con un punto Y con texto para lectores de pantalla (nunca solo con color).
// No es pulsable: si lo tiene que ser, envuélvela en un <button> o <a> de 44 px como mínimo.
@Component({
  selector: 'app-etiqueta-flotante',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '[attr.data-anclada]': 'x() !== null && y() !== null',
    '[attr.data-icono]': 'icono() ? "" : null',
    '[style.left.%]': 'x()',
    '[style.top.%]': 'y()',
    '[style.--i]': 'orden()',
  },
  template: `
    @if (icono()) {
      <span class="icono" aria-hidden="true"><svg class="icon" viewBox="0 0 24 24"><path [attr.d]="icono()" /></svg></span>
    }
    <span class="texto">
      <strong>{{ nombre() }}</strong>
      @if (dato()) { <span class="dato">{{ dato() }}</span> }
      @if (estado(); as e) { <span class="punto" [attr.data-estado]="e" aria-hidden="true"></span><span class="sr">{{ textoEstado(e) }}</span> }
    </span>
  `,
  styles: `
    :host {
      display: inline-flex; align-items: center; gap: var(--esp-2); padding: 5px var(--esp-4) 5px 5px; max-width: 100%;
      border-radius: var(--radio-pildora); border: 1px solid var(--vidrio-borde); color: var(--texto); font-size: var(--step-small); white-space: nowrap;
      background: var(--brillo), var(--vidrio-fuerte); box-shadow: var(--sombra-flota);
      backdrop-filter: var(--desenfoque); -webkit-backdrop-filter: var(--desenfoque);
      animation: aparece var(--dur-3) var(--ease-expo) backwards; animation-delay: calc(var(--i, 0) * var(--stagger));
    }
    :host([data-anclada='true']) { position: absolute; transform: translate(-50%, -50%); }
    .icono { display: grid; place-items: center; flex: none; width: 32px; height: 32px; border-radius: 50%; background: color-mix(in srgb, var(--texto) 10%, transparent); }
    .icono .icon { width: 18px; height: 18px; }
    :host(:not([data-icono])) { padding-left: var(--esp-4); }
    .texto { display: inline-flex; align-items: baseline; gap: 6px; min-width: 0; }
    strong { font-weight: 500; }
    .dato { color: var(--soft); }
    .dato::before { content: '·'; margin-right: 6px; }
    .punto { align-self: center; width: 8px; height: 8px; border-radius: 50%; background: var(--ok); }
    .punto[data-estado='aviso'] { background: var(--warn); }
    .punto[data-estado='mal'] { background: var(--bad); }
    @keyframes aparece { from { opacity: 0; transform: translate(var(--tx, 0), var(--ty, 0)) scale(0.9); } }
    :host([data-anclada='true']) { --tx: -50%; --ty: -50%; }
  `,
})
export class EtiquetaFlotante {
  readonly nombre = input.required<string>();
  readonly dato = input<string | null>(null);
  /** Trazo SVG del icono (viewBox 24×24). */
  readonly icono = input<string | null>(null);
  readonly x = input<number | null>(null);
  readonly y = input<number | null>(null);
  readonly estado = input<EstadoEtiqueta | null>(null);
  readonly orden = input(0);

  protected textoEstado(e: EstadoEtiqueta): string { return TEXTO_ESTADO[e]; }
}
