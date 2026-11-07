import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ConfirmarService } from '../core/confirmar.service';
import { EstadoError, Esqueleto, Interruptor } from '../ui';
import { MapaUbicacion } from './mapa-ubicacion';
import { CIUDADES, DURACIONES, MODOS, Modo, Punto, coordenadasValidas, distancia, duracion, kilometros, leerCoordenadas, posicionAhora, queda, segundos, texto } from './ubicacion-logica';

interface Actual { lat: number; lon: number; desde: string; hasta: string; modo: string; destinoLat: number | null; destinoLon: number | null; segundos: number; }
interface Lugar { id: string; nombre: string; lat: number; lon: number; }
interface Reciente { id: number; estado: string; creada: string; resultado: string; texto: string; }
interface Respuesta { activo: boolean; agenteConectado: boolean; actual: Actual | null; lugares: Lugar[]; recientes: Reciente[]; }
interface Orden { estado: string; resultado: string; }

// Ubicación del iPhone: pones el iPhone en cualquier sitio del mapa (o lo "llevas" andando, en bici o en coche hasta allí) desde el PC, por cable USB, con pymobiledevice3.
// Está APAGADA por defecto. Encenderla y cada cambio piden confirmar que eres tú, cada cambio te llega por Telegram, todo termina solo (como mucho a las 12 h)
// y «Restaurar» está siempre a mano, también con la función apagada. No manda nada a ningún servicio externo: solo bajan las teselas del mapa de OpenStreetMap.
@Component({
  selector: 'app-ubicacion-iphone',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [EstadoError, Esqueleto, Interruptor, MapaUbicacion],
  styleUrls: ['../gastos/comun.css', '../gastos/dialogos.css', './ubicacion.css'],
  template: `
    <section class="caja ubic">
      <header class="caja__cab"><h3>Ubicación del iPhone</h3><span class="sub">ponlo en otro sitio, desde el PC y por cable</span></header>
      @if (fallo()) { <app-estado-error titulo="No pude cargar la ubicación" (reintentar)="cargar()" /> }
      @else if (datos() === null) { <app-esqueleto forma="bloque" alto="120px" /> }
      @else {
        <div class="ubic__fila">
          <app-interruptor [activo]="datos()!.activo" etiqueta="Permitir cambiar la ubicación del iPhone" [ocupado]="ocupado()" (activoChange)="encender($event)" />
          <span>Permitir cambiar la ubicación del iPhone</span>
        </div>

        @if (datos()!.actual; as a) {
          <div class="ubic__estado ubic__estado--viva" role="status" aria-live="polite">
            <span class="ubic__punto" aria-hidden="true"></span>
            <div class="ubic__estado-texto">
              <strong>{{ enCamino() ? 'De camino · ' + modoTexto(a.modo) : 'Simulando' }} · {{ texto(aqui()!) }}</strong>
              <small>vuelve la ubicación real en {{ restante() }}</small>
              @if (enCamino()) {
                <span class="ubic__barra" role="progressbar" aria-label="Progreso del trayecto" [attr.aria-valuenow]="progreso()" aria-valuemin="0" aria-valuemax="100"><i [style.width.%]="progreso()"></i></span>
              }
            </div>
            <button class="button button--sm ubic__parar" type="button" [disabled]="ocupado()" (click)="parar()">Restaurar la real</button>
          </div>
        } @else {
          <div class="ubic__estado" role="status" aria-live="polite">
            <span class="ubic__punto ubic__punto--real" aria-hidden="true"></span>
            <div class="ubic__estado-texto"><strong>Ubicación real</strong><small>el iPhone usa su GPS normal</small></div>
            <button class="enlace" type="button" [disabled]="ocupado()" (click)="comprobar()">Comprobar el iPhone</button>
          </div>
        }
        @if (mensaje()) { <p class="ubic__msg" role="status" aria-live="polite">{{ mensaje() }}</p> }
        @if (error()) { <p class="error" role="alert">{{ error() }}</p> }

        @if (!datos()!.activo) {
          <div class="ubic__antes">
            <p class="nada">Apagado: no se puede cambiar la ubicación. Al encenderlo se pide confirmar que eres tú.</p>
            @if (datos()!.actual) { <p class="nada">Hay una simulación en marcha: «Restaurar la real» funciona aunque esté apagado.</p> }
            <details>
              <summary>Qué necesitas para usarlo</summary>
              <ol>
                <li>El <b>iPhone conectado al PC por cable</b>, desbloqueado y con «Confiar» pulsado.</li>
                <li><b>Modo desarrollador</b> en el iPhone (Ajustes › Privacidad y seguridad › Modo desarrollador; pide reiniciar).</li>
                <li>En el PC, una vez: <code>pip install -U pymobiledevice3</code>. Windows necesita iTunes o «Apple Devices» (por el servicio USB).</li>
                <li>El agente de Windows en marcha (icono de «Zona privada» junto al reloj).</li>
              </ol>
              <p class="nota">Sirve para probar apps y para tu intimidad. Algunas apps (bancos, juegos) detectan o limitan las ubicaciones falsas: úsalo con cabeza.</p>
            </details>
          </div>
        } @else {
          <app-mapa-ubicacion [seleccion]="seleccion()" [iphone]="aqui()" (elegido)="seleccion.set($event)" />
          <p class="ubic__ayuda">Toca el mapa para elegir un sitio{{ seleccion() ? '' : ' (o pega unas coordenadas o un enlace de Google Maps)' }}.</p>

          <div class="ubic__pegar">
            <label class="campo"><span class="campo__etiqueta">Coordenadas o enlace de Google/Apple Maps</span>
              <input class="campo__input" type="text" inputmode="text" autocomplete="off" spellcheck="false" placeholder="40.4168, -3.7038" #caja (keydown.enter)="pegar(caja.value); caja.value = ''" (paste)="pegarDesdePortapapeles($event)"></label>
            <button class="button button--sm" type="button" (click)="pegar(caja.value); caja.value = ''">Ir al punto</button>
          </div>

          @if (seleccion(); as s) {
            <div class="ubic__elegido">
              <div class="ubic__elegido-cab">
                <strong>{{ texto(s) }}</strong>
                @if (distanciaSel() !== null) { <small>a {{ km(distanciaSel()!) }} de donde está el iPhone</small> }
              </div>
              <div class="ubic__duracion" role="group" aria-label="Cuánto tiempo se mantiene">
                <span>Mantener</span>
                @for (d of duraciones; track d.min) {
                  <button type="button" class="ubic__chip" [class.ubic__chip--on]="minutos() === d.min" [attr.aria-pressed]="minutos() === d.min" (click)="minutos.set(d.min)">{{ d.texto }}</button>
                }
              </div>
              <div class="ubic__acciones">
                <button class="button" type="button" [disabled]="ocupado()" (click)="fijar()">Saltar aquí</button>
                @for (m of modosVista(); track m.id) {
                  <button class="button button--ghost" type="button" [disabled]="ocupado() || !m.posible" (click)="ir(m.id)" [title]="m.motivo">
                    <span aria-hidden="true">{{ m.icono }}</span> {{ m.texto }} · {{ m.duracion }}
                  </button>
                }
              </div>
              @if (!datos()!.actual) { <p class="nota">Caminar, ir en bici o en coche sale desde donde esté el iPhone: primero salta a un punto de partida.</p> }
              @else { <p class="nota">El trayecto es en línea recta (sin calles). Al llegar se queda quieto hasta que se cumpla el tiempo.</p> }
              <div class="ubic__guardar">
                <input class="campo__input" type="text" maxlength="40" placeholder="Nombre para guardar este sitio" aria-label="Nombre para guardar este sitio" #nombre (keydown.enter)="guardar(nombre.value); nombre.value = ''">
                <button class="enlace" type="button" (click)="guardar(nombre.value); nombre.value = ''">Guardar sitio</button>
              </div>
            </div>
          }

          @if (datos()!.lugares.length) {
            <h4 class="ubic__titulo">Tus sitios</h4>
            <ul class="ubic__chips">
              @for (l of datos()!.lugares; track l.id) {
                <li>
                  <button type="button" class="ubic__chip" (click)="elegir(l)">{{ l.nombre }}</button>
                  <button type="button" class="ubic__x" (click)="borrarLugar(l)" [attr.aria-label]="'Borrar ' + l.nombre">×</button>
                </li>
              }
            </ul>
          }

          <details class="ubic__ciudades">
            <summary>Ciudades famosas</summary>
            <ul class="ubic__chips">
              @for (c of ciudades; track c.nombre) { <li><button type="button" class="ubic__chip" (click)="elegir(c)">{{ c.nombre }}</button></li> }
            </ul>
          </details>

          @if (datos()!.recientes.length) {
            <details class="ubic__recientes">
              <summary>Últimos cambios</summary>
              <ul>
                @for (r of datos()!.recientes; track r.id) {
                  <li><span>{{ r.texto }}</span><small>{{ cuando(r.creada) }} · {{ r.estado === 'hecha' ? 'hecho' : r.estado === 'error' ? 'falló: ' + r.resultado : r.estado }}</small></li>
                }
              </ul>
            </details>
          }
          @if (!datos()!.agenteConectado) { <p class="error" role="alert">El agente de Windows no responde: abre «Zona privada» (icono junto al reloj).</p> }
        }
      }
    </section>
  `,
})
export class UbicacionIphone implements OnInit, OnDestroy {
  private readonly http = inject(HttpClient);
  private readonly confirmar = inject(ConfirmarService);

  protected readonly datos = signal<Respuesta | null>(null);
  protected readonly fallo = signal(false);
  protected readonly ocupado = signal(false);
  protected readonly mensaje = signal('');
  protected readonly error = signal('');
  protected readonly seleccion = signal<Punto | null>(null);
  protected readonly minutos = signal(120);
  protected readonly ahora = signal(Date.now());
  protected readonly duraciones = DURACIONES;
  protected readonly ciudades = CIUDADES;
  protected readonly texto = texto;

  // dónde está el iPhone ahora (calculado por el tiempo que lleva de camino)
  protected readonly aqui = computed<Punto | null>(() => { const a = this.datos()?.actual; return a ? posicionAhora(a, this.ahora()) : null; });
  protected readonly enCamino = computed(() => { const a = this.datos()?.actual; return !!a && a.destinoLat != null && a.segundos > 0; });
  protected readonly progreso = computed(() => {
    const a = this.datos()?.actual;
    return a && a.segundos > 0 ? Math.min(100, Math.max(0, Math.round(((this.ahora() - new Date(a.desde).getTime()) / 1000 / a.segundos) * 100))) : 0;
  });
  protected readonly restante = computed(() => { const a = this.datos()?.actual; return a ? queda(new Date(a.hasta), new Date(this.ahora())) : ''; });
  protected readonly distanciaSel = computed(() => { const s = this.seleccion(), p = this.aqui(); return s && p ? distancia(p, s) : null; });
  protected readonly modosVista = computed(() => {
    const d = this.distanciaSel();
    const hay = !!this.datos()?.actual;
    return MODOS.map((m) => {
      if (d === null) return { ...m, duracion: '—', posible: false, motivo: 'Primero salta a un punto de partida' };
      const s = segundos(d, m.id);
      return { ...m, duracion: duracion(s), posible: hay && d >= 5 && s <= 720 * 60, motivo: !hay ? 'Primero salta a un punto de partida' : d < 5 ? 'Ya estás ahí' : s > 720 * 60 ? 'Tarda más de 12 h' : '' };
    });
  });

  private reloj?: ReturnType<typeof setInterval>;

  ngOnInit(): void {
    void this.cargar();
    this.reloj = setInterval(() => {
      this.ahora.set(Date.now());
      const a = this.datos()?.actual;
      if (a && Date.now() > new Date(a.hasta).getTime() + 1500 && !this.ocupado()) void this.cargar(); // se cumplió el tiempo: la API ya no la da por activa
    }, 1000);
  }

  ngOnDestroy(): void {
    if (this.reloj) clearInterval(this.reloj);
  }

  protected async cargar(): Promise<void> {
    this.fallo.set(false);
    try { this.datos.set(await firstValueFrom(this.http.get<Respuesta>('/api/pc/ubicacion'))); } catch { this.fallo.set(true); }
  }

  protected modoTexto(m: string): string { return MODOS.find((x) => x.id === m)?.texto.toLowerCase() ?? m; }
  protected km(m: number): string { return kilometros(m); }
  protected cuando(iso: string): string { return new Date(iso).toLocaleString('es-ES', { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' }); }

  protected elegir(p: Punto): void {
    this.seleccion.set({ lat: p.lat, lon: p.lon });
  }

  protected pegar(t: string): void {
    this.error.set('');
    const p = leerCoordenadas(t);
    if (!p) { if (t.trim()) this.error.set('No entiendo esas coordenadas: prueba «40.4168, -3.7038» o pega el enlace de Google Maps.'); return; }
    this.elegir(p);
  }

  protected pegarDesdePortapapeles(e: ClipboardEvent): void {
    const t = e.clipboardData?.getData('text');
    if (t && leerCoordenadas(t)) { e.preventDefault(); this.pegar(t); (e.target as HTMLInputElement).value = ''; }
  }

  // ---- las órdenes ----

  // una petición que puede pedir «confirma que eres tú» (403 reautenticar): se confirma y se repite una vez
  private async conConfirmacion<T>(accion: () => Promise<T>): Promise<T | undefined> {
    for (let intento = 0; ; intento++) {
      try { return await accion(); }
      catch (e) {
        const x = e as HttpErrorResponse;
        if (x.status === 403 && x.error?.reautenticar && intento === 0) { if (!(await this.confirmar.pedir('Confirma que eres tú para cambiar la ubicación del iPhone.'))) return undefined; continue; }
        throw e;
      }
    }
  }

  private textoError(e: unknown): string {
    const x = e as HttpErrorResponse;
    const errores = x.error?.errors as Record<string, string[]> | undefined;
    return x.error?.detail ?? x.error?.detalle ?? (errores ? Object.values(errores).flat()[0] : null) ?? 'No se ha podido.';
  }

  // manda la orden y espera a que el agente diga qué tal (abrir el túnel con el iPhone puede tardar unos 12 s)
  private async orden(ruta: string, cuerpo: object, esperando: string, hecho: string): Promise<boolean> {
    this.error.set(''); this.mensaje.set(esperando); this.ocupado.set(true);
    try {
      const r = await this.conConfirmacion(() => firstValueFrom(this.http.post<{ orden: number }>(`/api/pc/ubicacion/${ruta}`, cuerpo)));
      if (!r) { this.mensaje.set(''); return false; }
      for (let i = 0; i < 40; i++) {
        await new Promise((res) => setTimeout(res, 1500));
        const o = await firstValueFrom(this.http.get<Orden>(`/api/pc/ordenes/${r.orden}`));
        if (o.estado === 'hecha') { this.mensaje.set(o.resultado ? `${hecho}: ${o.resultado}` : hecho); await this.cargar(); return true; }
        if (o.estado === 'error' || o.estado === 'caducada') {
          this.mensaje.set('');
          this.error.set(o.estado === 'caducada' ? 'El agente no la recogió a tiempo: ¿está en marcha?' : (o.resultado || 'No se pudo.'));
          await this.cargar();
          return false;
        }
      }
      this.mensaje.set(''); this.error.set('El agente tarda demasiado en contestar.');
      return false;
    } catch (e) { this.mensaje.set(''); this.error.set(this.textoError(e)); return false; }
    finally { this.ocupado.set(false); }
  }

  protected async encender(activo: boolean): Promise<void> {
    this.error.set(''); this.ocupado.set(true);
    try {
      const r = await this.conConfirmacion(() => firstValueFrom(this.http.put<{ activo: boolean }>('/api/pc/ubicacion/ajustes', { activo })));
      if (r) this.mensaje.set(activo ? 'Encendido. Cada cambio te lo pedirá otra vez y te llegará por Telegram.' : 'Apagado. Si había una simulación, se restaura.');
    } catch (e) { this.error.set(this.textoError(e)); }
    finally { this.ocupado.set(false); await this.cargar(); }
  }

  protected fijar(): Promise<boolean> {
    const s = this.seleccion();
    if (!s || !coordenadasValidas(s.lat, s.lon)) return Promise.resolve(false);
    return this.orden('fijar', { lat: s.lat, lon: s.lon, minutos: this.minutos() }, 'Pidiéndoselo al iPhone… (puede tardar unos segundos)', 'Hecho');
  }

  protected ir(modo: Modo): Promise<boolean> {
    const s = this.seleccion(), d = this.distanciaSel();
    if (!s || d === null) return Promise.resolve(false);
    // el tiempo que se mantiene al llegar nunca es menor que lo que tarda el trayecto
    const trayecto = Math.ceil(segundos(d, modo) / 60);
    const minutos = Math.min(720, Math.max(this.minutos(), trayecto + 5));
    return this.orden('ir', { lat: s.lat, lon: s.lon, minutos, modo }, 'Poniéndose en marcha…', 'En camino');
  }

  protected parar(): Promise<boolean> { return this.orden('parar', {}, 'Restaurando la ubicación real…', 'Listo'); }
  protected comprobar(): Promise<boolean> { return this.orden('comprobar', {}, 'Mirando el iPhone…', 'iPhone'); }

  protected async guardar(nombre: string): Promise<void> {
    const s = this.seleccion();
    if (!s) return;
    this.error.set('');
    try {
      const lugares = await firstValueFrom(this.http.post<Lugar[]>('/api/pc/ubicacion/lugares', { nombre, lat: s.lat, lon: s.lon }));
      this.datos.update((d) => (d ? { ...d, lugares } : d));
      this.mensaje.set('Sitio guardado.');
    } catch (e) { this.error.set(this.textoError(e)); }
  }

  protected async borrarLugar(l: Lugar): Promise<void> {
    try { const lugares = await firstValueFrom(this.http.delete<Lugar[]>(`/api/pc/ubicacion/lugares/${l.id}`)); this.datos.update((d) => (d ? { ...d, lugares } : d)); }
    catch (e) { this.error.set(this.textoError(e)); }
  }
}
