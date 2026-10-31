import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

interface Disco { nombre: string; modelo: string; salud: string; estado: string; temperaturaC: number | null; desgastePct: number | null; erroresLectura: number | null; }
interface Volumen { nombre: string; libreBytes: number; totalBytes: number; }
interface Orden { id: number; tipo: string; parametro: string; estado: string; creada: string; resultado: string; desde: string; }
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
  template: `
    <section class="caja">
      <header class="caja__cab">
        <h3>Mi PC</h3>
        <span class="sub">{{ e()?.agenteConectado ? '🟢 el agente de Windows está conectado' : '🔴 el agente de Windows no responde: arráncalo con arrancar.cmd (carpeta agente)' }}</span>
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

    <section class="caja">
      <header class="caja__cab"><h3>Órdenes</h3><span class="sub">Piden tu contraseña otra vez · avisan a Telegram · caducan a los 60 s</span></header>
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
          @if (pedirClave()) {
            <input type="text" autocomplete="username" hidden>
            <label class="campo"><span class="campo__etiqueta">Tu contraseña</span>
              <input class="campo__input" type="password" autocomplete="current-password" [value]="clave()" (input)="clave.set($any($event.target).value)"></label>
            <label class="campo"><span class="campo__etiqueta">Código de la app de dos pasos (si lo tienes)</span>
              <input class="campo__input" inputmode="numeric" autocomplete="one-time-code" maxlength="12" [value]="codigo()" (input)="codigo.set($any($event.target).value)"></label>
          }
          @if (error()) { <p class="error">{{ error() }}</p> }
          <footer class="pie">
            <button class="enlace" type="button" (click)="cancelar()">Cancelar</button>
            <button class="button" type="submit" [disabled]="ocupado()">{{ ocupado() ? 'Enviando…' : 'Confirmar' }}</button>
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
    this.cargado.set(true);
  }

  protected usado(v: Volumen): number { return v.totalBytes ? Math.round(100 - (100 * v.libreBytes) / v.totalBytes) : 0; }
  protected gb(b: number): string { return `${(b / 1_073_741_824).toFixed(b < 10_737_418_240 ? 1 : 0)} GB`; }
  protected nombre(o: Orden): string { return ORDENES.find((x) => x.tipo === o.tipo && (x.parametro ?? '') === o.parametro)?.texto ?? o.tipo; }

  protected elegir(o: (typeof ORDENES)[number]): void {
    this.pendiente.set(o); this.error.set(''); this.mensaje.set('');
    this.pedirClave.set(!this.e()?.reautenticado);
    // si aún vale la contraseña de hace un momento, solo se pide confirmar
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
