import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, ElementRef, OnInit, computed, inject, signal, viewChild } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ConfirmarService } from '../core/confirmar.service';
import { EstadoError, Esqueleto } from '../ui';

interface Ajustes { activo: boolean; enPanico: boolean; intruso: boolean; camaraIp: boolean; dias: number; }
interface Captura { id: number; fecha: string; origen: 'pantalla' | 'camara' | 'camara-ip'; motivo: 'manual' | 'panico' | 'intruso'; ancho: number; alto: number; tamano: number; }
interface Respuesta { ajustes: Ajustes; capturas: Captura[]; }
interface Orden { estado: string; resultado: string; }

const ORIGEN: Record<string, string> = { pantalla: 'Pantalla', camara: 'Cámara del PC', 'camara-ip': 'Cámara IP' };
const MOTIVO: Record<string, string> = { manual: 'a petición tuya', panico: 'por el pánico', intruso: 'por intentos de entrar fallidos' };

// Fotos bajo demanda del PC y de tu casa: la pantalla, la cámara web (con ffmpeg) o una cámara IP de tu red. Está APAGADO por defecto; encenderlo y cada foto piden
// confirmar que eres tú. No graba vídeo ni audio, las fotos se guardan cifradas, se borran solas pasados unos días y cada una te llega como aviso.
@Component({
  selector: 'app-capturas-pc',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [EstadoError, Esqueleto],
  styleUrls: ['../gastos/comun.css', '../gastos/dialogos.css'],
  template: `
    <section class="caja">
      <header class="caja__cab"><h3>Fotos del PC y de casa</h3><span class="sub">pantalla, cámara web o cámara IP, solo cuando tú lo pides</span></header>
      @if (fallo()) { <app-estado-error titulo="No pude cargar las fotos" (reintentar)="cargar()" /> }
      @else if (datos() === null) { <app-esqueleto forma="bloque" alto="64px" /> }
      @else {
        <label class="interruptor">
          <input type="checkbox" [checked]="datos()!.ajustes.activo" (change)="poner({ activo: $any($event.target).checked })">
          <span class="interruptor__pista" aria-hidden="true"></span>
          <span>Permitir hacer fotos desde aquí</span>
        </label>
        @if (!datos()!.ajustes.activo) {
          <p class="nada">Apagado: no se puede hacer ninguna foto, ni a mano ni sola. Al encenderlo se pide confirmar que eres tú.</p>
        } @else {
          <div class="acciones">
            <button class="button button--sm" type="button" [disabled]="ocupado()" (click)="hacer('pantalla')">Captura de pantalla</button>
            <button class="button button--sm" type="button" [disabled]="ocupado()" (click)="hacer('camara')">Foto de la cámara</button>
            @if (datos()!.ajustes.camaraIp) { <button class="button button--sm" type="button" [disabled]="ocupado()" (click)="hacer('camara-ip')">Cámara IP</button> }
          </div>
          @if (mensaje()) { <p class="msg" role="status" aria-live="polite">{{ mensaje() }}</p> }

          @if (datos()!.capturas.length) {
            <ul class="fotos" aria-label="Fotos guardadas">
              @for (c of datos()!.capturas; track c.id) {
                <li>
                  <button type="button" (click)="ver(c)" [attr.aria-label]="'Ver la foto de ' + origen(c) + ' del ' + cuando(c)">
                    <img [src]="url(c)" alt="" loading="lazy" width="120" height="90">
                    <small>{{ origen(c) }} · {{ cuando(c) }}</small>
                  </button>
                </li>
              }
            </ul>
          } @else { <p class="nada">Aún no hay fotos.</p> }

          <details class="ajustes">
            <summary>Ajustes de las fotos</summary>
            <label class="interruptor">
              <input type="checkbox" [checked]="datos()!.ajustes.enPanico" (change)="poner({ enPanico: $any($event.target).checked })">
              <span class="interruptor__pista" aria-hidden="true"></span>
              <span>Hacer fotos de la pantalla y la cámara al activar el pánico</span>
            </label>
            <label class="interruptor">
              <input type="checkbox" [checked]="datos()!.ajustes.intruso" (change)="poner({ intruso: $any($event.target).checked })">
              <span class="interruptor__pista" aria-hidden="true"></span>
              <span>Foto de la cámara tras 3 intentos fallidos de entrar en 10 minutos</span>
            </label>
            <label class="campo"><span class="campo__etiqueta">Borrar las fotos a los <em>(días)</em></span>
              <input class="campo__input" type="number" min="1" max="365" [value]="datos()!.ajustes.dias" (change)="poner({ dias: +$any($event.target).value })"></label>
            <label class="campo"><span class="campo__etiqueta">Cámara IP <em>({{ datos()!.ajustes.camaraIp ? 'configurada; escribe otra para cambiarla' : 'dirección de la instantánea' }})</em></span>
              <input class="campo__input" type="text" inputmode="url" autocomplete="off" placeholder="http://192.168.1.50/snapshot.jpg" #ip (keydown.enter)="guardarIp(ip.value); ip.value = ''"></label>
            <div class="acciones">
              <button class="enlace" type="button" (click)="guardarIp(ip.value); ip.value = ''">Guardar la cámara IP</button>
              @if (datos()!.ajustes.camaraIp) { <button class="enlace" type="button" (click)="guardarIp('')">Quitar la cámara IP</button> }
              @if (datos()!.capturas.length) { <button class="boton-borrar" type="button" (click)="borrarTodas()">Borrar todas las fotos</button> }
            </div>
            <p class="nota">La cámara del PC necesita <b>ffmpeg</b> (<code>winget install Gyan.FFmpeg</code>) y que Windows deje usar la cámara a las aplicaciones de escritorio. La cámara IP: solo direcciones de tu red local.</p>
          </details>
        }
        @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
      }
    </section>

    <dialog class="dlg" #visor (click)="fuera($event)" aria-label="Foto">
      @if (abierta(); as c) {
        <div class="dlg__caja">
          <header class="dlg__cabeza">
            <h2>{{ origen(c) }}</h2>
            <button class="dlg__x" type="button" (click)="cerrar()" aria-label="Cerrar"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6l12 12M18 6 6 18" /></svg></button>
          </header>
          <img class="grande" [src]="url(c)" [alt]="'Foto de ' + origen(c) + ' del ' + cuando(c)">
          <p class="dlg__lead">{{ cuando(c) }} · {{ motivo(c) }} · {{ c.ancho }}×{{ c.alto }}</p>
          <footer class="dlg__pie"><button class="boton-borrar" type="button" (click)="borrar(c)">Borrar esta foto</button></footer>
        </div>
      }
    </dialog>
  `,
  styles: `
    .acciones { display: flex; flex-wrap: wrap; gap: 10px; align-items: center; margin: 12px 0; }
    .msg { margin: 0 0 10px; font-size: .9rem; }
    .fotos { list-style: none; margin: 8px 0 0; padding: 0; display: grid; grid-template-columns: repeat(auto-fill, minmax(120px, 1fr)); gap: 8px; }
    .fotos button { display: grid; gap: 4px; width: 100%; padding: 0; border: 0; background: none; color: inherit; font: inherit; text-align: left; cursor: pointer; }
    .fotos img { width: 100%; aspect-ratio: 4 / 3; object-fit: cover; border-radius: 12px; border: 1px solid var(--vidrio-borde); background: var(--tarjeta-2); }
    .fotos small { color: var(--soft); font-size: .72rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .ajustes { margin-top: 14px; padding-top: 10px; border-top: 1px solid var(--linea); }
    .ajustes summary { cursor: pointer; font-weight: 500; min-height: 44px; display: flex; align-items: center; }
    .nota { color: var(--soft); font-size: .8rem; }
    .grande { width: 100%; max-height: 70svh; object-fit: contain; border-radius: 12px; background: var(--tarjeta-2); }
  `,
})
export class CapturasPc implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly confirmar = inject(ConfirmarService);
  private readonly visor = viewChild.required<ElementRef<HTMLDialogElement>>('visor');

  protected readonly datos = signal<Respuesta | null>(null);
  protected readonly fallo = signal(false);
  protected readonly ocupado = signal(false);
  protected readonly mensaje = signal('');
  protected readonly error = signal('');
  protected readonly abierta = signal<Captura | null>(null);
  protected readonly hayDatos = computed(() => this.datos() !== null);

  ngOnInit(): void { void this.cargar(); }

  protected async cargar(): Promise<void> {
    this.fallo.set(false);
    try { this.datos.set(await firstValueFrom(this.http.get<Respuesta>('/api/pc/capturas'))); } catch { this.fallo.set(true); }
  }

  protected origen(c: Captura): string { return ORIGEN[c.origen] ?? c.origen; }
  protected motivo(c: Captura): string { return MOTIVO[c.motivo] ?? c.motivo; }
  protected cuando(c: Captura): string { return new Date(c.fecha).toLocaleString('es-ES', { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' }); }
  protected url(c: Captura): string { return `/api/pc/capturas/${c.id}/imagen`; }

  // una petición que puede pedir «confirma que eres tú» (403 reautenticar): se confirma y se repite una vez
  private async conConfirmacion<T>(accion: () => Promise<T>): Promise<T | undefined> {
    for (let intento = 0; ; intento++) {
      try { return await accion(); }
      catch (e) {
        const x = e as HttpErrorResponse;
        if (x.status === 403 && x.error?.reautenticar && intento === 0) { if (!(await this.confirmar.pedir('Confirma que eres tú para las fotos del PC.'))) return undefined; continue; }
        throw e;
      }
    }
  }

  private texto(e: unknown): string {
    const x = e as HttpErrorResponse;
    const errores = x.error?.errors as Record<string, string[]> | undefined;
    return x.error?.detail ?? (errores ? Object.values(errores).flat()[0] : null) ?? 'No se ha podido.';
  }

  protected async poner(cambio: Partial<{ activo: boolean; enPanico: boolean; intruso: boolean; dias: number; camaraIpUrl: string }>): Promise<void> {
    this.error.set('');
    try {
      const a = await this.conConfirmacion(() => firstValueFrom(this.http.put<Ajustes>('/api/pc/capturas/ajustes', cambio)));
      if (a) this.datos.update((d) => (d ? { ...d, ajustes: a } : d)); else await this.cargar();
    } catch (e) { this.error.set(this.texto(e)); await this.cargar(); }
  }

  protected guardarIp(url: string): Promise<void> { return this.poner({ camaraIpUrl: url.trim() }); }

  protected async hacer(origen: 'pantalla' | 'camara' | 'camara-ip'): Promise<void> {
    this.error.set(''); this.mensaje.set(''); this.ocupado.set(true);
    try {
      const r = await this.conConfirmacion(() => firstValueFrom(this.http.post<{ id: number | null; orden: number | null }>('/api/pc/capturas/capturar', { origen })));
      if (!r) return;
      if (r.orden) {
        this.mensaje.set('Pidiendo la foto al PC…');
        let ok = false;
        for (let i = 0; i < 28; i++) {
          await new Promise((res) => setTimeout(res, 1500));
          const o = await firstValueFrom(this.http.get<Orden>(`/api/pc/ordenes/${r.orden}`));
          if (o.estado === 'hecha') { ok = true; break; }
          if (o.estado === 'error' || o.estado === 'caducada') { this.error.set(o.estado === 'caducada' ? 'El agente no la recogió a tiempo.' : (o.resultado || 'No se pudo.')); break; }
        }
        if (ok) this.mensaje.set('Foto hecha y guardada (cifrada).');
      } else this.mensaje.set('Foto hecha y guardada (cifrada).');
      await this.cargar();
    } catch (e) { this.error.set(this.texto(e)); } finally { this.ocupado.set(false); }
  }

  protected ver(c: Captura): void { this.abierta.set(c); this.visor().nativeElement.showModal(); }
  protected cerrar(): void { this.visor().nativeElement.close(); this.abierta.set(null); }
  protected fuera(e: MouseEvent): void { if (e.target === this.visor().nativeElement) this.cerrar(); }

  protected async borrar(c: Captura): Promise<void> {
    try { await firstValueFrom(this.http.delete(`/api/pc/capturas/${c.id}`)); this.cerrar(); await this.cargar(); } catch (e) { this.error.set(this.texto(e)); }
  }

  protected async borrarTodas(): Promise<void> {
    try { await this.conConfirmacion(() => firstValueFrom(this.http.delete('/api/pc/capturas'))); await this.cargar(); } catch (e) { this.error.set(this.texto(e)); }
  }
}
