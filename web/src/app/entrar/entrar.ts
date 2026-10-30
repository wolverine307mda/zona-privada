import { Component, ElementRef, OnDestroy, afterNextRender, inject, signal, viewChild } from '@angular/core';
import { Router } from '@angular/router';
import { ResultadoEntrar, SesionService } from '../core/sesion.service';
import { crearCandado } from './candado-3d';

const MENSAJES: Record<Exclude<ResultadoEntrar, 'ok' | 'segundo-factor'>, string> = {
  credenciales: 'Usuario o contraseña incorrectos.',
  bloqueado: 'Demasiados intentos fallidos: la cuenta está bloqueada 15 minutos.',
  demasiados: 'Vas muy rápido. Espera un minuto y vuelve a probar.',
  error: 'No hay conexión con el servidor. ¿Está encendido el equipo de casa?',
};

@Component({
  selector: 'app-entrar',
  templateUrl: './entrar.html',
  styleUrl: './entrar.css',
})
export class Entrar implements OnDestroy {
  private readonly sesion = inject(SesionService);
  private readonly router = inject(Router);
  private readonly canvas = viewChild.required<ElementRef<HTMLCanvasElement>>('escena');
  private escena?: ReturnType<typeof crearCandado>;

  protected readonly email = signal('');
  protected readonly password = signal('');
  protected readonly codigo = signal('');
  protected readonly pideCodigo = signal(false);
  protected readonly verClave = signal(false);
  protected readonly ocupado = signal(false);
  protected readonly error = signal('');
  protected readonly sacude = signal(false);

  constructor() {
    afterNextRender(() => {
      try { this.escena = crearCandado(this.canvas().nativeElement); } catch { /* sin WebGL: solo la tarjeta */ }
    });
  }

  protected valor(e: Event): string {
    return (e.target as HTMLInputElement).value;
  }

  protected async entrar(e: Event): Promise<void> {
    e.preventDefault();
    if (!this.email().trim() || !this.password()) { this.fallo('Escribe tu email y tu contraseña.'); return; }
    if (this.pideCodigo() && this.codigo().trim().length < 6) { this.fallo('Escribe el código de 6 cifras de tu app.'); return; }

    this.ocupado.set(true);
    this.error.set('');
    this.escena?.intento();
    const r = await this.sesion.entrar(this.email().trim(), this.password(), this.pideCodigo() ? this.codigo().trim() : undefined);
    this.ocupado.set(false);

    if (r === 'ok') {
      this.escena?.abrir();
      setTimeout(() => this.router.navigateByUrl('/panel'), 700);
    } else if (r === 'segundo-factor') {
      this.pideCodigo.set(true);
      this.error.set('Falta el código de tu app de autenticación.');
    } else {
      this.fallo(MENSAJES[r]);
    }
  }

  private fallo(texto: string): void {
    this.error.set(texto);
    this.sacude.set(false);
    requestAnimationFrame(() => this.sacude.set(true));
  }

  ngOnDestroy(): void {
    this.escena?.destruir();
  }
}
