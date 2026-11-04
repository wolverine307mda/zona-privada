import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Reautenticar } from '../core/reautenticar';
import { Esqueleto } from '../ui';

interface Campo { nombre: string; titulo: string; configurada: boolean; actualizado: string | null; }
interface Servicio { id: string; titulo: string; resumen: string; campos: Campo[]; }
interface Estado { reautenticado: boolean; servicios: Servicio[]; }

const OJO = 'M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12zM12 9a3 3 0 1 0 0 6 3 3 0 0 0 0-6z';
const OJO_CERRADO = 'M3 3l18 18M10.6 5.1A9.9 9.9 0 0 1 12 5c6 0 10 7 10 7a17 17 0 0 1-3.2 3.9M6.6 6.7C3.9 8.4 2 12 2 12s4 7 10 7a9.8 9.8 0 0 0 4.4-1M9.9 9.9a3 3 0 0 0 4.2 4.2';

// Claves de Telegram, WhatsApp, correo, Spotify, Google y el latido: se guardan cifradas en la base y ganan al .env.
// Nunca se muestran enteras: ver, cambiar o quitar una pide confirmar con Face ID o la contraseña, y el valor vuelve a ocultarse a los 20 s.
@Component({
  selector: 'app-claves',
  imports: [Esqueleto],
  styleUrls: ['../gastos/comun.css', './claves.css'],
  template: `
    <section class="caja">
      <header class="caja__cab"><h3>Claves de servicios</h3><span class="sub">Cifradas en la base de datos · para verlas o cambiarlas se pide Face ID</span></header>

      @if (confirmando()) {
        <form class="confirmar" (submit)="confirmarClave($event)" novalidate>
          <p><strong>Confirma que eres tú</strong> para seguir con las claves.</p>
          @if (reauth.conPasskey) {
            <button class="button cara" type="button" (click)="conCara()" [disabled]="ocupado()">
              <svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M4 8V6a2 2 0 0 1 2-2h2M16 4h2a2 2 0 0 1 2 2v2M20 16v2a2 2 0 0 1-2 2h-2M8 20H6a2 2 0 0 1-2-2v-2M9 10v1M15 10v1M12 10v4h-1M9 16c1.5 1.3 4.5 1.3 6 0"/></svg>
              {{ ocupado() ? 'Comprobando…' : 'Confirmar con Face ID' }}
            </button>
            @if (!verClave()) { <button class="enlace" type="button" (click)="verClave.set(true)">Usar la contraseña</button> }
          }
          @if (!reauth.conPasskey || verClave()) {
            <input type="text" autocomplete="username" hidden>
            <label class="campo"><span class="campo__etiqueta">Tu contraseña</span>
              <input class="campo__input" type="password" autocomplete="current-password" [value]="clave()" (input)="clave.set($any($event.target).value)"></label>
            <label class="campo"><span class="campo__etiqueta">Código de la app de dos pasos (si lo tienes)</span>
              <input class="campo__input" inputmode="numeric" autocomplete="one-time-code" maxlength="12" [value]="codigo()" (input)="codigo.set($any($event.target).value)"></label>
          }
          @if (errorConfirmar()) { <p class="error" role="alert">{{ errorConfirmar() }}</p> }
          <footer class="acciones">
            <button class="enlace" type="button" (click)="terminarConfirmar(false)">Cancelar</button>
            @if (!reauth.conPasskey || verClave()) { <button class="button" type="submit" [disabled]="ocupado()">{{ ocupado() ? 'Comprobando…' : 'Confirmar' }}</button> }
          </footer>
        </form>
      }

      @if (error()) { <p class="error" role="alert">{{ error() }}</p> }

      @for (s of servicios(); track s.id) {
        <article class="servicio">
          <header class="servicio__cab">
            <div><h4>{{ s.titulo }}</h4><small>{{ s.resumen }}</small></div>
            <span class="estado" [class.estado--ok]="configuradas(s) === s.campos.length" [class.estado--falta]="configuradas(s) === 0">{{ configuradas(s) }} de {{ s.campos.length }}</span>
          </header>
          <ul class="campos">
            @for (c of s.campos; track c.nombre) {
              <li class="clave" [class.clave--falta]="!c.configurada">
                <span class="clave__titulo">{{ c.titulo }}</span>
                @if (editando() === id(s, c)) {
                  <form class="clave__edicion" (submit)="guardar($event, s, c)" novalidate>
                    <input class="campo__input" [type]="verNuevo() ? 'text' : 'password'" autocomplete="off" autocapitalize="off" spellcheck="false" [attr.aria-label]="'Nuevo valor de ' + c.titulo"
                      [value]="nuevo()" (input)="nuevo.set($any($event.target).value)" placeholder="Pega el nuevo valor">
                    <button class="ojo" type="button" (click)="verNuevo.set(!verNuevo())" [attr.aria-label]="verNuevo() ? 'Ocultar lo escrito' : 'Mostrar lo escrito'">
                      <svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path [attr.d]="verNuevo() ? ojoCerrado : ojo"/></svg>
                    </button>
                    <button class="button" type="submit" [disabled]="ocupado() || !nuevo().trim()">{{ ocupado() ? 'Guardando…' : 'Guardar' }}</button>
                    <button class="enlace" type="button" (click)="editando.set(null)">Cancelar</button>
                  </form>
                } @else {
                  <span class="clave__valor" [class.clave__valor--visible]="visibles()[id(s, c)] !== undefined">
                    @if (visibles()[id(s, c)] !== undefined) { {{ visibles()[id(s, c)] || '(vacía)' }} }
                    @else if (c.configurada) { ••••••••••••••• }
                    @else { sin configurar }
                  </span>
                  <span class="clave__acciones">
                    @if (c.configurada) {
                      <button class="ojo" type="button" (click)="ver(s, c)" [attr.aria-label]="(visibles()[id(s, c)] !== undefined ? 'Ocultar ' : 'Ver ') + c.titulo" [attr.aria-pressed]="visibles()[id(s, c)] !== undefined">
                        <svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path [attr.d]="visibles()[id(s, c)] !== undefined ? ojoCerrado : ojo"/></svg>
                      </button>
                    }
                    <button class="enlace" type="button" (click)="editar(s, c)">{{ c.configurada ? 'Cambiar' : 'Añadir' }}</button>
                    @if (c.configurada) {
                      @if (borrando() === id(s, c)) { <button class="enlace enlace--peligro" type="button" (click)="quitar(s, c)">¿Seguro?</button> }
                      @else { <button class="enlace" type="button" (click)="borrando.set(id(s, c))" [attr.aria-label]="'Quitar ' + c.titulo">Quitar</button> }
                    }
                  </span>
                }
              </li>
            }
          </ul>
        </article>
      } @empty {
        <app-esqueleto forma="linea" [lineas]="3" />
      }
      <p class="nada">Si cambias una clave aquí, vale al momento y la del <code>.env</code> deja de usarse. Pasa lo mismo al quitarla.</p>
    </section>
  `,
})
export class Claves implements OnInit {
  private readonly http = inject(HttpClient);
  protected readonly reauth = inject(Reautenticar);
  protected readonly ojo = OJO;
  protected readonly ojoCerrado = OJO_CERRADO;
  protected readonly servicios = signal<Servicio[]>([]);
  protected readonly visibles = signal<Record<string, string>>({});
  protected readonly editando = signal<string | null>(null);
  protected readonly borrando = signal<string | null>(null);
  protected readonly nuevo = signal('');
  protected readonly verNuevo = signal(false);
  protected readonly error = signal('');
  protected readonly ocupado = signal(false);
  // confirmación (Face ID o contraseña)
  protected readonly confirmando = signal(false);
  protected readonly clave = signal('');
  protected readonly codigo = signal('');
  protected readonly verClave = signal(false);
  protected readonly errorConfirmar = signal('');
  private resolver: ((ok: boolean) => void) | null = null;
  private readonly temporizadores = new Map<string, ReturnType<typeof setTimeout>>();

  constructor() {
    inject(DestroyRef).onDestroy(() => { this.temporizadores.forEach(clearTimeout); this.resolver?.(false); });
  }

  ngOnInit(): void { void this.leer(); }

  protected id(s: Servicio, c: Campo): string { return `${s.id}/${c.nombre}`; }
  protected configuradas(s: Servicio): number { return s.campos.filter((c) => c.configurada).length; }

  private async leer(): Promise<void> {
    try { this.servicios.set((await firstValueFrom(this.http.get<Estado>('/api/seguridad/claves'))).servicios); }
    catch { this.error.set('No se pudieron leer las claves.'); }
  }

  // hace la petición y, si el servidor pide confirmar (403), enseña la confirmación y la repite una vez
  private async conConfirmacion<T>(accion: () => Promise<T>): Promise<T | undefined> {
    for (let intento = 0; intento < 2; intento++) {
      try { return await accion(); }
      catch (e) {
        const x = e as HttpErrorResponse;
        if (x.status === 403 && x.error?.reautenticar) { if (!(await this.pedirConfirmacion())) return undefined; continue; }
        this.error.set(x.error?.errors?.valor?.[0] ?? x.error?.detail ?? x.error?.title ?? 'No se pudo completar.');
        return undefined;
      }
    }
    return undefined;
  }

  private pedirConfirmacion(): Promise<boolean> {
    this.resolver?.(false);
    this.clave.set(''); this.codigo.set(''); this.errorConfirmar.set('');
    this.verClave.set(!this.reauth.conPasskey);
    this.confirmando.set(true);
    return new Promise((res) => { this.resolver = res; });
  }

  protected terminarConfirmar(ok: boolean): void {
    this.confirmando.set(false); this.clave.set(''); this.codigo.set('');
    const r = this.resolver; this.resolver = null; r?.(ok);
  }

  protected async conCara(): Promise<void> {
    if (this.ocupado()) return;
    this.ocupado.set(true); this.errorConfirmar.set('');
    try { if (await this.reauth.confirmarConPasskey()) this.terminarConfirmar(true); }
    catch (e) { this.errorConfirmar.set((e as HttpErrorResponse).status === 401 ? 'Ese passkey no sirve. Usa la contraseña.' : 'No se pudo confirmar con Face ID.'); this.verClave.set(true); }
    finally { this.ocupado.set(false); }
  }

  protected async confirmarClave(ev: Event): Promise<void> {
    ev.preventDefault();
    if (this.ocupado()) return;
    this.ocupado.set(true); this.errorConfirmar.set('');
    try { await this.reauth.confirmarConClave(this.clave(), this.codigo()); this.terminarConfirmar(true); }
    catch (e) { const x = e as HttpErrorResponse; this.errorConfirmar.set(x.error?.detail ?? x.error?.title ?? 'No se pudo confirmar.'); }
    finally { this.ocupado.set(false); }
  }

  private ocultar(k: string): void {
    clearTimeout(this.temporizadores.get(k)); this.temporizadores.delete(k);
    this.visibles.update((v) => { const { [k]: _, ...resto } = v; return resto; });
  }

  protected async ver(s: Servicio, c: Campo): Promise<void> {
    const k = this.id(s, c);
    if (this.visibles()[k] !== undefined) { this.ocultar(k); return; }
    this.error.set('');
    const r = await this.conConfirmacion(() => firstValueFrom(this.http.get<{ valor: string }>(`/api/seguridad/claves/${s.id}/${c.nombre}`)));
    if (!r) return;
    this.visibles.update((v) => ({ ...v, [k]: r.valor }));
    this.temporizadores.set(k, setTimeout(() => this.ocultar(k), 20_000));
  }

  protected editar(s: Servicio, c: Campo): void {
    this.editando.set(this.id(s, c)); this.nuevo.set(''); this.verNuevo.set(false); this.borrando.set(null); this.error.set('');
  }

  protected async guardar(ev: Event, s: Servicio, c: Campo): Promise<void> {
    ev.preventDefault();
    if (this.ocupado() || !this.nuevo().trim()) return;
    this.ocupado.set(true); this.error.set('');
    try {
      const hecho = await this.conConfirmacion(() => firstValueFrom(this.http.put(`/api/seguridad/claves/${s.id}/${c.nombre}`, { valor: this.nuevo() })).then(() => true));
      if (!hecho) return;
      this.ocultar(this.id(s, c)); this.editando.set(null); this.nuevo.set('');
      await this.leer();
    } finally { this.ocupado.set(false); }
  }

  protected async quitar(s: Servicio, c: Campo): Promise<void> {
    this.borrando.set(null); this.error.set('');
    const hecho = await this.conConfirmacion(() => firstValueFrom(this.http.delete(`/api/seguridad/claves/${s.id}/${c.nombre}`)).then(() => true));
    if (!hecho) return;
    this.ocultar(this.id(s, c));
    await this.leer();
  }
}
