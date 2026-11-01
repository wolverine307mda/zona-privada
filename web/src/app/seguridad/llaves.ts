import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { crearCredencial } from '../core/passkey';
import { Reautenticar } from '../core/reautenticar';

interface Llave {
  id: string; nombre: string; creado: string; sincronizada: boolean; verificada: boolean; transportes: string[];
  alta: { ip: string; dispositivo: string; tipo: string; agente: string; idioma: string; zona: string; pantalla: string } | null;
  ultimoUso: { fecha: string; ip: string; dispositivo: string; motivo: string } | null;
  usos: number;
}

const ICONO: Record<string, string> = {
  movil: 'M8 3h8v18H8zM11.5 18h1',
  tablet: 'M5 3h14v18H5zM11.5 18h1',
  ordenador: 'M4 5h16v11H4zM8 20h8M12 16v4',
  otro: 'M15 7a4 4 0 1 1-3.5 5.9L4 20v-3l2-1v-2l2-1 3.5-3.1A4 4 0 0 1 15 7zM16 8h.01',
};

// Llaves de acceso (passkeys): una tarjeta por dispositivo con su nombre, de dónde y cuándo se creó y cuándo se usó por última vez.
@Component({
  selector: 'app-llaves',
  styleUrls: ['../gastos/comun.css', './llaves.css'],
  template: `
    <section class="caja">
      <header class="caja__cab"><h3>Llaves de acceso</h3><span class="sub">Entrar y confirmar con la cara o la huella, sin escribir la contraseña</span></header>

      @if (!soporta) { <p class="nada">Este navegador no admite llaves de acceso. Prueba con Safari o Chrome actualizados.</p> }

      <ul class="llaves">
        @for (l of llaves(); track l.id) {
          <li class="llave" [class.llave--ok]="resultado()?.id === l.id && resultado()?.ok" [class.llave--mal]="resultado()?.id === l.id && !resultado()?.ok">
            <svg class="icon llave__icono" viewBox="0 0 24 24" aria-hidden="true"><path [attr.d]="icono(l)"/></svg>
            <div class="llave__cuerpo">
              @if (editando() === l.id) {
                <form class="llave__edicion" (submit)="guardarNombre($event, l)">
                  <input class="campo__input" maxlength="40" aria-label="Nombre de la llave" [value]="nombreEdicion()" (input)="nombreEdicion.set($any($event.target).value)" autofocus>
                  <button class="button" type="submit" [disabled]="!nombreEdicion().trim()">Guardar</button>
                  <button class="enlace" type="button" (click)="editando.set(null)">Cancelar</button>
                </form>
              } @else {
                <div class="llave__nombre">
                  <strong>{{ l.nombre }}</strong>
                  <button class="llave__lapiz" type="button" aria-label="Cambiar el nombre de {{ l.nombre }}" (click)="editar(l)">
                    <svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M4 20h4L19 9l-4-4L4 16zM13.5 6.5l4 4"/></svg>
                  </button>
                </div>
              }
              <div class="etiquetas">
                <span class="etiqueta" [class.etiqueta--acento]="l.sincronizada">{{ l.sincronizada ? 'Sincronizada en tu cuenta (iCloud/Google)' : 'Solo en este dispositivo' }}</span>
                @if (l.verificada) { <span class="etiqueta">Verificada con cara o huella</span> }
              </div>
              <p class="llave__linea">
                @if (l.ultimoUso; as u) { Último uso {{ hace(u.fecha) }} · {{ motivo(u.motivo) }} · {{ u.dispositivo }} }
                @else { Sin usar todavía }
              </p>

              @if (abierta() === l.id) {
                <dl class="detalle">
                  <dt>Creada</dt><dd>{{ fecha(l.creado) }}</dd>
                  @if (l.alta; as a) {
                    <dt>Desde</dt><dd>{{ a.dispositivo }}</dd>
                    <dt>IP al crearla</dt><dd>{{ a.ip }}</dd>
                    @if (a.idioma || a.zona) { <dt>Idioma y zona</dt><dd>{{ a.idioma }} {{ a.zona }}</dd> }
                    @if (a.pantalla) { <dt>Pantalla</dt><dd>{{ a.pantalla }}</dd> }
                  } @else { <dt>Origen</dt><dd>Creada antes de guardar estos datos</dd> }
                  @if (l.ultimoUso; as u) { <dt>Último uso</dt><dd>{{ fecha(u.fecha) }} · IP {{ u.ip }}</dd> }
                  <dt>Usos desde que se anotan</dt><dd>{{ l.usos }}</dd>
                </dl>
              }

              <div class="acciones">
                <button class="enlace" type="button" [attr.aria-expanded]="abierta() === l.id" (click)="abierta.set(abierta() === l.id ? null : l.id)">{{ abierta() === l.id ? 'Ocultar detalles' : 'Ver detalles' }}</button>
                @if (soporta) { <button class="enlace" type="button" (click)="probar(l)" [disabled]="probando() !== null">{{ probando() === l.id ? 'Comprobando…' : 'Probar' }}</button> }
                @if (quitando() === l.id) {
                  <span class="confirmar" role="alert">¿Quitar «{{ l.nombre }}»?
                    <button class="enlace enlace--peligro" type="button" (click)="quitar(l)">Sí, quitar</button>
                    <button class="enlace" type="button" (click)="quitando.set(null)">No</button></span>
                } @else {
                  <button class="enlace enlace--peligro" type="button" (click)="quitando.set(l.id)">Quitar</button>
                }
              </div>
              @if (resultado()?.id === l.id) { <p class="resultado" [class.resultado--mal]="!resultado()!.ok" role="status">{{ resultado()!.texto }}</p> }
            </div>
          </li>
        } @empty {
          <li class="vacio">
            <svg class="icon vacio__icono" viewBox="0 0 24 24" aria-hidden="true"><path [attr.d]="iconoOtro"/></svg>
            <p><strong>Aún no tienes ninguna llave.</strong> Con una, entras y confirmas lo importante (apagar el PC, por ejemplo) con Face ID o la huella, y una contraseña robada no sirve de nada.</p>
          </li>
        }
      </ul>

      @if (soporta) {
        @if (!nombrando()) {
          <button class="button" type="button" (click)="empezarAlta()" [disabled]="llaves().length >= 5">{{ llaves().length >= 5 ? 'Máximo 5 llaves' : 'Añadir este dispositivo' }}</button>
        } @else {
          <form class="alta" (submit)="registrar($event)">
            <label class="campo"><span class="campo__etiqueta">Pon un nombre a esta llave</span>
              <input class="campo__input" maxlength="40" placeholder="p. ej. iPhone de Mario" [value]="nombreNuevo()" (input)="nombreNuevo.set($any($event.target).value)"></label>
            <p class="nada">Después el sistema te pedirá la cara o la huella. Se guardará desde dónde la creas (IP, navegador, sistema) para que reconozcas cada llave.</p>
            <div class="acciones">
              <button class="enlace" type="button" (click)="nombrando.set(false)">Cancelar</button>
              <button class="button" type="submit" [disabled]="ocupado() || !nombreNuevo().trim()">{{ ocupado() ? 'Esperando al sistema…' : 'Crear la llave' }}</button>
            </div>
          </form>
        }
        @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
        <p class="nada">Se crea desde la dirección pública de la app (no desde localhost) y no sirve en otra dirección. La contraseña y los dos pasos siguen valiendo como respaldo.</p>
      }
    </section>
  `,
})
export class Llaves implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly reauth = inject(Reautenticar);
  protected readonly soporta = this.reauth.conPasskey;
  protected readonly llaves = signal<Llave[]>([]);
  protected readonly abierta = signal<string | null>(null);
  protected readonly editando = signal<string | null>(null);
  protected readonly nombreEdicion = signal('');
  protected readonly quitando = signal<string | null>(null);
  protected readonly probando = signal<string | null>(null);
  protected readonly resultado = signal<{ id: string; ok: boolean; texto: string } | null>(null);
  protected readonly nombrando = signal(false);
  protected readonly nombreNuevo = signal('');
  protected readonly ocupado = signal(false);
  protected readonly error = signal('');
  protected readonly iconoOtro = ICONO['otro'];
  /** avisa a la pantalla de Seguridad para que enseñe un aviso general */
  readonly total = computed(() => this.llaves().length);

  ngOnInit(): void { void this.leer(); }

  private async leer(): Promise<void> {
    try { this.llaves.set(await firstValueFrom(this.http.get<Llave[]>('/api/passkeys'))); } catch { /* se queda como estaba */ }
  }

  protected icono(l: Llave): string { return ICONO[l.alta?.tipo ?? 'otro'] ?? ICONO['otro']; }
  protected fecha(iso: string): string { return new Date(iso).toLocaleString('es-ES', { day: 'numeric', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' }); }
  protected motivo(m: string): string { return m === 'entrada' ? 'para entrar' : m === 'desbloqueo' ? 'para desbloquear' : m === 'confirmación' ? 'para confirmar' : m; }
  protected hace(iso: string): string {
    const m = Math.round((Date.now() - new Date(iso).getTime()) / 60000);
    if (m < 1) return 'ahora mismo';
    if (m < 60) return `hace ${m} min`;
    if (m < 1440) return `hace ${Math.round(m / 60)} h`;
    return `el ${new Date(iso).toLocaleDateString('es-ES', { day: 'numeric', month: 'short' })}`;
  }

  // un nombre por defecto según el aparato desde el que se crea
  private sugerido(): string {
    const ua = navigator.userAgent;
    const base = /iPhone/.test(ua) ? 'iPhone' : /iPad/.test(ua) ? 'iPad' : /Android/.test(ua) ? 'Android' : /Windows/.test(ua) ? 'Windows' : /Macintosh|Mac OS X/.test(ua) ? 'Mac' : 'Mi dispositivo';
    return base;
  }

  protected empezarAlta(): void { this.nombreNuevo.set(this.sugerido()); this.error.set(''); this.nombrando.set(true); }

  protected async registrar(ev: Event): Promise<void> {
    ev.preventDefault();
    const nombre = this.nombreNuevo().trim();
    if (!nombre || this.ocupado()) return;
    this.ocupado.set(true); this.error.set('');
    try {
      const opciones = await firstValueFrom(this.http.post<unknown>('/api/passkeys/opciones', null));
      const credencial = await crearCredencial(opciones);
      const dispositivo = { idioma: navigator.language, zona: Intl.DateTimeFormat().resolvedOptions().timeZone, pantalla: `${screen.width}x${screen.height}` };
      await firstValueFrom(this.http.post('/api/passkeys', { credencial, nombre, dispositivo }));
      this.nombrando.set(false);
      await this.leer();
      const nueva = this.llaves().find((l) => l.nombre === nombre);
      if (nueva) this.resultado.set({ id: nueva.id, ok: true, texto: 'Llave creada: la próxima vez puedes entrar con la cara o la huella.' });
    } catch (e) {
      const cancelado = e instanceof DOMException && (e.name === 'NotAllowedError' || e.name === 'AbortError');
      if (!cancelado) this.error.set('No se pudo crear la llave. Ábrela desde su dirección pública (no localhost) e inténtalo otra vez.');
    } finally { this.ocupado.set(false); }
  }

  protected editar(l: Llave): void { this.nombreEdicion.set(l.nombre); this.editando.set(l.id); }

  protected async guardarNombre(ev: Event, l: Llave): Promise<void> {
    ev.preventDefault();
    const nombre = this.nombreEdicion().trim();
    if (!nombre) return;
    try {
      await firstValueFrom(this.http.put(`/api/passkeys/${encodeURIComponent(l.id)}`, { nombre }));
      this.editando.set(null);
      await this.leer();
    } catch (e) { this.error.set((e as HttpErrorResponse).status === 400 ? 'El nombre tiene que tener entre 1 y 40 letras.' : 'No se pudo cambiar el nombre.'); }
  }

  // comprueba con la cara que la llave funciona (también deja la sesión confirmada 2 minutos)
  protected async probar(l: Llave): Promise<void> {
    this.probando.set(l.id); this.resultado.set(null);
    try {
      const ok = await this.reauth.confirmarConPasskey();
      if (ok) { this.resultado.set({ id: l.id, ok: true, texto: 'Funciona: has confirmado con tu llave.' }); await this.leer(); }
    } catch { this.resultado.set({ id: l.id, ok: false, texto: 'Esa llave no ha servido. Prueba otra vez o créala de nuevo.' }); }
    finally { this.probando.set(null); }
  }

  protected async quitar(l: Llave): Promise<void> {
    this.quitando.set(null);
    await firstValueFrom(this.http.delete(`/api/passkeys/${encodeURIComponent(l.id)}`));
    await this.leer();
  }
}
