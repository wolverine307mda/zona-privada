import { Component, computed, inject, signal } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map } from 'rxjs';
import { INICIO, MODULOS } from '../core/modulos';
import { SesionService } from '../core/sesion.service';
import { COLORES, MODOS, TemaService } from '../core/tema.service';
import { SistemaService } from '../inicio/sistema.service';

// Marco de la zona privada: menú lateral (secciones, tema y usuario), barra superior y la pantalla activa.
@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './shell.html',
  styleUrl: './shell.css',
})
export class Shell {
  private readonly sesion = inject(SesionService);
  private readonly router = inject(Router);
  protected readonly tema = inject(TemaService);
  protected readonly sistema = inject(SistemaService);

  protected readonly inicio = INICIO;
  protected readonly modulos = MODULOS;
  protected readonly colores = COLORES;
  protected readonly modos = MODOS;
  protected readonly usuario = this.sesion.usuario;
  protected readonly menuAbierto = signal(false);
  protected readonly ahora = signal(new Date());

  // título de la barra superior según la sección abierta
  private readonly url = toSignal(this.router.events.pipe(filter((e) => e instanceof NavigationEnd), map(() => this.router.url)), { initialValue: this.router.url });
  protected readonly seccion = computed(() => {
    const id = this.url().split('/').filter(Boolean).pop() ?? 'inicio';
    return [INICIO, ...MODULOS].find((m) => m.id === id) ?? INICIO;
  });

  constructor() {
    this.sistema.empezar();
    const reloj = setInterval(() => this.ahora.set(new Date()), 15_000);
    // cerrar el menú del móvil al navegar
    this.router.events.pipe(filter((e) => e instanceof NavigationEnd)).subscribe(() => this.menuAbierto.set(false));
    addEventListener('beforeunload', () => clearInterval(reloj), { once: true });
  }

  protected async salir(): Promise<void> {
    await this.sistema.parar();
    await this.sesion.salir();
    this.router.navigateByUrl('/entrar');
  }
}
