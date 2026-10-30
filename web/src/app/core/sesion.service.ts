import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export type ResultadoEntrar = 'ok' | 'credenciales' | 'segundo-factor' | 'bloqueado' | 'demasiados' | 'error';

// Sesión con cookie (HttpOnly, la pone la API): el front solo sabe si hay sesión y de quién.
// Se entra con el nombre de usuario; la API de Identity lo recibe en el campo «email».
@Injectable({ providedIn: 'root' })
export class SesionService {
  private readonly http = inject(HttpClient);
  private readonly nombre = signal<string | null>(null);
  private comprobada = false;

  readonly usuario = this.nombre.asReadonly();
  readonly dentro = computed(() => this.nombre() !== null);

  // Pregunta a la API una vez por carga; los guards la usan antes de entrar al panel
  async comprobar(): Promise<boolean> {
    if (this.comprobada) return this.dentro();
    try {
      const yo = await firstValueFrom(this.http.get<{ usuario: string }>('/api/sesion'));
      this.nombre.set(yo.usuario);
    } catch {
      this.nombre.set(null);
    }
    this.comprobada = true;
    return this.dentro();
  }

  async entrar(usuario: string, password: string, codigo?: string): Promise<ResultadoEntrar> {
    try {
      await firstValueFrom(this.http.post('/api/auth/login?useCookies=true', {
        email: usuario, password, ...(codigo ? { twoFactorCode: codigo } : {}),
      }));
      this.nombre.set(usuario);
      this.comprobada = true;
      return 'ok';
    } catch (e) {
      if (!(e instanceof HttpErrorResponse)) return 'error';
      if (e.status === 429) return 'demasiados';
      const detalle = String(e.error?.detail ?? '');
      if (detalle === 'RequiresTwoFactor') return 'segundo-factor';
      if (detalle === 'LockedOut') return 'bloqueado';
      return e.status === 401 ? 'credenciales' : 'error';
    }
  }

  async salir(): Promise<void> {
    try { await firstValueFrom(this.http.post('/api/sesion/salir', null)); } catch { /* la cookie ya no vale */ }
    this.nombre.set(null);
  }

  // Si la API responde 401 a mitad de uso (sesión caducada)
  olvidar(): void {
    this.nombre.set(null);
  }
}
