import { Component, booleanAttribute, input } from '@angular/core';

// El reactor de carga estilo HUD (anillos que giran, partículas en órbita y una línea que barre). Sin `mini` es la pantalla de
// arranque entera; con `mini`, un reactor pequeño para poner dentro de una pantalla mientras llegan los datos. Los estilos son globales (styles.css).
@Component({
  selector: 'app-nucleo',
  template: `
    <div class="nucleo" [class.nucleo--mini]="mini()" role="status" [attr.aria-label]="texto()">
      <div class="nucleo__reactor" aria-hidden="true">
      <i class="r r1"></i><i class="r r2"></i><i class="r r3"></i>
      <i class="o" style="--i:-7%;--d:3.6s"></i><i class="o" style="--i:4%;--d:5.2s;animation-direction:reverse"></i><i class="o" style="--i:-3%;--d:4.1s;--e:-1.3s"></i><i class="o" style="--i:9%;--d:6.4s;--e:-2.6s;animation-direction:reverse"></i><i class="o" style="--i:-11%;--d:7.5s;--e:-4s"></i>
      <span class="nucleo__barrido"></span>
      <b class="nucleo__marca">MDA</b>
    </div>
      @if (!mini()) { <p class="nucleo__txt">{{ texto() }}<i>.</i><i>.</i><i>.</i></p> }
    </div>
  `,
})
export class Nucleo {
  readonly mini = input(false, { transform: booleanAttribute });
  readonly texto = input('Iniciando');
}
