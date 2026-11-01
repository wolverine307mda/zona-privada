import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

interface Aparato { id: number; nombre: string; host: string; puerto: number; icono: string; avisar: boolean; enLinea: boolean | null; latenciaMs: number | null; comprobado: string | null; cambio: string | null; }

const ICONOS: Record<string, string> = { pc: '🖥️', movil: '📱', nas: '🗄️', impresora: '🖨️', camara: '📷', tv: '📺', router: '📡', otro: '🔌' };
const NOMBRES: Record<string, string> = { pc: 'Ordenador', movil: 'Móvil', nas: 'NAS / disco', impresora: 'Impresora', camara: 'Cámara', tv: 'Televisión', router: 'Router', otro: 'Otro' };

// Los aparatos de tu red de casa: la API prueba cada minuto si responden y te avisa por Telegram cuando uno se apaga o vuelve.
@Component({
  selector: 'app-red-casa',
  styleUrls: ['../gastos/comun.css', './modulos.css'],
  template: `
    <section class="caja">
      <header class="caja__cab"><h3>Aparatos de casa</h3><span class="sub">{{ enLinea() }} de {{ aparatos().length }} en línea</span></header>
      <ul class="aparatos">
        @for (a of aparatos(); track a.id) {
          <li class="aparato" [class.aparato--caido]="a.enLinea === false">
            <span class="aparato__icono" aria-hidden="true">{{ iconos[a.icono] ?? '🔌' }}</span>
            <span class="aparato__texto"><strong>{{ a.nombre }}</strong>
              <small>{{ a.host }}:{{ a.puerto }} · {{ a.enLinea === null ? 'sin comprobar' : a.enLinea ? 'en línea' + (a.latenciaMs != null ? ' · ' + a.latenciaMs + ' ms' : '') : 'sin respuesta' + (a.cambio ? ' desde ' + hace(a.cambio) : '') }}</small></span>
            <span class="punto" [class.punto--ok]="a.enLinea === true" [class.punto--mal]="a.enLinea === false" [attr.aria-label]="a.enLinea ? 'en línea' : 'sin respuesta'"></span>
            <button class="enlace" type="button" (click)="probar(a)">Probar</button>
            @if (borrando() === a.id) { <button class="enlace enlace--peligro" type="button" (click)="borrar(a)">¿Seguro?</button> }
            @else { <button class="enlace" type="button" (click)="borrando.set(a.id)" [attr.aria-label]="'Quitar ' + a.nombre">Quitar</button> }
          </li>
        } @empty { <li class="nada">Aún no vigilas ningún aparato. Añade el router, la impresora, el NAS…</li> }
      </ul>

      @if (!formulario()) { <button class="boton" type="button" (click)="formulario.set(true)">+ Añadir un aparato</button> }
      @else {
        <form class="alta" (submit)="anadir($event)" novalidate>
          <label class="campo"><span class="campo__etiqueta">Nombre</span><input class="campo__input" maxlength="60" [value]="nombre()" (input)="nombre.set($any($event.target).value)" placeholder="Impresora del salón"></label>
          <div class="fila2">
            <label class="campo"><span class="campo__etiqueta">IP o nombre en la red</span><input class="campo__input" maxlength="120" [value]="host()" (input)="host.set($any($event.target).value)" placeholder="192.168.1.20" inputmode="url" autocapitalize="off"></label>
            <label class="campo campo--corto"><span class="campo__etiqueta">Puerto</span><input class="campo__input" inputmode="numeric" [value]="puerto()" (input)="puerto.set($any($event.target).value)"></label>
          </div>
          <label class="campo"><span class="campo__etiqueta">Tipo</span>
            <select class="campo__input" [value]="icono()" (change)="icono.set($any($event.target).value)">@for (k of claves; track k) { <option [value]="k">{{ iconos[k] }} {{ nombres[k] }}</option> }</select></label>
          <p class="nada">Se comprueba abriendo una conexión a ese puerto (80 o 443 para la web del aparato, 9100 impresoras, 445 NAS, 554 cámaras). Solo direcciones de tu red de casa o de Tailscale.</p>
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
      await firstValueFrom(this.http.post('/api/red', { nombre: this.nombre(), host: this.host(), puerto: Number(this.puerto()), icono: this.icono(), avisar: this.avisar() }));
      this.formulario.set(false); this.nombre.set(''); this.host.set(''); this.puerto.set('80');
      await this.leer();
    } catch (e) {
      const d = (e as HttpErrorResponse).error as { errors?: Record<string, string[]> } | undefined;
      this.error.set(Object.values(d?.errors ?? {})[0]?.[0] ?? 'No se pudo añadir.');
    } finally { this.ocupado.set(false); }
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
