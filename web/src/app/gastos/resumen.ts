import { Component, ElementRef, OnDestroy, afterNextRender, computed, effect, inject, signal, untracked, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TemaService } from '../core/tema.service';
import { GastosService } from './gastos.service';
import { echarts, opcionesRosco } from './graficas-gastos';
import { GraficaPrevision } from './grafica-prevision';
import { CATEGORIAS, Consejos, Movimiento, Pendiente, EstadoCobro, PrevisionCierre, Presupuesto, PresupuestoSemana, ResumenMes, Saldo, categoria, diaCorto, euros, eurosCorto } from './modelo';
import { EstadoError, Esqueleto } from '../ui';

// Resumen del mes: el dinero que tienes (saldo que se arrastra de mes en mes), lo que puedes gastar al día,
// consejos para ahorrar, presupuestos, lo que se repite y falta por aplicar, en qué se va el dinero,
// el calendario de gasto por día y los últimos movimientos.
@Component({
  selector: 'app-resumen',
  imports: [RouterLink, EstadoError, Esqueleto, GraficaPrevision],
  templateUrl: './resumen.html',
  styleUrls: ['./comun.css', './dialogos.css', './resumen.css'],
})
export class Resumen implements OnDestroy {
  protected readonly g = inject(GastosService);
  private readonly tema = inject(TemaService);
  private readonly roscoEl = viewChild<ElementRef<HTMLDivElement>>('rosco');
  private readonly dlgSaldo = viewChild<ElementRef<HTMLDialogElement>>('dlgSaldo');
  private readonly dlgAplicar = viewChild<ElementRef<HTMLDialogElement>>('dlgAplicar');
  private readonly dlgPresupuestos = viewChild<ElementRef<HTMLDialogElement>>('dlgPresupuestos');
  private readonly dlgCobro = viewChild<ElementRef<HTMLDialogElement>>('dlgCobro');
  private rosco?: echarts.ECharts;
  private observador?: ResizeObserver;

  protected readonly datos = signal<ResumenMes | null>(null);
  protected readonly error = signal(false);
  protected readonly apuntando = signal<number | null>(null);
  protected readonly saldo = signal<Saldo | null>(null);
  protected readonly consejos = signal<Consejos | null>(null);
  protected readonly prevision = signal<PrevisionCierre | null>(null);
  protected informe(): string { return `/api/gastos/informe?anio=${this.g.anio()}&mes=${this.g.mes()}`; }
  protected readonly presupuestos = signal<Presupuesto[]>([]);
  // presupuestos de la semana de hoy; `vistaPresu` elige cuál se ve en la tarjeta y `modoDlg` cuál se edita en el diálogo
  protected readonly semanales = signal<PresupuestoSemana[]>([]);
  protected readonly cobro = signal<EstadoCobro | null>(null);
  protected readonly diaCobro = signal('');
  protected readonly vistaPresu = signal<'mes' | 'semana'>('mes');
  protected readonly modoDlg = signal<'mes' | 'semana'>('mes');
  protected readonly limitesSemana = signal<Record<string, string>>({});
  protected readonly arrastrar = signal(false);
  // diálogos
  protected readonly saldoTexto = signal('');
  protected readonly aplicando = signal<Pendiente | null>(null);
  protected readonly importeAplicar = signal('');
  protected readonly cambiarImporte = signal(false);
  protected readonly limites = signal<Record<string, string>>({});
  protected readonly errorDlg = signal('');
  protected readonly categoriasGasto = CATEGORIAS.filter((c) => c.tipo === 'Gasto');

  protected readonly euros = euros;
  protected readonly eurosCorto = eurosCorto;
  protected readonly categoria = categoria;
  protected readonly diaCorto = diaCorto;

  // variación frente al mes anterior (en %); null si no hay con qué comparar
  protected variacion(actual: number, anterior: number): number | null {
    return anterior > 0 ? Math.round(((actual - anterior) / anterior) * 100) : null;
  }

  protected readonly tasa = computed(() => Math.max(0, Math.min(100, this.datos()?.tasaAhorro ?? 0)));
  // % de lo ingresado que se ha gastado (100 si hay gastos sin ingresos)
  protected readonly gastado = computed(() => {
    const d = this.datos();
    if (!d) return 0;
    return d.ingresos > 0 ? Math.round((100 * d.gastos) / d.ingresos) : d.gastos > 0 ? 100 : 0;
  });
  protected readonly reparto = computed(() => {
    const d = this.datos();
    if (!d) return [];
    return d.porCategoria.map((c) => ({ ...c, cat: categoria(c.categoria), pct: d.gastos > 0 ? Math.round((c.total / d.gastos) * 100) : 0 }));
  });

  // calendario: huecos hasta el primer lunes y la intensidad de cada día respecto al día de más gasto
  protected readonly calendario = computed(() => {
    const d = this.datos();
    if (!d) return { huecos: [], dias: [] };
    const primero = new Date(d.anio, d.mes - 1, 1).getDay();
    const max = Math.max(1, ...d.gastoPorDia);
    const hoy = new Date();
    return {
      huecos: Array.from({ length: (primero + 6) % 7 }),
      dias: d.gastoPorDia.map((v, i) => ({
        n: i + 1, v, nivel: v > 0 ? 0.18 + 0.82 * (v / max) : 0,
        hoy: hoy.getFullYear() === d.anio && hoy.getMonth() + 1 === d.mes && hoy.getDate() === i + 1,
      })),
    };
  });
  protected readonly ultimos = computed(() => (this.datos()?.movimientos ?? []).slice(0, 7));
  protected readonly esActual = computed(() => { this.g.version(); return this.g.esMesActual(); });
  // regla 50/30/20: % de lo ingresado en necesidades, caprichos y ahorro
  protected readonly regla = computed(() => {
    const r = this.consejos()?.regla;
    if (!r || r.ingresos <= 0) return null;
    const pct = (v: number) => Math.max(0, Math.round((v / r.ingresos) * 100));
    return { necesidades: pct(r.necesidades), caprichos: pct(r.caprichos), ahorro: pct(r.ahorro), negativo: r.ahorro < 0 };
  });

  constructor() {
    effect(() => {
      const anio = this.g.anio(), mes = this.g.mes();
      this.g.version();
      untracked(() => this.cargar(anio, mes));
    });
    effect(() => {
      this.tema.cambio();
      this.datos();
      // el gráfico está dentro de @if: se espera a que Angular lo ponga en la página
      untracked(() => setTimeout(() => this.pintar()));
    });
    afterNextRender(() => this.pintar());
  }

  private async cargar(anio: number, mes: number): Promise<void> {
    try {
      const [d, s, p] = await Promise.all([this.g.resumen(anio, mes), this.g.saldo(anio, mes), this.g.presupuestos(anio, mes)]);
      this.error.set(false);
      this.datos.set(d);
      this.saldo.set(s);
      this.presupuestos.set(p);
      if (this.g.esMesActual()) this.g.presupuestosSemana().then((s) => { this.semanales.set(s.presupuestos); if (!s.presupuestos.length) this.vistaPresu.set('mes'); }).catch(() => this.semanales.set([]));
      else { this.semanales.set([]); this.vistaPresu.set('mes'); }
      if (this.g.esMesActual()) this.g.cobro().then((c) => this.cobro.set(c)).catch(() => this.cobro.set(null)); else this.cobro.set(null);
      if (this.g.esMesActual()) { this.g.consejos().then((c) => this.consejos.set(c)).catch(() => {}); this.g.prevision().then((p) => this.prevision.set(p)).catch(() => {}); }
      else { this.consejos.set(null); this.prevision.set(null); }
    } catch {
      this.error.set(true);
    }
  }

  private pintar(): void {
    const el = this.roscoEl()?.nativeElement;
    const d = this.datos();
    if (!el || !d) return;
    if (!this.rosco) {
      this.rosco = echarts.init(el);
      this.observador = new ResizeObserver(() => this.rosco?.resize());
      this.observador.observe(el);
    }
    this.rosco.setOption(opcionesRosco(d.porCategoria, d.gastos), true);
  }

  private num(s: string): number | null {
    const n = Number(String(s).replace(/\s/g, '').replace(',', '.'));
    return s.trim() && Number.isFinite(n) ? n : null;
  }
  protected valor(e: Event): string { return (e.target as HTMLInputElement).value; }

  private hoyISO(): string {
    const h = new Date();
    return `${h.getFullYear()}-${String(h.getMonth() + 1).padStart(2, '0')}-${String(h.getDate()).padStart(2, '0')}`;
  }
  protected esFutura(p: Pendiente): boolean { return p.fecha > this.hoyISO(); }

  // «Aplicar»: lo que se repite pasa a ser un apunte del mes. Si su importe puede variar, primero se pregunta.
  protected async aplicar(p: Pendiente): Promise<void> {
    if (p.variable) {
      this.aplicando.set(p);
      this.importeAplicar.set(String(p.importe).replace('.', ','));
      this.cambiarImporte.set(false);
      this.errorDlg.set('');
      this.dlgAplicar()?.nativeElement.showModal();
      return;
    }
    await this.registrar(p);
  }

  protected async confirmarAplicar(e: Event): Promise<void> {
    e.preventDefault();
    const p = this.aplicando();
    if (!p) return;
    const importe = this.cambiarImporte() ? this.num(this.importeAplicar()) : p.importe;
    if (!importe || importe <= 0) { this.errorDlg.set('Pon un importe mayor que 0.'); return; }
    if (await this.registrar(p, importe)) this.dlgAplicar()?.nativeElement.close();
  }

  private async registrar(p: Pendiente, importe?: number): Promise<boolean> {
    this.apuntando.set(p.recurrenteId);
    try {
      // si aún no ha llegado su día, se apunta hoy (cuando te la cobraron) y cubre la ocurrencia de su mes
      await this.g.registrarFijo(p.recurrenteId, this.esFutura(p) ? this.hoyISO() : p.fecha, importe, p.fecha.slice(0, 7));
      this.g.recargar();
      return true;
    } catch {
      this.errorDlg.set('No se pudo aplicar.');
      return false;
    } finally {
      this.apuntando.set(null);
    }
  }

  // «¿Cuánto tienes ahora?»: el saldo de hoy; a partir de ahí lo mueven los apuntes
  protected abrirSaldo(): void {
    const s = this.saldo();
    this.saldoTexto.set(s?.configurado ? String(s.actual).replace('.', ',') : '');
    this.errorDlg.set('');
    this.dlgSaldo()?.nativeElement.showModal();
  }

  protected async guardarSaldo(e: Event): Promise<void> {
    e.preventDefault();
    const v = this.num(this.saldoTexto().replace(/€/g, ''));
    if (v == null) { this.errorDlg.set('Escribe una cantidad (puede ser negativa).'); return; }
    try {
      await this.g.ponerSaldo(v);
      this.dlgSaldo()?.nativeElement.close();
      this.g.recargar();
    } catch { this.errorDlg.set('No se pudo guardar.'); }
  }

  protected abrirPresupuestos(): void {
    this.limites.set(Object.fromEntries(this.presupuestos().map((p) => [p.categoria, String(p.limite).replace('.', ',')])));
    this.limitesSemana.set(Object.fromEntries(this.semanales().map((p) => [p.categoria, String(p.limite).replace('.', ',')])));
    this.arrastrar.set(this.semanales().some((p) => p.arrastrar));
    this.modoDlg.set(this.esActual() ? this.vistaPresu() : 'mes');
    this.errorDlg.set('');
    this.dlgPresupuestos()?.nativeElement.showModal();
  }

  protected ponerLimite(cat: string, e: Event): void { this.limites.update((l) => ({ ...l, [cat]: this.valor(e) })); }
  protected ponerLimiteSemana(cat: string, e: Event): void { this.limitesSemana.update((l) => ({ ...l, [cat]: this.valor(e) })); }

  protected async guardarPresupuestos(e: Event): Promise<void> {
    e.preventDefault();
    if (this.modoDlg() === 'semana') { await this.guardarSemanales(); return; }
    const antes = new Map(this.presupuestos().map((p) => [p.categoria, p.limite]));
    try {
      for (const c of this.categoriasGasto) {
        const v = this.num(this.limites()[c.id] ?? '');
        if (v && v > 0 && v !== antes.get(c.id)) await this.g.ponerPresupuesto(c.id, v);
        else if (!v && antes.has(c.id)) await this.g.quitarPresupuesto(c.id);
      }
      this.dlgPresupuestos()?.nativeElement.close();
      this.g.recargar();
    } catch { this.errorDlg.set('Algún límite no es válido.'); }
  }

  private async guardarSemanales(): Promise<void> {
    const antes = new Map(this.semanales().map((p) => [p.categoria, p]));
    try {
      for (const c of this.categoriasGasto) {
        const v = this.num(this.limitesSemana()[c.id] ?? ''), previo = antes.get(c.id);
        if (v && v > 0 && (v !== previo?.limite || this.arrastrar() !== previo?.arrastrar)) await this.g.ponerPresupuestoSemana(c.id, v, this.arrastrar());
        else if (!v && previo) await this.g.quitarPresupuestoSemana(c.id);
      }
      this.vistaPresu.set('semana');
      this.dlgPresupuestos()?.nativeElement.close();
      this.g.recargar();
    } catch { this.errorDlg.set('Algún límite no es válido.'); }
  }

  protected abrirCobro(): void {
    this.diaCobro.set(this.cobro()?.fuente === 'manual' ? String(this.cobro()!.dia) : '');
    this.errorDlg.set('');
    this.dlgCobro()?.nativeElement.showModal();
  }

  protected async guardarCobro(e: Event): Promise<void> {
    e.preventDefault();
    const texto = this.diaCobro().trim(), n = texto ? Number(texto) : null;
    if (n !== null && (!Number.isInteger(n) || n < 1 || n > 31)) { this.errorDlg.set('Pon un día entre 1 y 31, o déjalo en blanco para que lo detecte solo.'); return; }
    try {
      this.cobro.set(await this.g.ponerCobro(n));
      this.dlgCobro()?.nativeElement.close();
    } catch { this.errorDlg.set('No se pudo guardar.'); }
  }

  protected diasTexto(n: number): string { return `${n} ${n === 1 ? 'día' : 'días'}`; }
  protected fechaCorta(iso: string): string { return new Date(iso + 'T12:00').toLocaleDateString('es-ES', { weekday: 'long', day: 'numeric', month: 'short' }); }

  protected cerrarFuera(e: MouseEvent): void { const d = e.target as HTMLElement; if (d.tagName === 'DIALOG') (d as HTMLDialogElement).close(); }
  protected iconoConsejo(i: string): string {
    return ({
      ritmo: 'M12 8v4l3 2M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18z',
      alerta: 'M12 3l10 18H2zM12 10v4M12 17h.01',
      sube: 'M3 17l6-6 4 4 8-8M15 7h6v6',
      suscripciones: 'M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2zM10 8l6 4-6 4z',
      hucha: 'M19 10c0-3.3-3.1-6-7-6S5 6.7 5 10c0 2 1 3.7 2.6 4.8L7 19h3l.5-2h3l.5 2h3l-.6-4.2C18 13.7 19 12 19 10zM15 9h.01M2 10h3',
      trofeo: 'M8 21h8M12 17v4M7 4h10v5a5 5 0 0 1-10 0zM17 5h3a3 3 0 0 1-3 4M7 5H4a3 3 0 0 0 3 4',
      colchon: 'M12 3l8 3v6c0 5-3.5 8-8 9-4.5-1-8-4-8-9V6z',
    } as Record<string, string>)[i] ?? 'M12 3v18M3 12h18';
  }

  protected editar(m: Movimiento): void { this.g.editandoMovimiento.set({ movimiento: m }); }

  ngOnDestroy(): void {
    this.observador?.disconnect();
    this.rosco?.dispose();
  }
}
