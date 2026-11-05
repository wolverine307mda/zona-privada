import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Aurora } from './aurora';
import { CalculadoraEstrellas } from './calculadora-estrellas';
import { DiarioSalidas } from './diario';
import { EventosCielo } from './eventos';
import { Iss } from './iss';
import { Niebla } from './niebla';
import { PestanasFoto } from './pestanas-foto';
import { Puntos } from './puntos';

// Las pantallas de Fotografía que no son el cielo de hoy; cada una junta tarjetas que van juntas.
@Component({
  selector: 'app-fotografia-previsiones',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PestanasFoto, Niebla, Aurora, EventosCielo, Iss],
  styleUrls: ['../gastos/comun.css'],
  template: `
    <header class="cab"><h2 class="cab__titulo">Previsiones especiales</h2><p class="cab__sub">Niebla, aurora, meteoros y eclipses</p></header>
    <app-pestanas-foto />
    <app-niebla />
    <app-aurora />
    <app-iss />
    <app-eventos-cielo />
  `,
})
export class Previsiones {}

@Component({
  selector: 'app-fotografia-sitios',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PestanasFoto, Puntos, DiarioSalidas],
  styleUrls: ['../gastos/comun.css'],
  template: `
    <header class="cab"><h2 class="cab__titulo">Mis sitios</h2><p class="cab__sub">Tus puntos favoritos y el diario de salidas</p></header>
    <app-pestanas-foto />
    <app-puntos />
    <app-diario-salidas />
  `,
})
export class Sitios {}

@Component({
  selector: 'app-fotografia-herramientas',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PestanasFoto, CalculadoraEstrellas, RouterLink],
  styleUrls: ['../gastos/comun.css'],
  template: `
    <header class="cab"><h2 class="cab__titulo">Herramientas</h2><p class="cab__sub">Para usar sobre el terreno</p></header>
    <app-pestanas-foto />
    <section class="caja">
      <header class="caja__cab"><h3>Sol y luna en AR</h3><span class="sub">con la cámara y la brújula del móvil</span></header>
      <p class="sub">Apunta el móvil al horizonte y ve por dónde pasan hoy el sol y la luna.</p>
      <a routerLink="/fotografia/ar" class="boton">Abrir la cámara</a>
    </section>
    <app-calculadora-estrellas />
  `,
})
export class Herramientas {}
