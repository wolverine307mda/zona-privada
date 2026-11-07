import { Component, inject } from '@angular/core';
import { TemaService } from '../core/tema.service';
import { TEMAS } from '../core/temas';

// «Tema» dentro de Ajustes: seis temas completos con su vista previa en vivo (cada tarjeta se pinta con las variables de su tema).
// Los de un solo modo (Papel, Terminal, Sepia, Alto contraste y OLED) fijan el claro u oscuro y desactivan el selector de modo. Se guarda en este dispositivo.
@Component({
  selector: 'app-ajustes-tema',
  template: `
    <section class="seccion" aria-labelledby="ajustes-tema">
      <h3 id="ajustes-tema">Tema</h3>
      <p class="nota">Cambia el aspecto entero: fondos, tarjetas, letra y bordes. El color de acento sigue siendo el que elijas.</p>
      <div class="temas" role="group" aria-labelledby="ajustes-tema">
        @for (t of temas; track t.id) {
          <button type="button" class="tema" [attr.aria-pressed]="tema.tema() === t.id" [attr.data-tema-id]="t.id" (click)="tema.ponerTema(t.id, $any($event.currentTarget))">
            <span class="tema__vista" aria-hidden="true"
                  [style.background]="t.vars['--fondo'] ?? 'var(--fondo)'" [style.border-color]="t.vars['--linea'] ?? 'var(--linea)'" [style.font-family]="t.vars['--font-text'] ?? 'inherit'">
              <span class="tema__tarjeta" [style.background]="t.vars['--tarjeta'] ?? 'var(--tarjeta)'" [style.color]="t.vars['--texto'] ?? 'var(--texto)'" [style.border-color]="t.vars['--linea'] ?? 'var(--linea)'">
                <b>Aa</b><i [style.background]="t.vars['--soft'] ?? 'var(--soft)'"></i><i [style.background]="t.vars['--soft'] ?? 'var(--soft)'"></i>
              </span>
            </span>
            <span class="tema__nombre">{{ t.nombre }}</span>
            <span class="tema__nota">{{ t.nota }}</span>
          </button>
        }
      </div>
      @if (tema.modoFijo()) { <p class="nota">Este tema es {{ tema.modoFijo() === 'dark' ? 'oscuro' : 'claro' }}: el selector de modo de arriba no cambia nada mientras lo uses.</p> }
    </section>
  `,
  styles: `
    :host { display: block; }
    .seccion { display: flex; flex-direction: column; gap: 10px; }
    h3 { font-size: 12px; font-weight: 500; text-transform: uppercase; letter-spacing: .08em; color: var(--soft); margin: 0; }
    .nota { margin: 0; color: var(--soft); font-size: .82rem; line-height: 1.5; }
    .temas { display: grid; grid-template-columns: repeat(auto-fill, minmax(124px, 1fr)); gap: 10px; }
    .tema { display: flex; flex-direction: column; align-items: stretch; gap: 4px; padding: 8px; text-align: left; border-radius: 14px; border: 2px solid var(--linea); background: transparent; color: var(--texto); font: inherit; cursor: pointer; min-height: 44px; }
    .tema[aria-pressed='true'] { border-color: var(--brand); }
    .tema:focus-visible { outline: 2px solid var(--acento); outline-offset: 2px; }
    .tema__vista { display: grid; place-items: center; height: 64px; border-radius: 8px; border: 1px solid; }
    .tema__tarjeta { display: grid; gap: 3px; width: 70%; padding: 6px 8px; border-radius: 6px; border: 1px solid; }
    .tema__tarjeta b { font-size: .9rem; } .tema__tarjeta i { display: block; height: 3px; border-radius: 2px; opacity: .8; } .tema__tarjeta i + i { width: 60%; }
    .tema__nombre { font-weight: 600; font-size: .85rem; } .tema__nota { color: var(--soft); font-size: .72rem; line-height: 1.3; }
  `,
})
export class AjustesTema {
  protected readonly tema = inject(TemaService);
  protected readonly temas = TEMAS;
}
