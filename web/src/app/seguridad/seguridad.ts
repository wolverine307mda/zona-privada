import { GloboSondas } from './globo-sondas';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { soportaPasskeys } from '../core/passkey';
import { Reautenticar } from '../core/reautenticar';
import { Atajos } from './atajos';
import { Claves } from './claves';
import { Llaves } from './llaves';
import { PinAjustes } from './pin-ajustes';
import { Notificaciones } from '../core/notificaciones';
import { SesionService } from '../core/sesion.service';
import type { Conexion } from '../musica/musica';
import { RecargaService } from '../core/recarga';
import { Esqueleto } from '../ui';

interface Sesion { id: string; creada: string; ultimaVez: string; ip: string; app: boolean; dispositivo: string; navegador: string; tipo: string; enLinea: boolean; actual: boolean; }
interface Acceso { fecha: string; ip: string; exito: boolean; motivo: string; dispositivo: string; tipo: string; }
interface Estado {
  dosPasos: boolean; canales: { correo: boolean; whatsApp: boolean; telegram?: boolean }; avisarInicios: boolean; fallosMes: number;
  resumenSemanal: boolean; dispositivosPush: number; copia: { vigilada: boolean; fecha: string | null; tamano: number; nombre: string | null };
}
interface Telegram { configurado: boolean; vinculado: boolean; nombre: string | null; bot: string | null; }
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
  imports: [Atajos, Claves, GloboSondas, Llaves, PinAjustes, Esqueleto],
  templateUrl: './seguridad.html',
  styleUrls: ['../gastos/comun.css', './seguridad.css'],
})
export class Seguridad {
  // las secciones de la pantalla; se recuerda la última que miraste
  protected readonly pestana = signal<'cuenta' | 'vigilancia' | 'claves'>(((): 'cuenta' | 'vigilancia' | 'claves' => {
    try { const g = localStorage.getItem('mda.seg.pestana'); return g === 'vigilancia' || g === 'claves' ? g : 'cuenta'; } catch { return 'cuenta'; }
  })());
  protected ponerPestana(p: 'cuenta' | 'vigilancia' | 'claves'): void {
    this.pestana.set(p);
    try { localStorage.setItem('mda.seg.pestana', p); } catch { /* sin almacenamiento */ }
  }

  private readonly http = inject(HttpClient);
  protected readonly usuario = inject(SesionService).usuario;
  protected readonly push = inject(Notificaciones);

  protected readonly sesiones = signal<Sesion[]>([]);
  protected readonly accesos = signal<Acceso[]>([]);
  protected readonly estado = signal<Estado | null>(null);
  protected readonly conexiones = signal<Conexion[]>([]);
  protected readonly aviso = signal('');
  protected readonly ocupado = signal<string | null>(null);
  protected readonly copiado = signal<string | null>(null);
  protected readonly verTodos = signal(false);
  protected readonly iconos = ICONOS;
  protected readonly conPasskey = soportaPasskeys();
  // «pedir Face ID al volver a la app»: ajuste de este dispositivo
  private readonly sesionSvc = inject(SesionService);
  protected readonly bloqueoSeg = signal<number>(this.sesionSvc.bloqueoSegundos() ?? 0);
  protected readonly opcionesBloqueo = [
    { s: 0, t: 'No pedirlo' }, { s: 10, t: 'Al volver (tras 10 s fuera)' }, { s: 60, t: 'Tras 1 minuto fuera' }, { s: 300, t: 'Tras 5 minutos fuera' },
  ];
  protected readonly pedirClaveBloqueo = signal(false);
  protected readonly claveBloqueo = signal('');
  protected readonly codigoBloqueo = signal('');
  protected readonly errorBloqueo = signal('');
  protected readonly origen = location.origin;
  protected readonly senuelos = signal<{ id: number; ruta: string; activo: boolean; disparos: number }[]>([]);
  protected readonly plantillasSenuelo = signal<{ id: string; nombre: string }[]>([]);
  protected readonly cerrarSesionSenuelo = signal(true);

  // Telegram
  protected readonly telegram = signal<Telegram | null>(null);
  protected readonly enlaceTelegram = signal<string | null>(null);
  protected readonly confirmarTelegram = signal(false);
  private esperaTelegram?: ReturnType<typeof setInterval>;

  // verificación en dos pasos
  protected readonly dos = signal<DosPasos | null>(null);
  protected readonly qr = signal<string | null>(null);
  protected readonly codigo = signal('');
  protected readonly codigos = signal<string[] | null>(null);
  protected readonly errorDos = signal('');
  protected readonly confirmarQuitar = signal(false);
  protected readonly confirmarPanico = signal(false);

  protected readonly enLinea = computed(() => this.sesiones().filter((s) => s.enLinea).length);
  protected readonly accesosVistos = computed(() => (this.verTodos() ? this.accesos() : this.accesos().slice(0, 8)));
  protected readonly clave = computed(() => this.dos()?.sharedKey?.match(/.{1,4}/g)?.join(' ') ?? '');
  protected readonly enlaceApp = computed(() => {
    const k = this.dos()?.sharedKey;
    const emisor = encodeURIComponent('MDA Zona privada');
    return k ? `otpauth://totp/${emisor}:${encodeURIComponent(this.usuario() ?? 'mda')}?secret=${k}&issuer=${emisor}&digits=6&period=30` : '';
  });

  constructor() {
    this.push.comprobar();
    this.cargar();
    inject(RecargaService).alRecargar(() => this.cargar(), inject(DestroyRef));
    // la lista de sesiones se refresca sola para ver quién sigue en línea
    const t = setInterval(() => { if (!document.hidden) this.leerSesiones(); }, 30_000);
    inject(DestroyRef).onDestroy(() => { clearInterval(t); clearInterval(this.esperaTelegram); });
  }

  private async cargar(): Promise<void> {
    this.leerSesiones();
    const get = <T>(url: string) => firstValueFrom(this.http.get<T>(url));
    get<Acceso[]>('/api/seguridad/accesos').then((a) => this.accesos.set(a)).catch(() => {});
    this.leerSenuelos();
    get<Estado>('/api/seguridad/estado').then((e) => {
      this.estado.set(e);
      // en el iPhone, al ir a la app de códigos y volver, la app se recarga: se retoma la activación a medias
      if (!e.dosPasos && this.activacionPendiente()) this.empezarDosPasos();
    }).catch(() => {});
    get<Conexion[]>('/api/conexiones').then((c) => this.conexiones.set(c)).catch(() => {});
    get<Telegram>('/api/telegram').then((t) => this.telegram.set(t)).catch(() => {});
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

  // cambia el ajuste; aflojarlo o quitarlo pide la contraseña otra vez (si no, quien coja el móvil abierto lo desactivaría)
  protected async cambiarBloqueo(valor: string): Promise<void> {
    const s = Number(valor) || 0;
    this.errorBloqueo.set('');
    try {
      await this.sesionSvc.ajustarBloqueo(s || null);
      this.bloqueoSeg.set(s); this.pedirClaveBloqueo.set(false);
      this.aviso.set(s ? 'Listo: la app se bloqueará al volver y pedirá Face ID.' : 'Bloqueo desactivado en este dispositivo.');
    } catch (e) {
      const x = e as HttpErrorResponse;
      if (x.status === 403 && x.error?.reautenticar) { this.pedirClaveBloqueo.set(true); this.bloqueoSeg.set(this.bloqueoSeg()); this.pendienteBloqueo = s; }
      else this.errorBloqueo.set('No se pudo cambiar el ajuste.');
    }
  }
  private pendienteBloqueo = 0;

  private readonly reauth = inject(Reautenticar);

  protected async confirmarBloqueoConCara(): Promise<void> {
    try {
      if (!(await this.reauth.confirmarConPasskey())) return;
      await this.cambiarBloqueo(String(this.pendienteBloqueo));
    } catch { this.errorBloqueo.set('Ese passkey no sirve. Usa la contraseña.'); }
  }

  protected async confirmarBloqueo(ev: Event): Promise<void> {
    ev.preventDefault();
    try {
      await this.reauth.confirmarConClave(this.claveBloqueo(), this.codigoBloqueo());
      this.claveBloqueo.set(''); this.codigoBloqueo.set('');
      await this.cambiarBloqueo(String(this.pendienteBloqueo));
    } catch { this.errorBloqueo.set('Contraseña (o código) incorrectos.'); }
  }

  // corta todo (sesiones, tokens, lo público y la entrada durante una hora) y te lleva a la pantalla de entrar
  protected async panico(): Promise<void> {
    this.ocupado.set('panico');
    try { await firstValueFrom(this.http.post('/api/seguridad/panico', null)); } finally { this.ocupado.set(null); }
    location.href = '/entrar';
  }

  // el menú «···» de cada señuelo se cierra al elegir una acción
  protected cerrarMenu(e: Event): void { ((e.target as HTMLElement).closest('details') as HTMLDetailsElement | null)?.removeAttribute('open'); }

  private leerSenuelos(): void {
    firstValueFrom(this.http.get<{ cerrarSesion: boolean; senuelos: { id: number; ruta: string; activo: boolean; disparos: number }[]; plantillas: { id: string; nombre: string }[] }>('/api/senuelos'))
      .then((r) => { this.senuelos.set(r.senuelos); this.plantillasSenuelo.set(r.plantillas); this.cerrarSesionSenuelo.set(r.cerrarSesion); })
      .catch(() => {});
  }

  protected async crearSenuelo(plantilla: string): Promise<void> {
    await firstValueFrom(this.http.post('/api/senuelos', { plantilla, carpeta: '' }));
    this.leerSenuelos();
  }

  protected async alternarSenuelo(id: number, activo: boolean): Promise<void> {
    await firstValueFrom(this.http.put(`/api/senuelos/${id}?activo=${activo}`, null));
    this.leerSenuelos();
  }

  protected async quitarSenuelo(id: number): Promise<void> {
    if (!confirm('¿Quitar este señuelo? Se borra el archivo falso.')) return;
    await firstValueFrom(this.http.delete(`/api/senuelos/${id}`));
    this.leerSenuelos();
  }

  protected async probarSenuelo(id: number): Promise<void> {
    await firstValueFrom(this.http.post(`/api/senuelos/${id}/probar`, null));
    this.aviso.set('Prueba enviada: mira tus notificaciones y Telegram.');
  }

  protected async ajustarCierre(v: boolean): Promise<void> {
    this.cerrarSesionSenuelo.set(v);
    await firstValueFrom(this.http.put(`/api/senuelos/ajustes?cerrarSesion=${v}`, null));
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

  protected async resumenSemanal(v: boolean): Promise<void> {
    this.estado.update((e) => (e ? { ...e, resumenSemanal: v } : e));
    await firstValueFrom(this.http.put('/api/seguridad/ajustes', { avisarInicios: this.estado()?.avisarInicios ?? false, resumenSemanal: v }));
  }

  protected async alternarPush(): Promise<void> {
    this.ocupado.set('push-on');
    try {
      if (this.push.estado() === 'activas') await this.push.desactivar();
      else if (await this.push.activar()) this.aviso.set('¡Notificaciones activadas en este dispositivo!');
      this.estado.update((e) => (e ? { ...e, dispositivosPush: e.dispositivosPush + (this.push.estado() === 'activas' ? 1 : -1) } : e));
    } catch {
      this.aviso.set('No se pudieron activar las notificaciones en este navegador.');
    } finally { this.ocupado.set(null); }
  }

  protected copiaVieja(f: string | null): boolean { return !f || Date.now() - new Date(f).getTime() > 30 * 3600_000; }
  protected tamano(b: number): string { return b > 1048576 ? `${(b / 1048576).toFixed(1)} MB` : `${Math.max(1, Math.round(b / 1024))} KB`; }

  // vincular: un enlace de un solo uso que abre el bot; aquí se espera a que pulses «Iniciar»
  protected async vincularTelegram(): Promise<void> {
    this.ocupado.set('telegram');
    try {
      const r = await firstValueFrom(this.http.post<{ enlace: string }>('/api/telegram/vincular', null));
      this.enlaceTelegram.set(r.enlace);
      window.open(r.enlace, '_blank', 'noopener');
      const hasta = Date.now() + 15 * 60_000;
      clearInterval(this.esperaTelegram);
      this.esperaTelegram = setInterval(async () => {
        try {
          const t = await firstValueFrom(this.http.get<Telegram>('/api/telegram'));
          if (t.vinculado || Date.now() > hasta) {
            clearInterval(this.esperaTelegram);
            this.enlaceTelegram.set(null);
            this.telegram.set(t);
            if (t.vinculado) {
              this.estado.update((e) => (e ? { ...e, canales: { ...e.canales, telegram: true } } : e));
              this.aviso.set('¡Telegram vinculado! Te ha escrito el bot: ya puedes hablarle.');
            }
          }
        } catch { /* se reintenta */ }
      }, 3000);
    } catch (e) {
      this.aviso.set(e instanceof HttpErrorResponse ? e.error?.detail ?? 'No se pudo vincular.' : 'Sin conexión.');
    } finally { this.ocupado.set(null); }
  }

  protected async desvincularTelegram(): Promise<void> {
    if (!this.confirmarTelegram()) { this.confirmarTelegram.set(true); setTimeout(() => this.confirmarTelegram.set(false), 4000); return; }
    await firstValueFrom(this.http.delete('/api/telegram'));
    this.confirmarTelegram.set(false);
    this.telegram.update((t) => (t ? { ...t, vinculado: false, nombre: null } : t));
    this.estado.update((e) => (e ? { ...e, canales: { ...e.canales, telegram: false } } : e));
  }

  protected async probar(canal: 'correo' | 'whatsapp' | 'push' | 'telegram'): Promise<void> {
    this.ocupado.set(canal);
    try {
      await firstValueFrom(this.http.post(`/api/seguridad/avisos/prueba/${canal}`, null));
      this.aviso.set(canal === 'correo' ? 'Correo de prueba enviado: mira tu bandeja de entrada.' : canal === 'push' ? 'Notificación enviada a tus dispositivos.' : canal === 'telegram' ? 'Mensaje de prueba enviado a Telegram.' : 'WhatsApp de prueba enviado.');
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

  private static readonly PENDIENTE = 'dos-pasos-pendiente';
  private activacionPendiente(): boolean {
    try { return Date.now() - Number(localStorage.getItem(Seguridad.PENDIENTE) ?? 0) < 15 * 60_000; } catch { return false; }
  }
  private marcarPendiente(si: boolean): void {
    try { if (si) localStorage.setItem(Seguridad.PENDIENTE, String(Date.now())); else localStorage.removeItem(Seguridad.PENDIENTE); } catch { /* sin almacenamiento */ }
  }

  protected async empezarDosPasos(): Promise<void> {
    this.errorDos.set('');
    this.codigo.set('');
    const d = await this.dosPasos({});
    this.dos.set(d);
    if (!this.activacionPendiente()) this.marcarPendiente(true);
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
      // se cree lo que dice el servidor, no lo que se ha pedido
      if (!d.isTwoFactorEnabled) { this.errorDos.set('No se ha podido activar. Prueba otra vez en un momento.'); return; }
      this.dos.set(null);
      this.marcarPendiente(false);
      this.codigos.set(d.recoveryCodes);
      this.estado.update((x) => (x ? { ...x, dosPasos: true } : x));
    } catch {
      this.errorDos.set('Ese código no vale. Mira que la hora del móvil esté bien y prueba con el siguiente.');
    }
  }

  protected cancelarDosPasos(): void {
    this.dos.set(null);
    this.marcarPendiente(false);
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
