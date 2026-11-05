// Gráficas del módulo de gastos (ECharts por piezas: se suman tarta y barras a lo que ya registra el inicio)
import { BarChart, PieChart } from 'echarts/charts';
import { TitleComponent } from 'echarts/components';
import { echarts } from '../inicio/graficas';
import { PuntoMes, Repostaje, TotalCategoria, categoria, euros, mesCorto } from './modelo';
import { resolverColor } from '../core/color-css';

echarts.use([PieChart, BarChart, TitleComponent]); // el título es el total en el centro del rosco
export { echarts };

const tema = () => {
  const cs = getComputedStyle(document.documentElement);
  const v = (n: string, d: string) => resolverColor(cs.getPropertyValue(n), d);
  return { brand: v('--brand', '#ff7a1f'), texto: v('--texto', '#e8edf0'), suave: v('--soft', '#8d979e'), linea: v('--linea', '#1c2a34'), fondo: v('--tarjeta', '#0f1920'), fondo2: v('--tarjeta-2', '#15222b'), ok: v('--ok', '#3ddc84'), bad: v('--bad', '#ff5a5f') };
};
const tooltip = (t: ReturnType<typeof tema>) => ({ backgroundColor: t.fondo2, borderColor: t.linea, textStyle: { color: t.texto, fontFamily: 'Switzer, sans-serif' } });

// Rosco de gastos por categoría, con el total en el centro
export function opcionesRosco(cats: TotalCategoria[], total: number): echarts.EChartsCoreOption {
  const t = tema();
  return {
    tooltip: { trigger: 'item', ...tooltip(t), formatter: (p: { name: string; value: number; percent: number }) => `${p.name}<br><b>${euros(p.value)}</b> · ${p.percent}%` },
    title: {
      text: euros(total), subtext: 'gastado', left: 'center', top: '40%',
      textStyle: { color: t.texto, fontFamily: 'Panchang, Arial Black, sans-serif', fontSize: 18 },
      subtextStyle: { color: t.suave, fontSize: 12 },
    },
    series: [{
      type: 'pie', radius: ['62%', '86%'], center: ['50%', '50%'], padAngle: 2, itemStyle: { borderRadius: 6 },
      label: { show: false }, emphasis: { scale: true, scaleSize: 6 },
      data: cats.length ? cats.map((c) => ({ name: categoria(c.categoria).nombre, value: c.total, itemStyle: { color: categoria(c.categoria).color } }))
        : [{ name: 'Sin gastos', value: 1, itemStyle: { color: t.fondo2 } }],
    }],
  };
}

// Ingresos y gastos por mes (barras) con el ahorro acumulado (línea)
export function opcionesEvolucion(serie: PuntoMes[]): echarts.EChartsCoreOption {
  const t = tema();
  return {
    animationDuration: 500,
    grid: { left: 54, right: 54, top: 40, bottom: 30 },
    legend: { top: 0, right: 0, textStyle: { color: t.suave }, itemWidth: 12, itemHeight: 12 },
    tooltip: { trigger: 'axis', ...tooltip(t), valueFormatter: (v: unknown) => euros(Number(v)) },
    xAxis: { type: 'category', data: serie.map((p) => mesCorto(p.anio, p.mes)), axisLabel: { color: t.suave }, axisLine: { lineStyle: { color: t.linea } }, axisTick: { show: false } },
    yAxis: [
      { type: 'value', axisLabel: { color: t.suave, formatter: (v: number) => `${v / 1000 >= 1 || v / 1000 <= -1 ? v / 1000 + 'k' : v} €` }, splitLine: { lineStyle: { color: t.linea } } },
      { type: 'value', axisLabel: { color: t.suave, formatter: (v: number) => `${v / 1000 >= 1 || v / 1000 <= -1 ? v / 1000 + 'k' : v} €` }, splitLine: { show: false } },
    ],
    series: [
      { name: 'Ingresos', type: 'bar', data: serie.map((p) => p.ingresos), itemStyle: { color: t.ok, borderRadius: [6, 6, 0, 0] }, barMaxWidth: 22 },
      { name: 'Gastos', type: 'bar', data: serie.map((p) => p.gastos), itemStyle: { color: t.brand, borderRadius: [6, 6, 0, 0] }, barMaxWidth: 22 },
      {
        name: 'Ahorro acumulado', type: 'line', yAxisIndex: 1, data: serie.map((p) => p.ahorroAcumulado), smooth: true, symbolSize: 7,
        lineStyle: { width: 3, color: t.texto }, itemStyle: { color: t.texto },
      },
    ],
  };
}

// Precio del litro en cada repostaje y los litros echados
export function opcionesGasolina(repostajes: Repostaje[]): echarts.EChartsCoreOption {
  const t = tema();
  const r = [...repostajes].reverse();
  return {
    animationDuration: 500,
    grid: { left: 54, right: 54, top: 40, bottom: 30 },
    legend: { top: 0, right: 0, textStyle: { color: t.suave }, itemWidth: 12, itemHeight: 12 },
    tooltip: { trigger: 'axis', ...tooltip(t) },
    xAxis: { type: 'category', data: r.map((x) => new Date(`${x.fecha}T12:00:00`).toLocaleDateString('es-ES', { day: 'numeric', month: 'short' })), axisLabel: { color: t.suave }, axisLine: { lineStyle: { color: t.linea } }, axisTick: { show: false } },
    yAxis: [
      { type: 'value', name: 'L', nameTextStyle: { color: t.suave }, axisLabel: { color: t.suave }, splitLine: { lineStyle: { color: t.linea } } },
      { type: 'value', name: '€/L', nameTextStyle: { color: t.suave }, scale: true, axisLabel: { color: t.suave, formatter: (v: number) => v.toFixed(2) }, splitLine: { show: false } },
    ],
    series: [
      { name: 'Litros', type: 'bar', data: r.map((x) => x.litros), itemStyle: { color: t.fondo2, borderColor: t.linea, borderRadius: [6, 6, 0, 0] }, barMaxWidth: 26, tooltip: { valueFormatter: (v: unknown) => `${Number(v).toLocaleString('es-ES')} L` } },
      {
        name: 'Precio por litro', type: 'line', yAxisIndex: 1, data: r.map((x) => x.precioLitro), smooth: true, symbolSize: 8,
        lineStyle: { width: 3, color: t.brand }, itemStyle: { color: t.brand }, tooltip: { valueFormatter: (v: unknown) => `${Number(v).toLocaleString('es-ES', { minimumFractionDigits: 3 })} €/L` },
        areaStyle: { color: new echarts.graphic.LinearGradient(0, 0, 0, 1, [{ offset: 0, color: t.brand + '44' }, { offset: 1, color: t.brand + '00' }]) },
      },
    ],
  };
}
