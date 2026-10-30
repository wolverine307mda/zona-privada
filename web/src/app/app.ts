import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { TemaService } from './core/tema.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  template: '<router-outlet />',
})
export class App {
  // arranca el tema (modo, color y cambios del sistema) para toda la app
  protected readonly tema = inject(TemaService);
}
