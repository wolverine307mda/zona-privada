import { ChangeDetectionStrategy, Component, input } from '@angular/core';

export type VarianteTarjeta = 'vidrio' | 'plana' | 'negra' | 'acento';
export type RadioTarjeta = 'm' | 'l' | 'xl';

// La superficie de la app. Variantes:
//  · vidrio (por defecto): esmerilada, con borde fino y canto de luz; para casi todo.
//  · plana: sin desenfoque ni sombra, para una tarjeta dentro de otra.
//  · negra: alto contraste, para la métrica principal (en ella `--texto` y `--soft` pasan a claros, así que el contenido se adapta solo).
//  · acento: el vidrio teñido con el color de marca, para lo seleccionado o destacado.
// `radio`: m = 20, l = 28 (por defecto), xl = 36 px. `orden` escalona la entrada (n × --stagger): 0 la primera, 1 la siguiente…
// El relleno se cambia desde fuera con `--tarjeta-relleno`.
@Component({
  selector: 'app-tarjeta',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[attr.data-variante]': 'variante()', '[attr.data-radio]': 'radio()', '[style.--i]': 'orden()' },
  template: `<ng-content />`,
  styles: `
    :host {
      display: block; position: relative; padding: var(--tarjeta-relleno, var(--esp-5)); color: var(--texto);
      border: 1px solid var(--vidrio-borde); border-radius: var(--r, var(--radio-l));
      animation: entra var(--dur-3) var(--ease-expo) backwards; animation-delay: calc(var(--i, 0) * var(--stagger));
    }
    :host([data-radio='m']) { --r: var(--radio-m); }
    :host([data-radio='xl']) { --r: var(--radio-xl); }

    :host([data-variante='vidrio']) {
      background: var(--brillo), var(--vidrio); box-shadow: var(--sombra), var(--sombra-interior);
      backdrop-filter: var(--desenfoque); -webkit-backdrop-filter: var(--desenfoque);
    }
    :host([data-variante='plana']) { background: var(--tarjeta-2); border-color: transparent; }
    :host([data-variante='negra']) {
      --texto: var(--on-negra); --soft: var(--negra-soft); --linea: var(--negra-linea);
      --esq-base: rgba(255, 255, 255, 0.08); --esq-luz: color-mix(in srgb, var(--brand) 30%, rgba(255, 255, 255, 0.08)); /* el esqueleto de dentro, claro sobre negro */
      background: var(--negra); border-color: var(--negra-linea); box-shadow: var(--sombra-flota);
    }
    :host([data-variante='acento']) {
      background: linear-gradient(180deg, color-mix(in srgb, var(--brand) 18%, transparent), color-mix(in srgb, var(--brand) 8%, transparent)), var(--vidrio-fuerte);
      border-color: color-mix(in srgb, var(--brand) 40%, transparent); box-shadow: var(--sombra), var(--sombra-interior);
      backdrop-filter: var(--desenfoque); -webkit-backdrop-filter: var(--desenfoque);
    }
    @keyframes entra { from { opacity: 0; transform: translateY(14px) scale(0.985); } }
  `,
})
export class Tarjeta {
  readonly variante = input<VarianteTarjeta>('vidrio');
  readonly radio = input<RadioTarjeta>('l');
  readonly orden = input(0);
}
