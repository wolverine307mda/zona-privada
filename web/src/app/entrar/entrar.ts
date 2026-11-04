import { Component, ElementRef, OnDestroy, afterNextRender, inject, signal, viewChild } from '@angular/core';
import { Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { pedirCredencial, soportaPasskeys } from '../core/passkey';
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
  private readonly http = inject(HttpClient);
  protected readonly conPasskey = soportaPasskeys();
  private readonly canvas = viewChild.required<ElementRef<HTMLCanvasElement>>('escena');
  private escena?: ReturnType<typeof crearCandado>;

  protected readonly usuario = signal('');
  protected readonly password = signal('');
  protected readonly codigo = signal('');
  protected readonly pideCodigo = signal(false);
  protected readonly verClave = signal(false);
  protected readonly ocupado = signal(false);
  protected readonly error = signal('');
  protected readonly sacude = signal(false);
  // «en línea» solo si /api/salud contesta (con la app guardada por el service worker, la página puede cargar sin servidor)
  protected readonly servidor = signal<'comprobando' | 'en-linea' | 'apagado'>('comprobando');

  constructor() {
    afterNextRender(() => {
      void this.comprobarServidor();
      try { this.escena = crearCandado(this.canvas().nativeElement); } catch { /* sin WebGL: solo la tarjeta */ }
    });
  }

  private async comprobarServidor(): Promise<void> {
    try {
      const r = await fetch('/api/salud', { cache: 'no-store', signal: AbortSignal.timeout(5000) });
      this.servidor.set(r.ok ? 'en-linea' : 'apagado');
    } catch {
      this.servidor.set('apagado');
    }
  }

  protected valor(e: Event): string {
    return (e.target as HTMLInputElement).value;
  }

  protected async entrar(e: Event): Promise<void> {
    e.preventDefault();
    if (!this.usuario().trim() || !this.password()) { this.fallo('Escribe tu usuario y tu contraseña.'); return; }
    if (this.pideCodigo() && this.codigo().trim().length < 6) { this.fallo('Escribe el código de 6 cifras de tu app.'); return; }

    this.ocupado.set(true);
    this.error.set('');
    this.escena?.intento();
    const r = await this.sesion.entrar(this.usuario().trim(), this.password(), this.pideCodigo() ? this.codigo().trim() : undefined);
    this.ocupado.set(false);

    if (r === 'ok') {
      this.escena?.abrir();
      setTimeout(() => this.router.navigateByUrl('/inicio'), 700);
    } else if (r === 'segundo-factor') {
      this.pideCodigo.set(true);
      this.error.set('Falta el código de tu app de autenticación.');
    } else {
      this.fallo(MENSAJES[r]);
    }
  }

  // Face ID / Windows Hello: el dispositivo guarda el passkey y solo hay que mirar a la cámara
  protected async entrarConPasskey(): Promise<void> {
    if (this.ocupado()) return;
    this.ocupado.set(true);
    this.error.set('');
    this.escena?.intento();
    try {
      const opciones = await firstValueFrom(this.http.post<unknown>('/api/auth/passkey/entrada/opciones', null));
      const credencial = await pedirCredencial(opciones);
      await firstValueFrom(this.http.post('/api/auth/passkey/entrada', { credencial }));
      if (await this.sesion.trasPasskey()) {
        this.escena?.abrir();
        setTimeout(() => this.router.navigateByUrl('/inicio'), 700);
      }
    } catch (e) {
      // cancelar el diálogo del sistema no es un error que haya que gritar
      const cancelado = e instanceof DOMException && (e.name === 'NotAllowedError' || e.name === 'AbortError');
      if (!cancelado) this.fallo('Ese passkey no sirve aquí. Entra con tu contraseña y registra uno nuevo en Seguridad.');
    } finally {
      this.ocupado.set(false);
    }
  }

  private fallo(texto: string): void {
    this.error.set(texto);
    this.escena?.rechazar(); // el candado se pone en rojo y se sacude
    this.sacude.set(false);
    requestAnimationFrame(() => this.sacude.set(true));
  }

  ngOnDestroy(): void {
    this.escena?.destruir();
  }
}
