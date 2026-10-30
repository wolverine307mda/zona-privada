import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { SesionService } from '../core/sesion.service';
import type { Conexion } from '../musica/musica';

interface Sesion { id: string; creada: string; ultimaVez: string; ip: string; app: boolean; dispositivo: string; navegador: string; tipo: string; enLinea: boolean; actual: boolean; }
interface Acceso { fecha: string; ip: string; exito: boolean; motivo: string; dispositivo: string; tipo: string; }
interface Estado { dosPasos: boolean; canales: { correo: boolean; whatsApp: boolean }; avisarInicios: boolean; fallosMes: number; }
interface DosPasos { sharedKey: string; recoveryCodesLeft: number; recoveryCodes: string[] | null; isTwoFactorEnabled: boolean; }

const ICONOS: Record<string, string> = {
  movil: 'M8 3h8v18H8zM11.5 18h1',
  tablet: 'M5 3h14v18H5zM11.5 18h1',
  ordenador: 'M4 5h16v11H4zM8 20h8M12 16v4',
  otro: 'M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18zM3 12h18M12 3c3 3 3 15 0 18M12 3c-3 3-3 15 0 18',
};

// Seguridad: dónde está abierta la sesión (y si está en línea), quién ha intentado entrar,
// la verificación en dos pasos, los avisos por correo y WhatsApp y las cuentas enlazadas.
@Component({
  selector: 'app-seguridad',
  templateUrl: './seguridad.html',
  styleUrls: ['../gastos/comun.css', './seguridad.css'],
})
export class Seguridad {
  private readonly http = inject(HttpClient);
  protected readonly usuario = inject(SesionService).usuario;

  protected readonly sesiones = signal<Sesion[]>([]);
  protected readonly accesos = signal<Acceso[]>([]);
  protected readonly estado = signal<Estado | null>(null);
  protected readonly conexiones = signal<Conexion[]>([]);
  protected readonly aviso = signal('');
  protected readonly ocupado = signal<string | null>(null);
  protected readonly copiado = signal<string | null>(null);
  protected readonly verTodos = signal(false);
  protected readonly iconos = ICONOS;

  // verificación en dos pasos
  protected readonly dos = signal<DosPasos | null>(null);
  protected readonly qr = signal<string | null>(null);
  protected readonly codigo = signal('');
  protected readonly codigos = signal<string[] | null>(null);
  protected readonly errorDos = signal('');
  protected readonly confirmarQuitar = signal(false);

  protected readonly enLinea = computed(() => this.sesiones().filter((s) => s.enLinea).length);
  protected readonly accesosVistos = computed(() => (this.verTodos() ? this.accesos() : this.accesos().slice(0, 8)));
  protected readonly clave = computed(() => this.dos()?.sharedKey?.match(/.{1,4}/g)?.join(' ') ?? '');
  protected readonly enlaceApp = computed(() => {
    const k = this.dos()?.sharedKey;
    const emisor = encodeURIComponent('MDA Zona privada');
    return k ? `otpauth://totp/${emisor}:${encodeURIComponent(this.usuario() ?? 'mda')}?secret=${k}&issuer=${emisor}&digits=6&period=30` : '';
  });

  constructor() {
    this.cargar();
    // la lista de sesiones se refresca sola para ver quién sigue en línea
    const t = setInterval(() => { if (!document.hidden) this.leerSesiones(); }, 30_000);
    inject(DestroyRef).onDestroy(() => clearInterval(t));
  }

  private async cargar(): Promise<void> {
    this.leerSesiones();
    const get = <T>(url: string) => firstValueFrom(this.http.get<T>(url));
    get<Acceso[]>('/api/seguridad/accesos').then((a) => this.accesos.set(a)).catch(() => {});
    get<Estado>('/api/seguridad/estado').then((e) => this.estado.set(e)).catch(() => {});
    get<Conexion[]>('/api/conexiones').then((c) => this.conexiones.set(c)).catch(() => {});
  }

  private async leerSesiones(): Promise<void> {
    try { this.sesiones.set(await firstValueFrom(this.http.get<Sesion[]>('/api/seguridad/sesiones'))); } catch { /* sin conexión */ }
  }

  protected hace(iso: string): string {
    const m = Math.round((Date.now() - new Date(iso).getTime()) / 60000);
    if (m < 1) return 'ahora mismo';
    if (m < 60) return `hace ${m} min`;
    if (m < 1440) return `hace ${Math.round(m / 60)} h`;
    return new Date(iso).toLocaleDateString('es-ES', { day: 'numeric', month: 'short' });
  }
  protected fecha(iso: string): string {
    return new Date(iso).toLocaleString('es-ES', { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' });
  }

  protected async cerrar(s: Sesion): Promise<void> {
    await firstValueFrom(this.http.delete(`/api/seguridad/sesiones/${s.id}`));
    this.sesiones.update((l) => l.filter((x) => x.id !== s.id));
  }

  protected async cerrarOtras(): Promise<void> {
    const r = await firstValueFrom(this.http.delete<{ cerradas: number }>('/api/seguridad/sesiones'));
    this.aviso.set(r.cerradas ? `Cerradas ${r.cerradas} sesiones. Esos equipos tendrán que volver a entrar.` : 'No había otras sesiones abiertas.');
    this.leerSesiones();
  }

  protected async avisarInicios(v: boolean): Promise<void> {
    this.estado.update((e) => (e ? { ...e, avisarInicios: v } : e));
    await firstValueFrom(this.http.put('/api/seguridad/ajustes', { avisarInicios: v }));
  }

  protected async probar(canal: 'correo' | 'whatsapp'): Promise<void> {
    this.ocupado.set(canal);
    try {
      await firstValueFrom(this.http.post(`/api/seguridad/avisos/prueba/${canal}`, null));
      this.aviso.set(canal === 'correo' ? 'Correo de prueba enviado: mira tu bandeja de entrada.' : 'WhatsApp de prueba enviado.');
    } catch (e) {
      this.aviso.set(e instanceof HttpErrorResponse ? e.error?.detail ?? 'No se pudo enviar.' : 'Sin conexión.');
    } finally { this.ocupado.set(null); }
  }

  protected conectar(id: string): void { location.href = `/api/conexiones/${id}/conectar`; }
  protected async desconectar(c: Conexion): Promise<void> {
    await firstValueFrom(this.http.delete(`/api/conexiones/${c.id}`));
    this.conexiones.update((l) => l.map((x) => (x.id === c.id ? { ...x, conectado: false, cuenta: null } : x)));
  }

  protected async copiar(t: string): Promise<void> {
    try { await navigator.clipboard.writeText(t); this.copiado.set(t); setTimeout(() => this.copiado.set(null), 1600); } catch { /* sin portapapeles */ }
  }

  // ---- verificación en dos pasos (la API de Identity: /api/auth/manage/2fa) ----
  private dosPasos(cuerpo: object) { return firstValueFrom(this.http.post<DosPasos>('/api/auth/manage/2fa', cuerpo)); }

  protected async empezarDosPasos(): Promise<void> {
    this.errorDos.set('');
    this.codigo.set('');
    const d = await this.dosPasos({});
    this.dos.set(d);
    // qrcode es CommonJS: según el empaquetado, sus funciones llegan en «default»
    const m = await import('qrcode');
    const qr = ((m as unknown as { default?: typeof m }).default ?? m);
    this.qr.set(await qr.toDataURL(this.enlaceApp(), { margin: 1, width: 240, color: { dark: '#05070a', light: '#ffffff' } }));
  }

  protected async activarDosPasos(e: Event): Promise<void> {
    e.preventDefault();
    this.errorDos.set('');
    try {
      const d = await this.dosPasos({ enable: true, twoFactorCode: this.codigo().replace(/\s/g, '') });
      this.dos.set(null);
      this.codigos.set(d.recoveryCodes);
      this.estado.update((x) => (x ? { ...x, dosPasos: true } : x));
    } catch {
      this.errorDos.set('Ese código no vale. Mira que la hora del móvil esté bien y prueba con el siguiente.');
    }
  }

  protected async nuevosCodigos(): Promise<void> {
    const d = await this.dosPasos({ resetRecoveryCodes: true });
    this.codigos.set(d.recoveryCodes);
  }

  protected async quitarDosPasos(): Promise<void> {
    if (!this.confirmarQuitar()) { this.confirmarQuitar.set(true); return; }
    await this.dosPasos({ enable: false, resetSharedKey: true });
    this.confirmarQuitar.set(false);
    this.codigos.set(null);
    this.estado.update((x) => (x ? { ...x, dosPasos: false } : x));
  }

  protected descargarCodigos(): void {
    const texto = `Códigos de recuperación de la zona privada MDA\nCada uno sirve una sola vez si pierdes el móvil.\n\n${(this.codigos() ?? []).join('\n')}\n`;
    const a = document.createElement('a');
    a.href = URL.createObjectURL(new Blob([texto], { type: 'text/plain' }));
    a.download = 'codigos-recuperacion-mda.txt';
    a.click();
    URL.revokeObjectURL(a.href);
  }
}
