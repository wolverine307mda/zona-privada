import { Component, ElementRef, inject, output, viewChild } from '@angular/core';
import { Instalar } from '../core/instalar';
import { SesionService } from '../core/sesion.service';
import { COLORES, MODOS, TemaService } from '../core/tema.service';
import { AjustesEntrada } from './ajustes-entrada';
import { AjustesFondo } from './ajustes-fondo';
import { AjustesAspecto } from './ajustes-aspecto';
import { AjustesTema } from './ajustes-tema';
import { AjustesSonido } from './ajustes-sonido';
import { AjustesEquipo } from './ajustes-equipo';
import { AjustesWeb } from './ajustes-web';

// Ajustes de la app en un modal: modo (día, noche, sistema), paleta de colores, fondo animado, sonido, el tema por defecto del portfolio, instalar la app y la sesión.
// Se cierra con Esc, con la X o pulsando fuera de la caja. `salir` avisa al marco de que se ha pedido cerrar la sesión.
@Component({
  selector: 'app-ajustes',
  imports: [AjustesEntrada, AjustesFondo, AjustesSonido, AjustesTema, AjustesAspecto, AjustesEquipo, AjustesWeb],
  templateUrl: './ajustes.html',
  styleUrl: './ajustes.css',
})
export class Ajustes {
  protected readonly tema = inject(TemaService);
  protected readonly instalar = inject(Instalar);
  protected readonly usuario = inject(SesionService).usuario;
  protected readonly colores = COLORES;
  protected readonly modos = MODOS;
  readonly salir = output<void>();
  private readonly dialogo = viewChild.required<ElementRef<HTMLDialogElement>>('ajustes');

  abrir(): void { this.dialogo().nativeElement.showModal(); }

  protected cerrarFuera(e: MouseEvent): void {
    if (e.target === this.dialogo().nativeElement) this.dialogo().nativeElement.close();
  }
}
