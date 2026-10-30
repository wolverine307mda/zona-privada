// ECharts por piezas: solo los gráficos y componentes que usa el panel (el paquete completo pesa mucho más)
import { GaugeChart, LineChart } from 'echarts/charts';
import { GridComponent, LegendComponent, TooltipComponent } from 'echarts/components';
import * as echarts from 'echarts/core';
import { CanvasRenderer } from 'echarts/renderers';
import type { Metricas } from './sistema.service';

echarts.use([GaugeChart, LineChart, GridComponent, LegendComponent, TooltipComponent, CanvasRenderer]);

export { echarts };

const tema = () => {
  const cs = getComputedStyle(document.documentElement);
  const v = (n: string, d: string) => cs.getPropertyValue(n).trim() || d;
  return { brand: v('--brand', '#ff7a1f'), texto: v('--on-dark', '#eef2f4'), suave: v('--soft', '#8d979e'), linea: v('--dark-2', '#15232c'), ok: v('--ok', '#3ddc84') };
};

// Medidor de CPU: arco con el color de marca y el número en el centro
export function opcionesCpu(cpu: number): echarts.EChartsCoreOption {
  const t = tema();
  return {
    series: [{
      type: 'gauge', startAngle: 220, endAngle: -40, min: 0, max: 100, radius: '100%', center: ['50%', '58%'],
      progress: { show: true, roundCap: true, width: 16, itemStyle: { color: t.brand } },
      axisLine: { roundCap: true, lineStyle: { width: 16, color: [[1, t.linea]] } },
      axisTick: { show: false }, splitLine: { show: false }, axisLabel: { show: false },
      pointer: { show: false }, anchor: { show: false }, title: { show: false },
      detail: { valueAnimation: true, offsetCenter: [0, '6%'], formatter: '{value}%', color: t.texto, fontSize: 34, fontFamily: 'Panchang, Arial Black, sans-serif' },
      data: [{ value: Math.round(cpu) }],
      animationDurationUpdate: 600,
    }],
  };
}

// Línea de los últimos minutos: CPU y memoria en %
export function opcionesHistorial(h: Metricas[]): echarts.EChartsCoreOption {
  const t = tema();
  const horas = h.map((m) => new Date(m.hora).toLocaleTimeString('es-ES', { hour: '2-digit', minute: '2-digit', second: '2-digit' }));
  const serie = (name: string, color: string, data: number[]) => ({
    name, type: 'line', data, smooth: true, showSymbol: false, lineStyle: { width: 2.5, color },
    areaStyle: { color: new echarts.graphic.LinearGradient(0, 0, 0, 1, [{ offset: 0, color: color + '55' }, { offset: 1, color: color + '00' }]) },
  });
  return {
    animationDuration: 300,
    grid: { left: 36, right: 12, top: 36, bottom: 28 },
    legend: { top: 0, right: 0, textStyle: { color: t.suave, fontFamily: 'JetBrains Mono, monospace' }, itemWidth: 14 },
    tooltip: { trigger: 'axis', valueFormatter: (v: unknown) => `${v} %` },
    xAxis: { type: 'category', data: horas, boundaryGap: false, axisLabel: { color: t.suave, hideOverlap: true }, axisLine: { lineStyle: { color: t.linea } } },
    yAxis: { type: 'value', min: 0, max: 100, axisLabel: { color: t.suave, formatter: '{value}' }, splitLine: { lineStyle: { color: t.linea } } },
    series: [
      serie('cpu', t.brand, h.map((m) => m.cpu)),
      serie('memoria', t.ok, h.map((m) => Math.round((100 * m.memoriaUsada) / Math.max(1, m.memoriaTotal)))),
    ],
  };
}
