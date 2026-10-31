import { HttpErrorResponse } from '@angular/common/http';
import { ErrorHandler, Injectable } from '@angular/core';

// Red de seguridad para las acciones que no tienen su propio mensaje de error (borrar, cerrar una sesión, desvincular…):
// si una petición falla y nadie la ha cogido, se avisa con un aviso breve en vez de no hacer nada.
// Los 401 ya los trata el interceptor (te lleva a entrar) y los 400 de validación los pintan los formularios.
@Injectable()
export class AvisoDeErrores implements ErrorHandler {
  private temporizador?: ReturnType<typeof setTimeout>;

  handleError(error: unknown): void {
    const e = error instanceof Error && 'rejection' in error ? (error as { rejection: unknown }).rejection : error;
    if (e instanceof HttpErrorResponse) {
      if (e.status === 401 || e.status === 400 || e.status === 404 || e.status === 423) return; // 423 = bloqueada: ya sale la pantalla de desbloqueo
      this.avisar(e.status === 0 ? 'El equipo no responde. Comprueba la conexión.' : e.status === 429 ? 'Demasiadas peticiones seguidas: espera un momento.' : 'No se pudo completar la acción. Inténtalo de nuevo.');
      return;
    }
    console.error(error);
  }

  private avisar(texto: string): void {
    let caja = document.getElementById('aviso-error');
    if (!caja) {
      caja = document.createElement('div');
      caja.id = 'aviso-error';
      caja.setAttribute('role', 'alert');
      caja.style.cssText = 'position:fixed;left:50%;bottom:calc(env(safe-area-inset-bottom,0px) + 84px);transform:translateX(-50%);z-index:9998;max-width:min(92vw,420px);padding:12px 16px;border-radius:14px;background:var(--tarjeta,#111);color:var(--texto,#fff);border:1px solid var(--bad,#e5484d);box-shadow:0 8px 30px rgba(0,0,0,.4);font:500 14px/1.35 Switzer,system-ui,sans-serif;text-align:center';
      document.body.append(caja);
    }
    caja.textContent = texto;
    caja.hidden = false;
    clearTimeout(this.temporizador);
    this.temporizador = setTimeout(() => { caja!.hidden = true; }, 4500);
  }
}
