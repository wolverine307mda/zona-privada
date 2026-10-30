import { Component, ElementRef, OnDestroy, afterNextRender, computed, effect, inject, signal, viewChild } from '@angular/core';
import { Router } from '@angular/router';
import { SesionService } from '../core/sesion.service';
import { echarts, opcionesCpu, opcionesHistorial } from './graficas';
import { SistemaService } from './sistema.service';

const GB = 1024 ** 3;

@Component({
  selector: 'app-panel',
  templateUrl: './panel.html',
  styleUrl: './panel.css',
})
export class Panel implements OnDestroy {
  private readonly sesion = inject(SesionService);
  private readonly router = inject(Router);
  protected readonly sistema = inject(SistemaService);

  private readonly cpuEl = viewChild.required<ElementRef<HTMLDivElement>>('cpu');
  private readonly historialEl = viewChild.required<ElementRef<HTMLDivElement>>('historial');
  private graficaCpu?: echarts.ECharts;
  private graficaHistorial?: echarts.ECharts;
  private observador?: ResizeObserver;
  private reloj?: ReturnType<typeof setInterval>;

  protected readonly ahora = signal(new Date());
  protected readonly nombre = computed(() => {
    const e = this.sesion.usuario() ?? '';
    const n = e.split('@')[0].split(/[._-]/)[0];
    return n ? n[0].toUpperCase() + n.slice(1) : '';
  });
  protected readonly saludo = computed(() => {
    const h = this.ahora().getHours();
    return h < 7 ? 'Buenas noches' : h < 14 ? 'Buenos días' : h < 21 ? 'Buenas tardes' : 'Buenas noches';
  });

  protected readonly memoria = computed(() => this.porcentaje(this.sistema.actual()?.memoriaUsada, this.sistema.actual()?.memoriaTotal));
  protected readonly disco = computed(() => this.porcentaje(this.sistema.actual()?.discoUsado, this.sistema.actual()?.discoTotal));
  protected readonly encendido = computed(() => {
    const s = this.sistema.actual()?.encendidoSegundos ?? 0;
    const d = Math.floor(s / 86400), h = Math.floor((s % 86400) / 3600), m = Math.floor((s % 3600) / 60);
    return d ? `${d} d ${h} h` : h ? `${h} h ${m} min` : `${m} min`;
  });

  // lo que viene: los siguientes módulos de la zona privada
  protected readonly proximos = [
    { codigo: 'fotos', titulo: 'Laboratorio de fotos', texto: 'Subir, convertir a WebP y publicar en la galería.' },
    { codigo: 'música', titulo: 'Lo que suena', texto: 'Estadísticas de Spotify con la clave a salvo en el servidor.' },
    { codigo: 'proyectos', titulo: 'Mis proyectos', texto: 'Repos de GitHub, despliegues y un tablero de ideas.' },
  ];

  constructor() {
    this.sistema.empezar();
    this.reloj = setInterval(() => this.ahora.set(new Date()), 30_000);

    afterNextRender(() => {
      this.graficaCpu = echarts.init(this.cpuEl().nativeElement, undefined, { renderer: 'canvas' });
      this.graficaHistorial = echarts.init(this.historialEl().nativeElement, undefined, { renderer: 'canvas' });
      this.observador = new ResizeObserver(() => { this.graficaCpu?.resize(); this.graficaHistorial?.resize(); });
      this.observador.observe(this.historialEl().nativeElement);
      this.observador.observe(this.cpuEl().nativeElement);
      this.pintar();
    });

    // cada lectura nueva repinta las dos gráficas
    effect(() => {
      this.sistema.historial();
      this.pintar();
    });
  }

  private pintar(): void {
    const actual = this.sistema.actual();
    this.graficaCpu?.setOption(opcionesCpu(actual?.cpu ?? 0));
    this.graficaHistorial?.setOption(opcionesHistorial(this.sistema.historial()));
  }

  private porcentaje(usado?: number, total?: number) {
    if (!usado || !total) return { pct: 0, usado: '—', total: '—' };
    return { pct: Math.round((100 * usado) / total), usado: (usado / GB).toFixed(1), total: (total / GB).toFixed(1) };
  }

  protected async salir(): Promise<void> {
    await this.sistema.parar();
    await this.sesion.salir();
    this.router.navigateByUrl('/entrar');
  }

  ngOnDestroy(): void {
    clearInterval(this.reloj);
    this.observador?.disconnect();
    this.graficaCpu?.dispose();
    this.graficaHistorial?.dispose();
    this.sistema.parar();
  }
}
