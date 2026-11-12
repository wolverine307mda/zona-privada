import { Component, ElementRef, OnDestroy, afterNextRender, inject, signal, viewChild } from '@angular/core';
import { Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { CieloConfig } from '../core/cielo-config';
import { pedirCredencial, soportaCondicional, soportaPasskeys } from '../core/passkey';
import { ResultadoEntrar, SesionService } from '../core/sesion.service';
import { crearCandado } from './candado-3d';
import { CieloVivo } from './cielo-vivo';

const MENSAJES: Record<Exclude<ResultadoEntrar, 'ok' | 'segundo-factor'>, string> = {
  credenciales: 'Usuario o contraseña incorrectos.',
  bloqueado: 'Demasiados intentos fallidos: la cuenta está bloqueada 15 minutos.',
  demasiados: 'Vas muy rápido. Espera un minuto y vuelve a probar.',
  error: 'No hay conexión con el servidor. ¿Está encendido el equipo de casa?',
};

@Component({
  selector: 'app-entrar',
  imports: [CieloVivo],
  templateUrl: './entrar.html',
  styleUrl: './entrar.css',
})
export class Entrar implements OnDestroy {
  private readonly sesion = inject(SesionService);
  private readonly router = inject(Router);
  private readonly http = inject(HttpClient);
  protected readonly conPasskey = soportaPasskeys();
  protected readonly config = inject(CieloConfig);
  private readonly canvas = viewChild<ElementRef<HTMLCanvasElement>>('escena');
  private readonly cielo = viewChild<CieloVivo>('cielo');
  private escenaBase?: ReturnType<typeof crearCandado>;
  // lo que pasa en la pantalla se cuenta a la escena de fondo, sea el candado o el cielo
  private readonly escena = {
    intento: () => this.escenaBase?.intento(),
    abrir: () => { this.escenaBase?.abrir(); this.cielo()?.destello('ok'); },
    rechazar: () => { this.escenaBase?.rechazar(); this.cielo()?.destello('mal'); },
  };
  private aborto?: AbortController;

  protected readonly usuario = signal('');
  protected readonly password = signal('');
  protected readonly codigo = signal('');
  protected readonly pideCodigo = signal(false);
  protected readonly verClave = signal(false);
  protected readonly ocupado = signal(false);
  protected readonly entrando = signal(false);
  protected readonly error = signal('');
  protected readonly sacude = signal(false);
  // «en línea» solo si /api/salud contesta (con la app guardada por el service worker, la página puede cargar sin servidor)
  protected readonly servidor = signal<'comprobando' | 'en-linea' | 'apagado'>('comprobando');

  constructor() {
    afterNextRender(() => {
      void this.comprobarServidor();
      if (this.conPasskey) void this.entrarConPasskey(true);
      const lienzo = this.canvas()?.nativeElement;
      if (lienzo) { try { this.escenaBase = crearCandado(lienzo); } catch { /* sin WebGL: solo la tarjeta */ } }
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
    this.escena.intento();
    const r = await this.sesion.entrar(this.usuario().trim(), this.password(), this.pideCodigo() ? this.codigo().trim() : undefined);
    if (r === 'ok') {
      // el botón sigue «ocupado» hasta que el menú está cargado: con mala conexión no se puede pulsar dos veces ni parece colgado
      this.entrando.set(true);
      this.escena.abrir();
      setTimeout(() => this.router.navigateByUrl('/inicio'), 700);
      return;
    }
    this.ocupado.set(false);
    if (r === 'segundo-factor') {
      this.pideCodigo.set(true);
      this.error.set('Falta el código de tu app de autenticación.');
    } else {
      this.fallo(MENSAJES[r]);
    }
  }

  // Face ID / Windows Hello: el dispositivo guarda el passkey y solo hay que mirar a la cámara
  // `auto`: se intenta al abrir la pantalla, sin que hayas tocado nada; si no se puede, se calla y queda el botón
  protected async entrarConPasskey(auto = false): Promise<void> {
    if (this.ocupado()) return;
    this.aborto?.abort(); // el autocompletado pendiente cede ante la ventana de Face ID
    this.ocupado.set(true);
    this.error.set('');
    if (!auto) this.escena.intento();
    try {
      const opciones = await firstValueFrom(this.http.post<unknown>('/api/auth/passkey/entrada/opciones', null));
      await this.terminar(await pedirCredencial(opciones));
    } catch (e) {
      // cancelar el diálogo del sistema no es un error que haya que gritar
      const cancelado = e instanceof DOMException && (e.name === 'NotAllowedError' || e.name === 'AbortError');
      if (!cancelado && !auto) this.fallo('Ese passkey no sirve aquí. Entra con tu contraseña y registra uno nuevo en Seguridad.');
      if (cancelado && auto) void this.ofrecerEnElCampo(); // Safari pide un gesto: la llave sale al tocar el campo de usuario
    } finally {
      this.ocupado.set(false);
    }
  }

  private async terminar(credencial: unknown): Promise<void> {
    await firstValueFrom(this.http.post('/api/auth/passkey/entrada', { credencial }));
    if (await this.sesion.trasPasskey()) {
      this.entrando.set(true);
      this.escena.abrir();
      setTimeout(() => this.router.navigateByUrl('/inicio'), 700);
    }
  }

  // autocompletado condicional: la llave aparece como sugerencia en el campo de usuario, sin ventana aparte
  private async ofrecerEnElCampo(): Promise<void> {
    if (!(await soportaCondicional())) return;
    this.aborto = new AbortController();
    try {
      const opciones = await firstValueFrom(this.http.post<unknown>('/api/auth/passkey/entrada/opciones', null));
      await this.terminar(await pedirCredencial(opciones, { condicional: true, senal: this.aborto.signal }));
    } catch { /* cancelado o sin llave: se sigue con la contraseña */ }
  }

  private fallo(texto: string): void {
    this.error.set(texto);
    this.escena.rechazar(); // el candado se pone en rojo y se sacude
    this.sacude.set(false);
    requestAnimationFrame(() => this.sacude.set(true));
  }

  ngOnDestroy(): void {
    this.aborto?.abort();
    this.escenaBase?.destruir();
  }
}
