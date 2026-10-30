import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export type ResultadoEntrar = 'ok' | 'credenciales' | 'segundo-factor' | 'bloqueado' | 'demasiados' | 'error';

// Sesión con cookie (HttpOnly, la pone la API): el front solo sabe si hay sesión y de quién.
@Injectable({ providedIn: 'root' })
export class SesionService {
  private readonly http = inject(HttpClient);
  private readonly email = signal<string | null>(null);
  private comprobada = false;

  readonly usuario = this.email.asReadonly();
  readonly dentro = computed(() => this.email() !== null);

  // Pregunta a la API una vez por carga; los guards la usan antes de entrar al panel
  async comprobar(): Promise<boolean> {
    if (this.comprobada) return this.dentro();
    try {
      const yo = await firstValueFrom(this.http.get<{ email: string }>('/api/sesion'));
      this.email.set(yo.email);
    } catch {
      this.email.set(null);
    }
    this.comprobada = true;
    return this.dentro();
  }

  async entrar(email: string, password: string, codigo?: string): Promise<ResultadoEntrar> {
    try {
      await firstValueFrom(this.http.post('/api/auth/login?useCookies=true', {
        email, password, ...(codigo ? { twoFactorCode: codigo } : {}),
      }));
      this.email.set(email);
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
    this.email.set(null);
  }

  // Si la API responde 401 a mitad de uso (sesión caducada)
  olvidar(): void {
    this.email.set(null);
  }
}
