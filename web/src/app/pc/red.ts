import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ConfirmarService } from '../core/confirmar.service';
import { GraficaRed } from './grafica-red';

interface Aparato { id: number; nombre: string; host: string; puerto: number; icono: string; avisar: boolean; mac: string; presencia: boolean; historial: boolean; enLinea: boolean | null; latenciaMs: number | null; comprobado: string | null; cambio: string | null; }

const ICONOS: Record<string, string> = { pc: '🖥️', movil: '📱', nas: '🗄️', impresora: '🖨️', camara: '📷', tv: '📺', router: '📡', otro: '🔌' };
const NOMBRES: Record<string, string> = { pc: 'Ordenador', movil: 'Móvil', nas: 'NAS / disco', impresora: 'Impresora', camara: 'Cámara', tv: 'Televisión', router: 'Router', otro: 'Otro' };

// Los aparatos de tu red de casa: la API prueba cada minuto si responden y te avisa por Telegram cuando uno se apaga o vuelve.
@Component({
  selector: 'app-red-casa',
  imports: [GraficaRed],
  styleUrls: ['../gastos/comun.css', './modulos.css'],
  template: `
    <section class="caja">
      <header class="caja__cab"><h3>Aparatos de casa</h3><span class="sub">{{ enLinea() }} de {{ aparatos().length }} en línea</span></header>
      <ul class="aparatos">
        @for (a of aparatos(); track a.id) {
          <li class="aparato" [class.aparato--caido]="a.enLinea === false">
            <span class="aparato__icono" aria-hidden="true">{{ iconos[a.icono] ?? '🔌' }}</span>
            <span class="aparato__texto"><strong>{{ a.nombre }}{{ a.presencia ? ' · 📍 presencia' : '' }}</strong>
              <small>{{ a.host }}:{{ a.puerto }} · {{ a.enLinea === null ? 'sin comprobar' : a.enLinea ? 'en línea' + (a.latenciaMs != null ? ' · ' + a.latenciaMs + ' ms' : '') : 'sin respuesta' + (a.cambio ? ' desde ' + hace(a.cambio) : '') }}</small></span>
            <span class="punto" [class.punto--ok]="a.enLinea === true" [class.punto--mal]="a.enLinea === false" [attr.aria-label]="a.enLinea ? 'en línea' : 'sin respuesta'"></span>
            <span class="aparato__acciones">
              @if (a.historial) { <button class="enlace" type="button" (click)="grafica.set(grafica() === a.id ? null : a.id)" [attr.aria-expanded]="grafica() === a.id">Gráfica</button> }
              @if (a.mac) { <button class="enlace" type="button" (click)="despertar(a)" [attr.aria-label]="'Despertar ' + a.nombre">Despertar</button> }
              <button class="enlace" type="button" (click)="probar(a)">Probar</button>
              @if (borrando() === a.id) { <button class="enlace enlace--peligro" type="button" (click)="borrar(a)">¿Seguro?</button> }
              @else { <button class="enlace" type="button" (click)="borrando.set(a.id)" [attr.aria-label]="'Quitar ' + a.nombre">Quitar</button> }
            </span>
            @if (grafica() === a.id) { <app-grafica-red class="aparato__grafica" [id]="a.id" /> }
          </li>
        } @empty { <li class="nada">Aún no vigilas ningún aparato. Añade el router, la impresora, el NAS…</li> }
      </ul>
      @if (mensaje()) { <p class="msg" role="status">{{ mensaje() }}</p> }

      @if (!formulario()) { <button class="boton" type="button" (click)="formulario.set(true)">+ Añadir un aparato</button> }
      @else {
        <form class="alta" (submit)="anadir($event)" novalidate>
          <label class="campo"><span class="campo__etiqueta">Nombre</span><input class="campo__input" maxlength="60" [value]="nombre()" (input)="nombre.set($any($event.target).value)" placeholder="Impresora del salón"></label>
          <div class="fila2">
            <label class="campo"><span class="campo__etiqueta">IP o nombre en la red</span><input class="campo__input" maxlength="120" [value]="host()" (input)="host.set($any($event.target).value)" placeholder="192.168.1.20" inputmode="url" autocapitalize="off"></label>
            <label class="campo campo--corto"><span class="campo__etiqueta">Puerto</span><input class="campo__input" inputmode="numeric" [value]="puerto()" (input)="puerto.set($any($event.target).value)"></label>
          </div>
          <label class="campo"><span class="campo__etiqueta">Dirección MAC (opcional, para poder despertarlo)</span><input class="campo__input" maxlength="17" [value]="mac()" (input)="mac.set($any($event.target).value)" placeholder="AA:BB:CC:DD:EE:FF" autocapitalize="characters" spellcheck="false"></label>
          <label class="campo"><span class="campo__etiqueta">Tipo</span>
            <select class="campo__input" [value]="icono()" (change)="icono.set($any($event.target).value)">@for (k of claves; track k) { <option [value]="k">{{ iconos[k] }} {{ nombres[k] }}</option> }</select></label>
          <p class="nada">Se comprueba abriendo una conexión a ese puerto (80 o 443 para la web del aparato, 9100 impresoras, 445 NAS, 554 cámaras). Solo direcciones de tu red de casa o de Tailscale.</p>
          <label class="interruptor"><input type="checkbox" [checked]="historial()" (change)="historial.set($any($event.target).checked)"><span><strong>Guardar su historial de latencia</strong><small>Una lectura por minuto para ver la gráfica de lo rápido que contesta y cuándo se cae (pensado para el router; hasta 3 aparatos).</small></span></label>
          <label class="interruptor"><input type="checkbox" [checked]="presencia()" (change)="presencia.set($any($event.target).checked)"><span><strong>Es mi móvil: úsalo para saber si estoy en casa</strong><small>Al llegar se aplica «llegué a casa» y, si lleva 10 minutos sin estar en la red, «me voy». Pon la IP fija del móvil en la red de casa (o su IP de Tailscale).</small></span></label>
          <label class="interruptor"><input type="checkbox" [checked]="avisar()" (change)="avisar.set($any($event.target).checked)"><span><strong>Avisarme por Telegram si se apaga</strong></span></label>
          @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
          <div class="acciones"><button class="enlace" type="button" (click)="formulario.set(false)">Cancelar</button><button class="button" type="submit" [disabled]="ocupado()">{{ ocupado() ? 'Probando…' : 'Añadir' }}</button></div>
        </form>
      }
    </section>
  `,
})
export class RedCasa implements OnInit {
  private readonly http = inject(HttpClient);
  protected readonly iconos = ICONOS;
  protected readonly nombres = NOMBRES;
  protected readonly claves = Object.keys(ICONOS);
  protected readonly aparatos = signal<Aparato[]>([]);
  protected readonly enLinea = computed(() => this.aparatos().filter((a) => a.enLinea).length);
  protected readonly formulario = signal(false);
  protected readonly borrando = signal<number | null>(null);
  protected readonly ocupado = signal(false);
  protected readonly error = signal('');
  protected readonly nombre = signal('');
  protected readonly host = signal('');
  protected readonly puerto = signal('80');
  protected readonly icono = signal('otro');
  protected readonly mac = signal('');
  protected readonly presencia = signal(false);
  protected readonly historial = signal(false);
  protected readonly grafica = signal<number | null>(null);
  protected readonly mensaje = signal('');
  private readonly confirmar = inject(ConfirmarService);
  protected readonly avisar = signal(true);

  constructor() {
    const t = setInterval(() => { if (!document.hidden) void this.leer(); }, 30_000);
    inject(DestroyRef).onDestroy(() => clearInterval(t));
  }

  ngOnInit(): void { void this.leer(); }

  private async leer(): Promise<void> {
    try { this.aparatos.set(await firstValueFrom(this.http.get<Aparato[]>('/api/red'))); } catch { /* se queda como estaba */ }
  }

  protected hace(iso: string): string {
    const m = Math.round((Date.now() - new Date(iso).getTime()) / 60000);
    return m < 1 ? 'hace un momento' : m < 60 ? `hace ${m} min` : m < 1440 ? `hace ${Math.round(m / 60)} h` : `hace ${Math.round(m / 1440)} d`;
  }

  protected async anadir(ev: Event): Promise<void> {
    ev.preventDefault();
    this.ocupado.set(true); this.error.set('');
    try {
      await firstValueFrom(this.http.post('/api/red', { nombre: this.nombre(), host: this.host(), puerto: Number(this.puerto()), icono: this.icono(), avisar: this.avisar(), mac: this.mac(), presencia: this.presencia(), historial: this.historial() }));
      this.formulario.set(false); this.nombre.set(''); this.host.set(''); this.puerto.set('80'); this.mac.set(''); this.presencia.set(false); this.historial.set(false);
      await this.leer();
    } catch (e) {
      const d = (e as HttpErrorResponse).error as { errors?: Record<string, string[]> } | undefined;
      this.error.set(Object.values(d?.errors ?? {})[0]?.[0] ?? 'No se pudo añadir.');
    } finally { this.ocupado.set(false); }
  }

  // Wake-on-LAN: lo manda el agente desde el PC; pide Face ID (o la contraseña) y el agente en marcha
  protected async despertar(a: Aparato): Promise<void> {
    this.mensaje.set('');
    for (let intento = 0; intento < 2; intento++) {
      try {
        await firstValueFrom(this.http.post(`/api/red/${a.id}/despertar`, null));
        this.mensaje.set(`Paquete de despertar enviado a «${a.nombre}». Si está apagado, tarda unos segundos en arrancar.`);
        return;
      } catch (e) {
        const x = e as HttpErrorResponse;
        if (x.status === 403 && x.error?.reautenticar && intento === 0) { if (!(await this.confirmar.pedir(`Confirma que eres tú para despertar «${a.nombre}».`))) return; continue; }
        this.mensaje.set(x.error?.detalle ?? x.error?.errors?.mac?.[0] ?? 'No se pudo mandar la orden.');
        return;
      }
    }
  }

  protected async probar(a: Aparato): Promise<void> {
    try { const r = await firstValueFrom(this.http.post<Aparato>(`/api/red/${a.id}/probar`, null)); this.aparatos.update((l) => l.map((x) => (x.id === a.id ? r : x))); } catch { /* sin conexión */ }
  }

  protected async borrar(a: Aparato): Promise<void> {
    this.borrando.set(null);
    await firstValueFrom(this.http.delete(`/api/red/${a.id}`));
    await this.leer();
  }
}
