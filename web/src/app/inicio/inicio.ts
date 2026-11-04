import { Linea } from './linea';
import { SemaforoVida } from './semaforo';
import { HttpClient } from '@angular/common/http';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { GastosService } from '../gastos/gastos.service';
import { Panel } from './panel';
import { Bateria } from './bateria';
import { Espacio } from './espacio';
import { DatosPc, Pc3d } from './pc-3d';
import { Component, ElementRef, OnDestroy, afterNextRender, computed, effect, inject, signal, viewChild } from '@angular/core';
import { PerfilService } from '../core/perfil.service';
import { SesionService } from '../core/sesion.service';
import { TemaService } from '../core/tema.service';
import { echarts, opcionesCpu, opcionesHistorial } from './graficas';
import { SistemaService } from './sistema.service';

const GB = 1024 ** 3;

// bytes/s legibles: 812 B/s, 34,2 KB/s, 4,1 MB/s
export function velocidad(b: number): string {
  if (b < 1024) return `${Math.round(b)} B/s`;
  if (b < 1024 ** 2) return `${(b / 1024).toLocaleString('es-ES', { maximumFractionDigits: 1 })} KB/s`;
  return `${(b / 1024 ** 2).toLocaleString('es-ES', { maximumFractionDigits: 1 })} MB/s`;
}

// Inicio: el saludo con accesos rápidos, el panel con todas las secciones y el equipo de casa en directo
@Component({
  selector: 'app-inicio',
  imports: [Bateria, Espacio, Linea, Panel, RouterLink, Pc3d, SemaforoVida],
  templateUrl: './inicio.html',
  styleUrl: './inicio.css',
})
export class Inicio implements OnDestroy {
  private readonly sesion = inject(SesionService);
  protected readonly perfil = inject(PerfilService);
  private readonly tema = inject(TemaService);
  protected readonly sistema = inject(SistemaService);

  private readonly cpuEl = viewChild.required<ElementRef<HTMLDivElement>>('cpu');
  private readonly historialEl = viewChild.required<ElementRef<HTMLDivElement>>('historial');
  private graficaCpu?: echarts.ECharts;
  private graficaHistorial?: echarts.ECharts;
  private observador?: ResizeObserver;

  protected readonly ahora = signal(new Date());
  protected readonly a = computed(() => this.sistema.actual());
  protected readonly nombre = computed(() => {
    // el nombre de pila del perfil («Mario»); si aún no lo hay, el usuario
    const n = this.perfil.datos()?.nombre?.trim().split(' ')[0];
    if (n) return n;
    const u = this.sesion.usuario() ?? '';
    return u ? u[0].toUpperCase() + u.slice(1) : '';
  });
  protected readonly saludo = computed(() => {
    const h = this.ahora().getHours();
    return h < 7 ? 'Buenas noches' : h < 14 ? 'Buenos días' : h < 21 ? 'Buenas tardes' : 'Buenas noches';
  });
  protected readonly real = computed(() => this.a()?.origen === 'equipo');
  protected readonly datosPc = computed<DatosPc>(() => ({ cpu: this.a()?.cpu ?? 0, memoria: this.memoria().pct, disco: this.disco().pct, real: this.real() }));

  // la temperatura más alta de los discos (la manda el agente con la salud del equipo); sin dato, no se enseña
  protected readonly temperatura = signal<number | null>(null);
  protected readonly temperaturaCpu = signal<number | null>(null);
  private readonly http = inject(HttpClient);
  protected readonly discosUso = computed(() => {
    const m = this.a();
    if (m?.discos?.length) return m.discos;
    return m?.discoTotal ? [{ nombre: 'Sistema', usado: m.discoUsado, total: m.discoTotal }] : [];
  });

  private async leerTemperatura(): Promise<void> {
    try {
      const r = await firstValueFrom(this.http.get<{ salud: { discos?: { temperaturaC: number | null }[]; cpuTemperaturaC?: number | null } | null }>('/api/pc'));
      this.temperaturaCpu.set(typeof r.salud?.cpuTemperaturaC === 'number' ? Math.round(r.salud.cpuTemperaturaC) : null);
      const t = (r.salud?.discos ?? []).map((d) => d.temperaturaC).filter((x): x is number => typeof x === 'number');
      this.temperatura.set(t.length ? Math.round(Math.max(...t)) : null);
    } catch { /* se queda como estaba */ }
  }

  protected readonly memoria = computed(() => this.porcentaje(this.a()?.memoriaUsada, this.a()?.memoriaTotal));
  protected readonly disco = computed(() => this.porcentaje(this.a()?.discoUsado, this.a()?.discoTotal));
  protected readonly discos = computed(() => (this.a()?.discos ?? []).map((d) => ({ nombre: d.nombre.replace(/\\$/, ''), ...this.porcentaje(d.usado, d.total) })));
  protected readonly bajada = computed(() => velocidad(this.a()?.redBajada ?? 0));
  protected readonly subida = computed(() => velocidad(this.a()?.redSubida ?? 0));
  protected readonly encendido = computed(() => {
    const s = this.a()?.encendidoSegundos ?? 0;
    const d = Math.floor(s / 86400), h = Math.floor((s % 86400) / 3600), m = Math.floor((s % 3600) / 60);
    return d ? `${d} d ${h} h` : h ? `${h} h ${m} min` : `${m} min`;
  });
  protected readonly so = computed(() => (this.a()?.sistemaOperativo ?? '').replace('Microsoft ', ''));

  constructor() {
    const reloj = setInterval(() => this.ahora.set(new Date()), 1000);
    const temp = setInterval(() => { if (!document.hidden) void this.leerTemperatura(); }, 60_000);
    void this.leerTemperatura();
    this.limpiar = () => { clearInterval(reloj); clearInterval(temp); };

    afterNextRender(() => {
      this.graficaCpu = echarts.init(this.cpuEl().nativeElement);
      this.graficaHistorial = echarts.init(this.historialEl().nativeElement);
      this.observador = new ResizeObserver(() => { this.graficaCpu?.resize(); this.graficaHistorial?.resize(); });
      this.observador.observe(this.historialEl().nativeElement);
      this.observador.observe(this.cpuEl().nativeElement);
      this.pintar();
    });

    // cada lectura nueva y cada cambio de tema repintan las gráficas
    effect(() => {
      this.sistema.historial();
      this.tema.cambio();
      this.pintar();
    });
  }

  private limpiar: () => void;
  private readonly gastos = inject(GastosService);
  private readonly router = inject(Router);

  protected nuevoGasto(): void {
    this.gastos.editandoMovimiento.set({ tipo: 'Gasto' });
    this.router.navigateByUrl('/gastos');
  }

  private pintar(): void {
    this.graficaCpu?.setOption(opcionesCpu(this.a()?.cpu ?? 0));
    this.graficaHistorial?.setOption(opcionesHistorial(this.sistema.historial()));
  }

  private porcentaje(usado?: number, total?: number) {
    if (!usado || !total) return { pct: 0, usado: '—', total: '—' };
    const f = (b: number) => (b / GB).toLocaleString('es-ES', { maximumFractionDigits: b / GB >= 100 ? 0 : 1 });
    return { pct: Math.round((100 * usado) / total), usado: f(usado), total: f(total) };
  }

  ngOnDestroy(): void {
    this.limpiar();
    this.observador?.disconnect();
    this.graficaCpu?.dispose();
    this.graficaHistorial?.dispose();
  }
}
