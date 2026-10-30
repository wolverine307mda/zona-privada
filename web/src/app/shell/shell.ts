import { Component, DestroyRef, ElementRef, computed, effect, inject, signal, viewChild } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map } from 'rxjs';
import { activarInclinacion } from '../core/inclinar';
import { INICIO, MODULOS } from '../core/modulos';
import { SesionService } from '../core/sesion.service';
import { COLORES, MODOS, TemaService } from '../core/tema.service';
import { SistemaService } from '../inicio/sistema.service';

// Marco de la zona privada: menú lateral (secciones, ajustes y usuario), barra superior y la pantalla activa.
@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './shell.html',
  styleUrl: './shell.css',
  host: {
    '(document:keydown.escape)': 'menuAbierto.set(false)',
    '(document:touchstart)': 'bordeGesto($event)',
  },
})
export class Shell {
  private readonly sesion = inject(SesionService);
  private readonly router = inject(Router);
  protected readonly tema = inject(TemaService);
  protected readonly sistema = inject(SistemaService);

  protected readonly inicio = INICIO;
  protected readonly listos = MODULOS.filter((m) => m.listo);
  protected readonly modulos = MODULOS.filter((m) => !m.listo);
  protected readonly colores = COLORES;
  protected readonly modos = MODOS;
  protected readonly usuario = this.sesion.usuario;
  protected readonly menuAbierto = signal(false);
  protected readonly ahora = signal(new Date());
  private readonly ajustes = viewChild.required<ElementRef<HTMLDialogElement>>('ajustes');

  // título de la barra superior según la sección abierta
  private readonly url = toSignal(this.router.events.pipe(filter((e) => e instanceof NavigationEnd), map(() => this.router.url)), { initialValue: this.router.url });
  protected readonly seccion = computed(() => {
    // /gastos/fijos -> gastos; /pronto/fotos -> fotos
    const partes = this.url().split(/[?#]/)[0].split('/').filter(Boolean);
    const id = partes[0] === 'pronto' ? partes[1] : partes[0] ?? 'inicio';
    return [INICIO, ...MODULOS].find((m) => m.id === id) ?? INICIO;
  });

  constructor() {
    this.sistema.empezar();
    const reloj = setInterval(() => this.ahora.set(new Date()), 15_000);
    const sinInclinar = activarInclinacion();
    // el gesto que empieza en el borde se sigue por todo el documento
    const mover = (e: TouchEvent) => this.sigueGesto(e);
    const acabar = () => {
      if (this.x0 != null && !this.menuAbierto() && this.dx > 60) this.menuAbierto.set(true);
      if (!this.menuAbierto()) this.x0 = null;
    };
    document.addEventListener('touchmove', mover, { passive: true });
    document.addEventListener('touchend', acabar);
    inject(DestroyRef).onDestroy(() => {
      clearInterval(reloj);
      sinInclinar();
      document.removeEventListener('touchmove', mover);
      document.removeEventListener('touchend', acabar);
    });
    // cerrar el menú del móvil al navegar
    this.router.events.pipe(filter((e) => e instanceof NavigationEnd)).subscribe(() => this.menuAbierto.set(false));
    // con el menú abierto, la página de detrás no se desplaza
    effect(() => document.documentElement.classList.toggle('menu-movil-abierto', this.menuAbierto()));
  }

  // ---- gestos del menú en el móvil: deslizar desde el borde izquierdo lo abre; hacia la izquierda, lo cierra ----
  private x0: number | null = null;
  private dx = 0;

  protected bordeGesto(e: TouchEvent): void {
    const t = e.touches[0];
    if (!this.menuAbierto() && t.clientX < 24 && innerWidth <= 960) { this.x0 = t.clientX; this.dx = 0; }
  }

  protected empiezaGesto(e: TouchEvent): void { this.x0 = e.touches[0].clientX; this.dx = 0; }

  protected sigueGesto(e: TouchEvent): void {
    if (this.x0 == null) return;
    this.dx = e.touches[0].clientX - this.x0;
  }

  protected acabaGesto(): void {
    if (this.x0 == null) return;
    if (this.menuAbierto() && this.dx < -60) this.menuAbierto.set(false);
    this.x0 = null;
  }

  // Ajustes en un modal: se cierra con Esc, con la X o pulsando fuera de la caja
  protected abrirAjustes(): void {
    this.menuAbierto.set(false);
    this.ajustes().nativeElement.showModal();
  }

  protected cerrarFuera(e: MouseEvent): void {
    if (e.target === this.ajustes().nativeElement) this.ajustes().nativeElement.close();
  }

  protected async salir(): Promise<void> {
    await this.sistema.parar();
    await this.sesion.salir();
    this.router.navigateByUrl('/entrar');
  }
}
