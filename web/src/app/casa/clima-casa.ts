import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, ElementRef, OnDestroy, effect, inject, input, signal, untracked, viewChildren } from '@angular/core';
import { MarkLineComponent } from 'echarts/components';
import { firstValueFrom } from 'rxjs';
import { resolverColor } from '../core/color-css';
import { TemaService } from '../core/tema.service';
import { echarts } from '../inicio/graficas';

echarts.use([MarkLineComponent]);

export interface PuntoClima { t: string; v: number; }
export interface Extremo { t: string; v: number; }
export interface SerieClima { puntos: PuntoClima[]; min: number | null; media: number | null; max: number | null; actual: number | null; masBajo: Extremo | null; masAlto: Extremo | null; }
export interface EstanciaClima {
  id: number | null; nombre: string; icono: string; sensores: { id: number; nombre: string }[];
  temperatura?: SerieClima; humedad?: SerieClima; hizoFrio: boolean; riesgoMoho: boolean;
}
export interface RespuestaClima { dias: number; frioBajo: number; humedadAlta: number; estancias: EstanciaClima[]; }

const RANGOS: { dias: number; nombre: string }[] = [{ dias: 1, nombre: '24 h' }, { dias: 7, nombre: 'Semana' }, { dias: 30, nombre: 'Mes' }, { dias: 180, nombre: '6 meses' }];

// Clima de la casa: una tarjeta por habitación con la temperatura y la humedad de sus sensores, lo más frío y lo más cálido del periodo,
// y las marcas de «hace frío» (menos de 17 °) y «riesgo de moho» (más de 65 % de humedad). Se puede bajar todo en CSV.
@Component({
  selector: 'app-clima-casa',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="cab">
      <div class="selector" role="group" aria-label="Periodo">
        @for (r of rangos; track r.dias) { <button type="button" [attr.aria-pressed]="dias() === r.dias" (click)="cambiar(r.dias)">{{ r.nombre }}</button> }
      </div>
      <a class="enlace" [href]="'/api/domotica/clima/csv?dias=' + dias()" download="clima-casa.csv">Bajar CSV</a>
    </section>

    @if (fallo()) { <p class="vacio">No se pudo leer el clima. <button class="enlace" type="button" (click)="cargar()">Reintentar</button></p> }
    @else if (datos() === null) { <p class="vacio" aria-busy="true">Cargando…</p> }
    @else if (datos()!.estancias.length === 0) {
      <p class="vacio">Aún no hay sensores de temperatura ni humedad. Añade un termómetro en el panel y, en un rato, aquí aparece su historia.</p>
    } @else {
      <div class="rejilla">
        @for (e of datos()!.estancias; track e.nombre) {
          <article class="tarjeta">
            <header>
              <h3>{{ e.icono }} {{ e.nombre }}</h3>
              <span class="sensores">{{ e.sensores.length === 1 ? e.sensores[0].nombre : e.sensores.length + ' sensores' }}</span>
            </header>
            <div class="cifras">
              @if (e.temperatura; as t) { <div><span class="etq">Temperatura</span><strong>{{ n(t.actual) }}<small> °C</small></strong><small>mín {{ n(t.min) }} · media {{ n(t.media) }} · máx {{ n(t.max) }}</small></div> }
              @if (e.humedad; as h) { <div><span class="etq">Humedad</span><strong>{{ n(h.actual) }}<small> %</small></strong><small>mín {{ n(h.min) }} · media {{ n(h.media) }} · máx {{ n(h.max) }}</small></div> }
            </div>
            @if (e.hizoFrio) { <p class="marca marca--frio">Bajó de {{ datos()!.frioBajo }} ° en este periodo.</p> }
            @if (e.riesgoMoho) { <p class="marca marca--moho">La humedad pasó del {{ datos()!.humedadAlta }} %: riesgo de moho, ventila.</p> }
            <div class="grafica" #grafica [attr.data-clave]="e.nombre" role="img" [attr.aria-label]="'Temperatura y humedad de ' + e.nombre"></div>
            @if (e.temperatura?.masBajo; as b) { <p class="extremos">Lo más frío: <b>{{ n(b.v) }} °</b> {{ cuando(b.t) }} · lo más cálido: <b>{{ n(e.temperatura!.masAlto!.v) }} °</b> {{ cuando(e.temperatura!.masAlto!.t) }}</p> }
          </article>
        }
      </div>
    }
  `,
  styles: `
    :host { display: block; }
    .cab { display: flex; align-items: center; justify-content: space-between; gap: 12px; flex-wrap: wrap; margin-bottom: 16px; }
    .selector { display: inline-flex; flex-wrap: wrap; gap: 4px; }
    .selector button { border: 1px solid var(--vidrio-borde); background: none; color: var(--soft); border-radius: 999px; padding: 6px 14px; font: 500 .8rem/1 var(--font-text); min-height: 40px; cursor: pointer; }
    .selector button[aria-pressed='true'] { background: var(--brand); border-color: var(--brand); color: var(--on-brand); }
    .enlace { background: none; border: 0; color: var(--brand); font: inherit; cursor: pointer; text-decoration: none; }
    .vacio { color: var(--soft); }
    .rejilla { display: grid; grid-template-columns: repeat(auto-fill, minmax(340px, 1fr)); gap: 16px; }
    .tarjeta { padding: 16px; border-radius: var(--radio-l, 16px); border: 1px solid var(--vidrio-borde); background: var(--vidrio); display: flex; flex-direction: column; gap: 10px; min-width: 0; }
    .tarjeta header { display: flex; align-items: baseline; justify-content: space-between; gap: 8px; }
    .tarjeta h3 { margin: 0; font: 500 1.05rem/1.2 var(--font-text); }
    .sensores, .extremos, .cifras small { color: var(--soft); font-size: .78rem; }
    .cifras { display: flex; gap: 20px; flex-wrap: wrap; }
    .cifras div { display: flex; flex-direction: column; gap: 2px; }
    .cifras strong { font: 600 1.6rem/1.1 var(--font-display, var(--font-text)); }
    .cifras strong small { font-size: .5em; }
    .etq { color: var(--soft); font-size: .72rem; text-transform: uppercase; letter-spacing: .06em; }
    .marca { margin: 0; padding: 6px 10px; border-radius: 10px; font-size: .82rem; }
    .marca--frio { background: color-mix(in srgb, #4aa3ff 18%, transparent); }
    .marca--moho { background: color-mix(in srgb, var(--bad, #ff5a5a) 20%, transparent); }
    .grafica { width: 100%; height: 190px; }
    .extremos { margin: 0; line-height: 1.5; }
    .extremos b { color: var(--texto); font-weight: 600; }
    @media (max-width: 560px) { .rejilla { grid-template-columns: minmax(0, 1fr); } .grafica { height: 170px; } }
  `,
})
export class ClimaCasa implements OnDestroy {
  private readonly http = inject(HttpClient);
  private readonly tema = inject(TemaService);
  private readonly graficas = viewChildren<ElementRef<HTMLDivElement>>('grafica');
  private readonly instancias = new Map<HTMLDivElement, echarts.ECharts>();
  private readonly observador = new ResizeObserver((es) => es.forEach((e) => this.instancias.get(e.target as HTMLDivElement)?.resize()));

  // se deja la vista a un hueco por si otra pantalla quiere abrir el clima de una estancia concreta
  readonly estancia = input<string | null>(null);
  protected readonly rangos = RANGOS;
  protected readonly dias = signal(1);
  protected readonly datos = signal<RespuestaClima | null>(null);
  protected readonly fallo = signal(false);

  constructor() {
    void this.cargar();
    effect(() => {
      this.tema.cambio();
      this.datos();
      this.graficas();
      untracked(() => setTimeout(() => this.pintar()));
    });
  }

  protected cambiar(d: number): void { this.dias.set(d); void this.cargar(); }

  protected async cargar(): Promise<void> {
    this.fallo.set(false);
    try { this.datos.set(await firstValueFrom(this.http.get<RespuestaClima>(`/api/domotica/clima?dias=${this.dias()}`))); } catch { this.fallo.set(true); }
  }

  protected n(v: number | null | undefined): string { return v == null ? '—' : v.toLocaleString('es-ES', { maximumFractionDigits: 1 }); }

  protected cuando(iso: string): string {
    const d = new Date(iso);
    return this.dias() <= 2 ? d.toLocaleTimeString('es-ES', { hour: '2-digit', minute: '2-digit' }) : d.toLocaleDateString('es-ES', { day: 'numeric', month: 'short' });
  }

  private pintar(): void {
    const d = this.datos();
    if (!d) return;
    const cs = getComputedStyle(document.documentElement);
    const c = (n: string, def: string) => resolverColor(cs.getPropertyValue(n), def);
    const col = { brand: c('--brand', '#ff7a1f'), suave: c('--soft', '#8d979e'), linea: c('--linea', '#1c2a34'), texto: c('--texto', '#e8edf0'), fondo: c('--tarjeta-2', '#15222b') };
    const vivos = new Set<HTMLDivElement>();
    for (const ref of this.graficas()) {
      const el = ref.nativeElement, e = d.estancias.find((x) => x.nombre === el.dataset['clave']);
      if (!e) continue;
      vivos.add(el);
      let g = this.instancias.get(el);
      if (!g) { g = echarts.init(el); this.instancias.set(el, g); this.observador.observe(el); }
      const serie = (nombre: string, s: SerieClima | undefined, color: string, eje: number, marca?: { y: number; texto: string }) => s && s.puntos.length ? [{
        name: nombre, type: 'line', yAxisIndex: eje, showSymbol: false, smooth: true, data: s.puntos.map((p) => [p.t, p.v]), lineStyle: { width: 2, color }, itemStyle: { color },
        ...(marca ? { markLine: { silent: true, symbol: 'none', lineStyle: { color, type: 'dashed', opacity: 0.6 }, label: { color: col.suave, fontSize: 10, formatter: marca.texto }, data: [{ yAxis: marca.y }] } } : {}),
      }] : [];
      g.setOption({
        animationDuration: 400,
        grid: { left: 6, right: 6, top: 18, bottom: 4, containLabel: true },
        legend: { top: 0, right: 0, textStyle: { color: col.suave, fontSize: 11 }, itemWidth: 14, itemHeight: 6 },
        tooltip: { trigger: 'axis', backgroundColor: col.fondo, borderColor: col.linea, textStyle: { color: col.texto } },
        xAxis: { type: 'time', axisLabel: { color: col.suave, hideOverlap: true }, axisLine: { lineStyle: { color: col.linea } }, splitLine: { show: false } },
        yAxis: [
          { type: 'value', scale: true, axisLabel: { color: col.suave, formatter: '{value}°' }, splitLine: { lineStyle: { color: col.linea } } },
          { type: 'value', scale: true, axisLabel: { color: col.suave, formatter: '{value}%' }, splitLine: { show: false } },
        ],
        series: [
          ...serie('Temperatura', e.temperatura, col.brand, 0, { y: d.frioBajo, texto: `frío < ${d.frioBajo}°` }),
          ...serie('Humedad', e.humedad, '#4aa3ff', 1, { y: d.humedadAlta, texto: `moho > ${d.humedadAlta}%` }),
        ],
      }, true);
    }
    // las que ya no están en pantalla se sueltan
    for (const [el, g] of this.instancias) if (!vivos.has(el)) { this.observador.unobserve(el); g.dispose(); this.instancias.delete(el); }
  }

  ngOnDestroy(): void {
    this.observador.disconnect();
    for (const g of this.instancias.values()) g.dispose();
    this.instancias.clear();
  }
}
