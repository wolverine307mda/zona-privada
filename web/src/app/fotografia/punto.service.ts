import { Injectable, signal } from '@angular/core';

// El sitio elegido en «Mis sitios» (o null para tu lugar de siempre): lo lee la pantalla del cielo para calcular desde allí.
@Injectable({ providedIn: 'root' })
export class PuntoService {
  readonly elegido = signal<number | null>(null);
}
