import { CapturasPc } from './capturas';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Reautenticar } from '../core/reautenticar';
import { SistemaService } from '../inicio/sistema.service';
import { DatosPc, Pc3d } from '../inicio/pc-3d';
import { ConfirmarService } from '../core/confirmar.service';
import { BateriaSalud } from './bateria-salud';
import { CasaPc } from './casa';
import { Escenas } from './escenas';
import { Habitacion3d } from './habitacion-3d';
import { Hogar } from './hogar';
import { Velocidad } from './velocidad';
import { BandejaPc } from './bandeja';
import { PortapapelesPc } from './portapapeles';
import { RedCasa } from './red';
import { Esqueleto } from '../ui';

interface Disco { nombre: string; modelo: string; salud: string; estado: string; temperaturaC: number | null; desgastePct: number | null; erroresLectura: number | null; }
interface Volumen { nombre: string; libreBytes: number; totalBytes: number; }
interface Orden { id: number; tipo: string; parametro: string; estado: string; creada: string; resultado: string; desde: string; }
interface DiaSalud { fecha: string; volumenes: Volumen[]; discos: { nombre: string; desgastePct: number | null; temperaturaC: number | null }[]; }
interface Tendencia { titulo: string; unidad: string; puntos: string; actual: number; cambio: number | null; dias: number; area: string; nota?: string; }
interface Estado {
  agenteConectado: boolean; saludHace: number | null; reautenticado: boolean; ordenes: Orden[];
  salud: { discos: Disco[]; volumenes: Volumen[]; reinicioPendiente: boolean } | null;
}

const ORDENES: { tipo: string; texto: string; icono: string; peligro?: boolean; parametro?: string; aviso?: string }[] = [
  { tipo: 'bloquear', texto: 'Bloquear la pantalla', icono: '🔒' },
  { tipo: 'despierto', texto: 'Mantener despierto', icono: '☕', parametro: 'on' },
  { tipo: 'despierto', texto: 'Dejar que se suspenda', icono: '💤', parametro: 'off' },
  { tipo: 'reiniciar', texto: 'Reiniciar', icono: '🔄', peligro: true, aviso: 'Se reinicia en 30 s. La app vuelve sola cuando arranque Windows y Docker.' },
  { tipo: 'apagar', texto: 'Apagar en 15 min', icono: '⏲️', parametro: '15', peligro: true, aviso: 'Se apaga dentro de 15 minutos. Puedes cancelarlo desde aquí con «Cancelar apagado o reinicio».' },
  { tipo: 'apagar', texto: 'Apagar en 1 hora', icono: '⏲️', parametro: '60', peligro: true, aviso: 'Se apaga dentro de 1 hora (para quedarte dormido viendo algo). Puedes cancelarlo desde aquí.' },
  { tipo: 'apagar', texto: 'Apagar en 2 horas', icono: '⏲️', parametro: '120', peligro: true, aviso: 'Se apaga dentro de 2 horas. Puedes cancelarlo desde aquí.' },
  { tipo: 'suspender', texto: 'Suspender', icono: '🌙', peligro: true, aviso: 'Un PC suspendido NO se puede despertar desde fuera: no podrás entrar hasta que lo despiertes tú.' },
  { tipo: 'apagar', texto: 'Apagar', icono: '⏻', peligro: true, aviso: 'Se apaga en 30 s y no hay forma de encenderlo desde fuera. Hasta que lo enciendas tú, la app y la web no responden.' },
  { tipo: 'limpiar', texto: 'Limpiar temporales y papelera', icono: '🧹', aviso: 'Borra los archivos temporales de más de un día (lo que esté en uso se queda) y vacía la papelera de reciclaje. No se puede deshacer.' },
  { tipo: 'cancelar', texto: 'Cancelar apagado o reinicio', icono: '✖' },
];

// Mi PC: cómo está (espacio, discos, reinicio pendiente) y unas pocas órdenes. Cada orden pide la contraseña otra vez
// (y el código de dos pasos si lo tienes) y avisa a Telegram; el agente de Windows solo ejecuta una lista cerrada y firmada.
@Component({
  selector: 'app-pc',
  imports: [Pc3d, BandejaPc, BateriaSalud, CapturasPc, CasaPc, Escenas, Habitacion3d, Hogar, RedCasa, PortapapelesPc, Velocidad, Esqueleto],
  templateUrl: './pc.html',
  styleUrls: ['../gastos/comun.css', './pc.css'],
})
export class Pc implements OnInit, OnDestroy {
  // las secciones de la pantalla; se recuerda la última que miraste
  protected readonly pestana = signal<'equipo' | 'controles' | 'casa'>(((): 'equipo' | 'controles' | 'casa' => {
    try { const g = localStorage.getItem('mda.pc.pestana'); return g === 'controles' || g === 'casa' ? g : 'equipo'; } catch { return 'equipo'; }
  })());
  protected ponerPestana(p: 'equipo' | 'controles' | 'casa'): void {
    this.pestana.set(p);
    try { localStorage.setItem('mda.pc.pestana', p); } catch { /* sin almacenamiento */ }
  }

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
  // el plan de energía no se puede leer del PC: se recuerda el último que se eligió desde aquí
  protected readonly planes = [
    { id: 'equilibrado', nombre: 'Equilibrado', icono: '⚖️', nota: 'El de siempre' },
    { id: 'rendimiento', nombre: 'Rendimiento', icono: '🚀', nota: 'Todo a tope' },
    { id: 'ahorro', nombre: 'Ahorro', icono: '🍃', nota: 'Más batería' },
    { id: 'cine', nombre: 'Cine', icono: '🎬', nota: 'No apaga la pantalla' },
  ];
  protected readonly energia = signal<string | null>((() => { try { return localStorage.getItem('mda-energia'); } catch { return null; } })());
  protected readonly avisoEnergia = signal('');
  // «abrir en el PC»: una dirección web o una aplicación de la lista; siempre pide Face ID (o la contraseña)
  protected readonly aplicaciones = [{ id: 'calculadora', nombre: 'Calculadora', icono: '🧮' }, { id: 'bloc', nombre: 'Bloc de notas', icono: '📝' }, { id: 'explorador', nombre: 'Explorador', icono: '🗂️' }];
  protected readonly destino = signal('');
  protected readonly abriendo = signal(false);
  protected readonly avisoAbrir = signal('');
  // «descargar en el PC»: un enlace directo a un archivo; también pide Face ID (o la contraseña)
  protected readonly urlDescarga = signal('');
  protected readonly descargando = signal(false);
  protected readonly avisoDescarga = signal('');
  private readonly confirmador = inject(ConfirmarService);
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
  // días que le quedan a cada disco que se está llenando (lo calcula la API sobre los últimos 30 días)
  private readonly llenado = signal<Record<string, number>>({});
  // una línea por volumen (porcentaje ocupado) y por disco (desgaste), con lo que sube o baja al mes
  protected readonly tendencias = computed<Tendencia[]>(() => {
    const h = this.historial();
    if (h.length < 2) return [];
    const r: Tendencia[] = [];
    const nombres = new Set(h.flatMap((d) => d.volumenes.map((v) => v.nombre)));
    for (const n of nombres) {
      const serie = h.map((d) => { const v = d.volumenes.find((x) => x.nombre === n); return v && v.totalBytes ? { f: d.fecha, v: Math.round(100 - (100 * v.libreBytes) / v.totalBytes) } : null; }).filter((x) => x !== null);
      const t = this.tendencia(`Disco ${n} ocupado`, '%', serie);
      const dias = this.llenado()[n];
      if (t) r.push(dias != null && dias <= 365 ? { ...t, nota: `Se llenará en ~${dias} ${dias === 1 ? 'día' : 'días'} al ritmo de ahora` } : t);
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
    firstValueFrom(this.http.get<{ nombre: string; dias: number }[]>('/api/pc/llenado')).then((l) => this.llenado.set(Object.fromEntries(l.map((x) => [x.nombre, x.dias])))).catch(() => {});
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

  protected async abrirEnPc(ev: Event): Promise<void> {
    ev.preventDefault();
    const d = this.destino().trim();
    if (!d) return;
    await this.pedirAbrir(/^[a-z][a-z0-9+.-]*:/i.test(d) ? d : 'https://' + d, d);
  }

  protected async pedirAbrir(parametro: string, etiqueta: string): Promise<void> {
    this.avisoAbrir.set(''); this.abriendo.set(true);
    try {
      for (let intento = 0; intento < 2; intento++) {
        try {
          await firstValueFrom(this.http.post('/api/pc/ordenes', { tipo: 'abrir', parametro }));
          this.avisoAbrir.set(`Enviado al PC: ${etiqueta}`);
          if (parametro === this.destino().trim() || parametro === 'https://' + this.destino().trim()) this.destino.set('');
          return;
        } catch (err) {
          const x = err as HttpErrorResponse;
          if (x.status === 403 && x.error?.reautenticar && intento === 0) { if (!(await this.confirmador.pedir('Confirma que eres tú para abrir algo en el PC.'))) return; continue; }
          this.avisoAbrir.set(x.status === 409 ? 'El agente de Windows no está en marcha.' : (x.error?.errors?.parametro?.[0] ?? 'No se pudo enviar.'));
          return;
        }
      }
    } finally { this.abriendo.set(false); }
  }

  protected async descargarEnPc(ev: Event): Promise<void> {
    ev.preventDefault();
    const d = this.urlDescarga().trim();
    if (!d) return;
    const parametro = /^[a-z][a-z0-9+.-]*:/i.test(d) ? d : 'https://' + d;
    this.avisoDescarga.set(''); this.descargando.set(true);
    try {
      for (let intento = 0; intento < 2; intento++) {
        try {
          await firstValueFrom(this.http.post('/api/pc/ordenes', { tipo: 'descargar', parametro }));
          this.avisoDescarga.set('Enviado al PC: empieza a descargar. Te aviso por Telegram cuando acabe.');
          this.urlDescarga.set('');
          return;
        } catch (err) {
          const x = err as HttpErrorResponse;
          if (x.status === 403 && x.error?.reautenticar && intento === 0) { if (!(await this.confirmador.pedir('Confirma que eres tú para descargar algo en el PC.'))) return; continue; }
          this.avisoDescarga.set(x.status === 409 ? 'El agente de Windows no está en marcha.' : (x.error?.errors?.parametro?.[0] ?? 'No se pudo enviar.'));
          return;
        }
      }
    } finally { this.descargando.set(false); }
  }

  // plan de energía: orden inocua, no pide confirmar con Face ID
  protected async plan(id: string): Promise<void> {
    this.avisoEnergia.set('');
    try {
      await firstValueFrom(this.http.post('/api/pc/ordenes', { tipo: 'energia', parametro: id }));
      this.energia.set(id);
      try { localStorage.setItem('mda-energia', id); } catch { /* sin almacenamiento */ }
      this.avisoEnergia.set(`Plan «${this.planes.find((p) => p.id === id)?.nombre}» enviado al PC.`);
    } catch (err) {
      this.avisoEnergia.set((err as HttpErrorResponse).status === 409 ? 'El agente de Windows no está en marcha.' : 'No se pudo cambiar el plan.');
    }
  }

  protected usado(v: Volumen): number { return v.totalBytes ? Math.round(100 - (100 * v.libreBytes) / v.totalBytes) : 0; }
  protected gb(b: number): string { return `${(b / 1_073_741_824).toFixed(b < 10_737_418_240 ? 1 : 0)} GB`; }
  protected nombre(o: Orden): string { return o.tipo === 'energia' ? `Energía: ${o.parametro}` : o.tipo === 'abrir' ? `Abrir en el PC: ${o.parametro}` : o.tipo === 'descargar' ? `Descargar en el PC: ${o.parametro}` : ORDENES.find((x) => x.tipo === o.tipo && (x.parametro ?? '') === o.parametro)?.texto ?? o.tipo; }

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
