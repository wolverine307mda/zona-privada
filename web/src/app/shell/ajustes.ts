import { Component, ElementRef, inject, output, viewChild } from '@angular/core';
import { Instalar } from '../core/instalar';
import { SesionService } from '../core/sesion.service';
import { COLORES, MODOS, TemaService } from '../core/tema.service';
import { AjustesFondo } from './ajustes-fondo';
import { AjustesSonido } from './ajustes-sonido';

// Ajustes de la app en un modal: modo (día, noche, sistema), paleta de colores, fondo animado, sonido, instalar la app y la sesión.
// Se cierra con Esc, con la X o pulsando fuera de la caja. `salir` avisa al marco de que se ha pedido cerrar la sesión.
@Component({
  selector: 'app-ajustes',
  imports: [AjustesFondo, AjustesSonido],
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
