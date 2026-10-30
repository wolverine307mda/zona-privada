import { Component, ElementRef, OnDestroy, afterNextRender, computed, effect, inject, signal, untracked, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TemaService } from '../core/tema.service';
import { GastosService } from './gastos.service';
import { echarts, opcionesRosco } from './graficas-gastos';
import { Movimiento, Pendiente, ResumenMes, categoria, diaCorto, euros, eurosCorto } from './modelo';

// Resumen del mes: balance y ahorro, fijos pendientes de apuntar, en qué se va el dinero,
// el calendario de gasto por día y los últimos movimientos.
@Component({
  selector: 'app-resumen',
  imports: [RouterLink],
  templateUrl: './resumen.html',
  styleUrls: ['./comun.css', './resumen.css'],
})
export class Resumen implements OnDestroy {
  protected readonly g = inject(GastosService);
  private readonly tema = inject(TemaService);
  private readonly roscoEl = viewChild<ElementRef<HTMLDivElement>>('rosco');
  private rosco?: echarts.ECharts;
  private observador?: ResizeObserver;

  protected readonly datos = signal<ResumenMes | null>(null);
  protected readonly error = signal(false);
  protected readonly importes = signal<Record<number, string>>({});
  protected readonly apuntando = signal<number | null>(null);

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
      const d = await this.g.resumen(anio, mes);
      this.error.set(false);
      this.datos.set(d);
      this.importes.set(Object.fromEntries(d.pendientes.map((p) => [p.recurrenteId, String(p.importe).replace('.', ',')])));
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

  protected importeDe(p: Pendiente): string { return this.importes()[p.recurrenteId] ?? String(p.importe).replace('.', ','); }
  protected ponerImporte(p: Pendiente, e: Event): void {
    this.importes.update((v) => ({ ...v, [p.recurrenteId]: (e.target as HTMLInputElement).value }));
  }

  protected async apuntar(p: Pendiente): Promise<void> {
    this.apuntando.set(p.recurrenteId);
    try {
      const importe = Number(this.importeDe(p).replace(',', '.'));
      await this.g.registrarFijo(p.recurrenteId, p.fecha, importe > 0 ? importe : undefined);
      this.g.recargar();
    } finally {
      this.apuntando.set(null);
    }
  }

  protected editar(m: Movimiento): void { this.g.editandoMovimiento.set({ movimiento: m }); }

  ngOnDestroy(): void {
    this.observador?.disconnect();
    this.rosco?.dispose();
  }
}
