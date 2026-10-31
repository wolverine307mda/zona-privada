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
  // true si la API no ha contestado (equipo apagado o sin red): no es lo mismo que «no hay sesión»
  readonly sinConexion = signal(false);

  // Pregunta a la API una vez por carga; los guards la usan antes de entrar al panel
  async comprobar(): Promise<boolean> {
    if (this.comprobada) return this.dentro();
    try {
      const yo = await firstValueFrom(this.http.get<{ usuario: string }>('/api/sesion'));
      this.nombre.set(yo.usuario);
      this.sinConexion.set(false);
      this.recordar(yo.usuario);
    } catch (e) {
      // 401 = de verdad sin sesión. Sin respuesta (0) o error del servidor/proxy: si ya entraste antes en este
      // dispositivo se te deja ver la app guardada (que avisa de que el equipo no responde) en vez de echarte al login
      const caida = e instanceof HttpErrorResponse && (e.status === 0 || e.status >= 500);
      const anterior = caida ? this.recordado() : null;
      if (anterior) {
        this.nombre.set(anterior);
        this.sinConexion.set(true);
        return true; // sin marcar «comprobada»: se vuelve a preguntar en la siguiente navegación
      }
      this.nombre.set(null);
      this.recordar(null);
    }
    this.comprobada = true;
    return this.dentro();
  }

  async entrar(usuario: string, password: string, codigo?: string): Promise<ResultadoEntrar> {
    try {
      await firstValueFrom(this.http.post('/api/auth/login?useCookies=true', {
        // 6 cifras: el código de la app; algo más largo: un código de recuperación
        email: usuario, password, ...(codigo ? (/^\d{6}$/.test(codigo.replace(/\s/g, '')) ? { twoFactorCode: codigo.replace(/\s/g, '') } : { twoFactorRecoveryCode: codigo }) : {}),
      }));
      this.nombre.set(usuario);
      this.sinConexion.set(false);
      this.recordar(usuario);
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
    this.recordar(null);
  }

  // Si la API responde 401 a mitad de uso (sesión caducada)
  olvidar(): void {
    this.nombre.set(null);
    this.recordar(null);
  }

  // solo el nombre de usuario (nada secreto): para no echarte al login cuando el equipo no responde
  private recordado(): string | null {
    try { return localStorage.getItem('mda-usuario'); } catch { return null; }
  }

  private recordar(usuario: string | null): void {
    try { usuario ? localStorage.setItem('mda-usuario', usuario) : localStorage.removeItem('mda-usuario'); } catch { /* sin almacenamiento */ }
  }
}
