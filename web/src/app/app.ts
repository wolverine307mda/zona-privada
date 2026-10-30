import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  template: '<router-outlet />',
})
export class App {
  constructor() {
    // el color elegido en la web pública no viaja (otro dominio): se guarda aquí aparte
    try {
      const color = localStorage.getItem('color');
      if (color) document.documentElement.dataset['color'] = color;
    } catch { /* sin almacenamiento: color por defecto */ }
  }
}
