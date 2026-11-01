import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Reautenticar } from '../core/reautenticar';
import { SistemaService } from '../inicio/sistema.service';
import { DatosPc, Pc3d } from '../inicio/pc-3d';
import { CasaPc } from './casa';
import { PortapapelesPc } from './portapapeles';
import { RedCasa } from './red';

interface Disco { nombre: string; modelo: string; salud: string; estado: string; temperaturaC: number | null; desgastePct: number | null; erroresLectura: number | null; }
interface Volumen { nombre: string; libreBytes: number; totalBytes: number; }
interface Orden { id: number; tipo: string; parametro: string; estado: string; creada: string; resultado: string; desde: string; }
interface DiaSalud { fecha: string; volumenes: Volumen[]; discos: { nombre: string; desgastePct: number | null; temperaturaC: number | null }[]; }
interface Tendencia { titulo: string; unidad: string; puntos: string; actual: number; cambio: number | null; dias: number; area: string; }
interface Estado {
  agenteConectado: boolean; saludHace: number | null; reautenticado: boolean; ordenes: Orden[];
  salud: { discos: Disco[]; volumenes: Volumen[]; reinicioPendiente: boolean } | null;
}

const ORDENES: { tipo: string; texto: string; icono: string; peligro?: boolean; parametro?: string; aviso?: string }[] = [
  { tipo: 'bloquear', texto: 'Bloquear la pantalla', icono: '🔒' },
  { tipo: 'despierto', texto: 'Mantener despierto', icono: '☕', parametro: 'on' },
  { tipo: 'despierto', texto: 'Dejar que se suspenda', icono: '💤', parametro: 'off' },
  { tipo: 'reiniciar', texto: 'Reiniciar', icono: '🔄', peligro: true, aviso: 'Se reinicia en 30 s. La app vuelve sola cuando arranque Windows y Docker.' },
  { tipo: 'suspender', texto: 'Suspender', icono: '🌙', peligro: true, aviso: 'Un PC suspendido NO se puede despertar desde fuera: no podrás entrar hasta que lo despiertes tú.' },
  { tipo: 'apagar', texto: 'Apagar', icono: '⏻', peligro: true, aviso: 'Se apaga en 30 s y no hay forma de encenderlo desde fuera. Hasta que lo enciendas tú, la app y la web no responden.' },
  { tipo: 'cancelar', texto: 'Cancelar apagado o reinicio', icono: '✖' },
];

// Mi PC: cómo está (espacio, discos, reinicio pendiente) y unas pocas órdenes. Cada orden pide la contraseña otra vez
// (y el código de dos pasos si lo tienes) y avisa a Telegram; el agente de Windows solo ejecuta una lista cerrada y firmada.
@Component({
  selector: 'app-pc',
  imports: [Pc3d, CasaPc, RedCasa, PortapapelesPc],
  template: `
    <section class="caja pc3d">
      <header class="caja__cab"><h3>Tu PC por dentro</h3><span class="sub">{{ a()?.origen === 'equipo' ? 'en directo · arrastra para girarlo' : 'midiendo Docker · arranca el agente' }}</span></header>
      <div class="pc3d__escena"><app-pc-3d [datos]="datosPc()" /></div>
    </section>

    @if (e()?.agenteConectado && (a()?.bateria != null || a()?.brillo != null || a()?.volumen != null)) {
      <section class="caja">
        <header class="caja__cab"><h3>Portátil</h3><span class="sub">batería, pantalla y sonido</span></header>
        @if (a()?.bateria != null) {
          <div class="bateria" role="img" [attr.aria-label]="'Batería al ' + a()!.bateria + ' por ciento' + (a()!.cargando ? ', enchufado' : '')">
            <span class="bateria__icono" [class.baja]="a()!.bateria! <= 20 && !a()!.cargando"><i [style.width.%]="a()!.bateria"></i></span>
            <strong>{{ a()!.bateria }} %</strong><small>{{ a()!.cargando ? 'enchufado' : 'con batería' }}</small>
          </div>
        }
        @if (a()?.brillo != null) {
          <label class="nivel"><span>Brillo de la pantalla <b>{{ brillo() ?? a()!.brillo }} %</b></span>
            <input type="range" min="0" max="100" step="5" [value]="brillo() ?? a()!.brillo" (input)="brillo.set(+$any($event.target).value)" (change)="nivel('brillo', +$any($event.target).value)"></label>
        }
        @if (a()?.volumen != null) {
          <label class="nivel"><span>Volumen <b>{{ volumen() ?? a()!.volumen }} %</b></span>
            <input type="range" min="0" max="100" step="5" [value]="volumen() ?? a()!.volumen" (input)="volumen.set(+$any($event.target).value)" (change)="nivel('volumen', +$any($event.target).value)"></label>
        }
        @if (aviso()) { <p class="msg" role="status">{{ aviso() }}</p> }
      </section>
    }

    <app-casa-pc />
    <app-red-casa />
    <app-portapapeles-pc />

    <section class="caja">
      <header class="caja__cab">
        <h3>Mi PC</h3>
        <span class="sub">{{ e()?.agenteConectado ? '🟢 el agente de Windows está conectado' : '🔴 el agente de Windows no responde: abre «Zona privada» (icono junto al reloj) o agente\arrancar.cmd' }}</span>
      </header>
      @if (e()?.salud; as s) {
        <p class="sub">{{ hace() }}</p>
        @for (v of s.volumenes; track v.nombre) {
          <div class="vol">
            <strong>{{ v.nombre }}</strong>
            <div class="barra" role="img" [attr.aria-label]="'Ocupado ' + usado(v) + ' %'"><i [style.width.%]="usado(v)" [class.aviso]="usado(v) >= 85" [class.rojo]="usado(v) >= 92"></i></div>
            <small>{{ gb(v.libreBytes) }} libres de {{ gb(v.totalBytes) }}</small>
          </div>
        }
        @for (d of s.discos; track d.nombre) {
          <p class="disco" [class.mal]="d.salud !== 'Healthy'">
            <span aria-hidden="true">{{ d.salud === 'Healthy' ? '✅' : '⚠️' }}</span>
            <span><strong>{{ d.modelo }}</strong>
              <small>{{ d.salud === 'Healthy' ? 'Sano' : d.salud }}{{ d.temperaturaC != null ? ' · ' + d.temperaturaC + ' °C' : '' }}{{ d.desgastePct != null ? ' · desgaste ' + d.desgastePct + ' %' : '' }}</small></span>
          </p>
        }
        @if (s.reinicioPendiente) { <p class="disco mal"><span aria-hidden="true">🔄</span><span><strong>Windows espera un reinicio</strong><small>Para terminar de instalar actualizaciones.</small></span></p> }
        @if (s.discos.length && s.discos.every(d => d.temperaturaC == null)) {
          <p class="sub">La temperatura y el desgaste del disco solo se leen si el agente se ejecuta como administrador.</p>
        }
      } @else {
        <p class="vacio">{{ cargado() ? 'Aún no ha llegado la salud del PC: el agente la manda cada 5 minutos.' : 'Cargando…' }}</p>
      }
    </section>

    @if (tendencias().length) {
      <section class="caja">
        <header class="caja__cab"><h3>Tendencia</h3><span class="sub">una foto al día, últimos {{ diasHistorial() }} días</span></header>
        <div class="tendencias">
          @for (t of tendencias(); track t.titulo) {
            <div class="tendencia">
              <strong>{{ t.titulo }}</strong>
              <svg viewBox="0 0 160 44" preserveAspectRatio="none" role="img" [attr.aria-label]="t.titulo + ': ' + t.actual + ' ' + t.unidad">
                <polygon [attr.points]="t.area" class="area"/><polyline [attr.points]="t.puntos" class="linea"/>
              </svg>
              <small>{{ t.actual }} {{ t.unidad }}{{ t.cambio == null ? '' : ' · ' + (t.cambio > 0 ? '+' : '') + t.cambio + ' ' + t.unidad + ' al mes' }}</small>
            </div>
          }
        </div>
      </section>
    }

    <section class="caja">
      <header class="caja__cab"><h3>Órdenes</h3><span class="sub">Piden Face ID o tu contraseña · avisan a Telegram · caducan a los 60 s</span></header>
      @if (mensaje()) { <p class="msg" role="status">{{ mensaje() }}</p> }
      <div class="ordenes">
        @for (o of ordenes; track o.texto) {
          <button class="orden" type="button" [class.peligro]="o.peligro" [disabled]="!e()?.agenteConectado || ocupado()" (click)="elegir(o)">
            <span aria-hidden="true">{{ o.icono }}</span> {{ o.texto }}
          </button>
        }
      </div>
      @if (pendiente(); as p) {
        <form class="reauth" (submit)="confirmar($event)" novalidate>
          <p><strong>{{ p.texto }}</strong>{{ p.aviso ? ' — ' + p.aviso : '' }}</p>
          @if (pedirClave() && reauth.conPasskey) {
            <button class="button cara" type="button" (click)="conCara()" [disabled]="ocupado()">
              <svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M4 8V6a2 2 0 0 1 2-2h2M16 4h2a2 2 0 0 1 2 2v2M20 16v2a2 2 0 0 1-2 2h-2M8 20H6a2 2 0 0 1-2-2v-2M9 10v1M15 10v1M12 10v4h-1M9 16c1.5 1.3 4.5 1.3 6 0"/></svg>
              {{ ocupado() ? 'Comprobando…' : 'Confirmar con Face ID' }}
            </button>
            @if (!verClave()) { <button class="enlace" type="button" (click)="verClave.set(true)">Usar la contraseña</button> }
          }
          @if (pedirClave() && (!reauth.conPasskey || verClave())) {
            <input type="text" autocomplete="username" hidden>
            <label class="campo"><span class="campo__etiqueta">Tu contraseña</span>
              <input class="campo__input" type="password" autocomplete="current-password" [value]="clave()" (input)="clave.set($any($event.target).value)"></label>
            <label class="campo"><span class="campo__etiqueta">Código de la app de dos pasos (si lo tienes)</span>
              <input class="campo__input" inputmode="numeric" autocomplete="one-time-code" maxlength="12" [value]="codigo()" (input)="codigo.set($any($event.target).value)"></label>
          }
          @if (error()) { <p class="error">{{ error() }}</p> }
          <footer class="pie">
            <button class="enlace" type="button" (click)="cancelar()">Cancelar</button>
            @if (!pedirClave() || !reauth.conPasskey || verClave()) { <button class="button" type="submit" [disabled]="ocupado()">{{ ocupado() ? 'Enviando…' : 'Confirmar' }}</button> }
          </footer>
        </form>
      }
      @if (e()?.ordenes?.length) {
        <ul class="historial">
          @for (o of e()!.ordenes; track o.id) {
            <li><span>{{ nombre(o) }}</span><small>{{ o.estado }}{{ o.resultado ? ' · ' + o.resultado : '' }} · {{ o.desde }}</small></li>
          }
        </ul>
      }
    </section>
  `,
  styleUrl: './pc.css',
})
export class Pc implements OnInit, OnDestroy {
  private readonly http = inject(HttpClient);
  protected readonly reauth = inject(Reautenticar);
  private readonly sistema = inject(SistemaService);
  protected readonly a = computed(() => this.sistema.actual());
  protected readonly datosPc = computed<DatosPc>(() => {
    const m = this.a();
    const pct = (u?: number, t?: number) => (u && t ? Math.round((100 * u) / t) : 0);
    return { cpu: m?.cpu ?? 0, memoria: pct(m?.memoriaUsada, m?.memoriaTotal), disco: pct(m?.discoUsado, m?.discoTotal), real: m?.origen === 'equipo' };
  });
  protected readonly volumen = signal<number | null>(null);
  protected readonly brillo = signal<number | null>(null);
  protected readonly aviso = signal('');
  protected readonly verClave = signal(false);
  protected readonly ordenes = ORDENES;
  protected readonly e = signal<Estado | null>(null);
  protected readonly cargado = signal(false);
  protected readonly pendiente = signal<(typeof ORDENES)[number] | null>(null);
  protected readonly pedirClave = signal(false);
  protected readonly clave = signal('');
  protected readonly codigo = signal('');
  protected readonly error = signal('');
  protected readonly mensaje = signal('');
  protected readonly ocupado = signal(false);
  private temporizador?: ReturnType<typeof setInterval>;

  protected readonly historial = signal<DiaSalud[]>([]);
  protected readonly diasHistorial = computed(() => this.historial().length);
  // una línea por volumen (porcentaje ocupado) y por disco (desgaste), con lo que sube o baja al mes
  protected readonly tendencias = computed<Tendencia[]>(() => {
    const h = this.historial();
    if (h.length < 2) return [];
    const r: Tendencia[] = [];
    const nombres = new Set(h.flatMap((d) => d.volumenes.map((v) => v.nombre)));
    for (const n of nombres) {
      const serie = h.map((d) => { const v = d.volumenes.find((x) => x.nombre === n); return v && v.totalBytes ? { f: d.fecha, v: Math.round(100 - (100 * v.libreBytes) / v.totalBytes) } : null; }).filter((x) => x !== null);
      const t = this.tendencia(`Disco ${n} ocupado`, '%', serie);
      if (t) r.push(t);
    }
    const discos = new Set(h.flatMap((d) => d.discos.map((x) => x.nombre)));
    for (const n of discos) {
      const serie = h.map((d) => { const x = d.discos.find((y) => y.nombre === n); return x?.desgastePct != null ? { f: d.fecha, v: x.desgastePct } : null; }).filter((x) => x !== null);
      const t = this.tendencia(`Desgaste del disco ${n}`, '%', serie);
      if (t) r.push(t);
    }
    return r;
  });

  private tendencia(titulo: string, unidad: string, serie: { f: string; v: number }[]): Tendencia | null {
    if (serie.length < 2) return null;
    const vs = serie.map((p) => p.v), min = Math.min(...vs), max = Math.max(...vs), rango = Math.max(max - min, 2);
    const x = (i: number) => (i / (serie.length - 1)) * 160;
    const y = (v: number) => 40 - ((v - min) / rango) * 34;
    const puntos = serie.map((p, i) => `${x(i).toFixed(1)},${y(p.v).toFixed(1)}`).join(' ');
    const dias = Math.max(1, Math.round((new Date(serie[serie.length - 1].f).getTime() - new Date(serie[0].f).getTime()) / 86_400_000));
    const cambio = dias >= 7 ? Math.round(((vs[vs.length - 1] - vs[0]) / dias) * 30 * 10) / 10 : null;
    return { titulo, unidad, puntos, actual: vs[vs.length - 1], cambio, dias, area: `0,44 ${puntos} 160,44` };
  }

  protected readonly hace = computed(() => {
    const s = this.e()?.saludHace;
    return s == null ? '' : s < 90 ? 'Medido hace un momento' : `Medido hace ${Math.round(s / 60)} min`;
  });

  async ngOnInit(): Promise<void> {
    await this.cargar();
    this.temporizador = setInterval(() => void this.cargar(), 15000);
  }

  ngOnDestroy(): void { clearInterval(this.temporizador); }

  private async cargar(): Promise<void> {
    try { this.e.set(await firstValueFrom(this.http.get<Estado>('/api/pc'))); } catch { /* se reintenta en 15 s */ }
    if (!this.historial().length) firstValueFrom(this.http.get<DiaSalud[]>('/api/pc/historial')).then((h) => this.historial.set(h)).catch(() => {});
    this.cargado.set(true);
  }

  // volumen y brillo: órdenes inocuas, no piden confirmar con Face ID
  protected async nivel(tipo: 'volumen' | 'brillo', valor: number): Promise<void> {
    this.aviso.set('');
    try {
      await firstValueFrom(this.http.post('/api/pc/ordenes', { tipo, parametro: String(valor) }));
      this.aviso.set(`${tipo === 'volumen' ? 'Volumen' : 'Brillo'} al ${valor} %`);
    } catch (err) {
      this.aviso.set((err as HttpErrorResponse).status === 409 ? 'El agente de Windows no está en marcha.' : 'No se pudo cambiar.');
    }
    // en cuanto el agente cuente el valor real, el deslizador vuelve a seguirlo
    setTimeout(() => { if (tipo === 'volumen') this.volumen.set(null); else this.brillo.set(null); }, 6000);
  }

  protected usado(v: Volumen): number { return v.totalBytes ? Math.round(100 - (100 * v.libreBytes) / v.totalBytes) : 0; }
  protected gb(b: number): string { return `${(b / 1_073_741_824).toFixed(b < 10_737_418_240 ? 1 : 0)} GB`; }
  protected nombre(o: Orden): string { return ORDENES.find((x) => x.tipo === o.tipo && (x.parametro ?? '') === o.parametro)?.texto ?? o.tipo; }

  protected elegir(o: (typeof ORDENES)[number]): void {
    this.pendiente.set(o); this.error.set(''); this.mensaje.set('');
    this.pedirClave.set(!this.e()?.reautenticado);
    this.verClave.set(false);
    // si aún vale la contraseña de hace un momento, solo se pide confirmar
  }

  // Face ID: confirma y envía la orden sin escribir nada
  protected async conCara(): Promise<void> {
    const o = this.pendiente();
    if (!o || this.ocupado()) return;
    this.ocupado.set(true); this.error.set('');
    try {
      if (!(await this.reauth.confirmarConPasskey())) return;
      await this.enviar(o);
    } catch (err) {
      const x = err as HttpErrorResponse;
      this.error.set(x.status === 401 ? 'Ese passkey no sirve. Usa la contraseña.' : (x.error?.detail ?? x.error?.title ?? 'No se pudo enviar la orden.'));
      this.verClave.set(true);
    } finally { this.ocupado.set(false); }
  }

  protected cancelar(): void { this.pendiente.set(null); this.clave.set(''); this.codigo.set(''); this.error.set(''); }

  protected async confirmar(ev: Event): Promise<void> {
    ev.preventDefault();
    const o = this.pendiente();
    if (!o || this.ocupado()) return;
    this.ocupado.set(true); this.error.set('');
    try {
      if (this.pedirClave()) {
        await firstValueFrom(this.http.post('/api/seguridad/reautenticar', { password: this.clave(), codigo: this.codigo() || null }));
        this.clave.set(''); this.codigo.set('');
      }
      await this.enviar(o);
    } catch (err) {
      const x = err as HttpErrorResponse;
      if (x.status === 403 && x.error?.reautenticar) { this.pedirClave.set(true); this.error.set('Confirma tu contraseña para continuar.'); }
      else this.error.set(x.error?.detail ?? x.error?.title ?? 'No se pudo enviar la orden.');
    } finally { this.ocupado.set(false); }
  }

  private async enviar(o: (typeof ORDENES)[number]): Promise<void> {
    const orden = await firstValueFrom(this.http.post<Orden>('/api/pc/ordenes', { tipo: o.tipo, parametro: o.parametro ?? null }));
    this.pendiente.set(null);
    this.mensaje.set(`Orden enviada: ${o.texto.toLowerCase()}…`);
    // espera el resultado unos segundos
    for (let i = 0; i < 12; i++) {
      await new Promise((r) => setTimeout(r, 1500));
      const estado = await firstValueFrom(this.http.get<Orden>(`/api/pc/ordenes/${orden.id}`));
      if (estado.estado === 'hecha') { this.mensaje.set(`✅ ${estado.resultado || 'Hecho'}`); break; }
      if (estado.estado === 'error' || estado.estado === 'caducada') { this.mensaje.set(`⚠️ ${estado.estado === 'caducada' ? 'El agente no la recogió a tiempo' : estado.resultado}`); break; }
    }
    await this.cargar();
  }
}
