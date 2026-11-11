import { DatePipe, DecimalPipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { RecargaService } from '../core/recarga';
import { ICONOS } from '../core/iconos';

interface Lugar { nombre: string; lat: number; lon: number; region?: string | null; }
interface Actual { temperatura: number; sensacion: number; codigo: number; deDia: boolean; viento: number; humedad: number; texto: string; icono: string; }
interface Hora { hora: string; temperatura: number; lluvia: number; codigo: number; icono: string; }
interface Dia { fecha: string; maxima: number; minima: number; lluvia: number; litros: number; codigo: number; texto: string; icono: string; sale: string | null; pone: string | null; uv: number; }
interface Respuesta { lugar: Lugar | null; ahora?: Actual; horas?: Hora[]; dias?: Dia[]; avisos: boolean; }

// El tiempo de donde vives (Open-Meteo): ahora, las próximas horas y la semana. El sitio se elige aquí mismo.
@Component({
  selector: 'app-tiempo',
  template: `
    <header class="cab">
      <span class="ico" aria-hidden="true">{{ datos()?.ahora?.icono ?? '🌤️' }}</span>
      <h3>El tiempo</h3>
      @if (datos()?.lugar && !buscando()) {
        <button type="button" class="lugar" (click)="empezarBusqueda()" [title]="datos()!.lugar!.region ?? ''"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path [attr.d]="iconos.pin" /></svg> {{ datos()!.lugar!.nombre }}</button>
      }
      @if (datos()?.ahora && !buscando()) {
        <button type="button" class="avisos" [class.avisos--on]="datos()!.avisos" role="switch" [attr.aria-checked]="datos()!.avisos"
                (click)="avisos(!datos()!.avisos)" title="Avisarme la víspera si llueve, hay tormenta, mucho calor o helada" aria-label="Avisarme la víspera si llueve, hay tormenta, mucho calor o helada">
          <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M6 17V11a6 6 0 0 1 12 0v6l1.5 2h-15zM10 21h4" /></svg><span>Avisos</span>
        </button>
      }
    </header>

    @if (buscando() || (datos() && !datos()!.lugar)) {
      <div class="buscar">
        <p class="sub">{{ datos()?.lugar ? '¿De dónde quieres ver el tiempo?' : 'Dime dónde vives y te enseño el tiempo (y te aviso si mañana llueve).' }}</p>
        <div class="buscar__fila">
          <input type="search" placeholder="Busca tu ciudad o pueblo" aria-label="Ciudad" maxlength="80" autocomplete="off"
                 [value]="q()" (input)="escribir($any($event.target).value)">
          <button type="button" class="boton-ubicacion" (click)="miUbicacion()" title="Usar mi ubicación" aria-label="Usar mi ubicación">
            <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 2v3M12 19v3M2 12h3M19 12h3" /><circle cx="12" cy="12" r="6" /><circle cx="12" cy="12" r="2" /></svg>
          </button>
        </div>
        @if (resultados().length) {
          <ul class="resultados">
            @for (r of resultados(); track r.lat + ',' + r.lon) {
              <li><button type="button" (click)="elegir(r)"><strong>{{ r.nombre }}</strong><span>{{ r.region }}</span></button></li>
            }
          </ul>
        } @else if (q().length >= 2 && !cargando()) {
          <p class="sub">No encuentro ese sitio.</p>
        }
        @if (error()) { <p class="sub">{{ error() }}</p> }
        @if (datos()?.lugar) { <button type="button" class="enlace" (click)="buscando.set(false)">Cancelar</button> }
      </div>
    } @else if (datos()?.ahora; as a) {
      <div class="ahora">
        <span class="ahora__icono" aria-hidden="true">{{ a.icono }}</span>
        <div class="ahora__temp">
          <strong>{{ a.temperatura | number: '1.0-0' }}°</strong>
          <span>{{ a.texto }}</span>
        </div>
        @if (hoy(); as d) {
          <div class="ahora__dia">
            <span><b>{{ d.maxima | number: '1.0-0' }}°</b> / {{ d.minima | number: '1.0-0' }}°</span>
            <span class="con-icono"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path [attr.d]="iconos.gota" /></svg> {{ d.lluvia }} %</span>
          </div>
        }
      </div>
      <ul class="datos" aria-label="Detalles de ahora">
        <li><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path [attr.d]="iconos.termometro" /></svg>Sensación {{ a.sensacion | number: '1.0-0' }}°</li>
        <li><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path [attr.d]="iconos.gota" /></svg>Humedad {{ a.humedad }} %</li>
        <li><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path [attr.d]="iconos.viento" /></svg>{{ a.viento | number: '1.0-0' }} km/h</li>
        @if (hoy()?.sale) { <li><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path [attr.d]="iconos.sol" /></svg>{{ hoy()!.sale }}–{{ hoy()!.pone }}</li> }
      </ul>

      <ol class="horas" aria-label="Próximas horas">
        @for (h of horas(); track h.hora; let i = $index) {
          <li>
            <span class="horas__h">{{ i === 0 ? 'Ahora' : (h.hora | date: 'HH') + 'h' }}</span>
            <span class="horas__i" aria-hidden="true">{{ i === 0 ? a.icono : h.icono }}</span>
            <b>{{ (i === 0 ? a.temperatura : h.temperatura) | number: '1.0-0' }}°</b>
            <small [class.oculto]="h.lluvia < 20">{{ h.lluvia }} %</small>
          </li>
        }
      </ol>

      <ul class="semana" aria-label="Los próximos días">
        @for (d of dias(); track d.fecha; let i = $index) {
          <li [title]="d.texto" [class.semana__hoy]="i === 0">
            <span class="semana__n">{{ i === 0 ? 'Hoy' : nombreDia(d.fecha) }}</span>
            <span class="semana__i" aria-hidden="true">{{ d.icono }}</span>
            <b>{{ d.maxima | number: '1.0-0' }}°</b>
            <span class="semana__min">{{ d.minima | number: '1.0-0' }}°</span>
            <small [class.oculto]="d.lluvia < 20">{{ d.lluvia }} %</small>
          </li>
        }
      </ul>
    } @else if (error()) {
      <p class="sub">{{ error() }} <button type="button" class="enlace" (click)="cargar()">Reintentar</button></p>
    } @else {
      <div class="esqueleto" aria-hidden="true"></div>
    }
  `,
  styles: `
    :host { --t-gap: 12px; }
    .cab { display: flex; align-items: center; gap: 10px; min-width: 0; }
    .cab h3 { flex: 1; min-width: 0; font: 500 0.95rem/1.1 var(--font-text); letter-spacing: -0.01em; }
    .ico { flex: none; display: grid; place-items: center; width: 34px; height: 34px; border-radius: 10px; background: color-mix(in srgb, var(--brand) 16%, transparent); font-size: 18px; }
    .lugar { max-width: 55%; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; border: 0; border-radius: 999px; padding: 5px 11px; background: var(--tarjeta-2); color: var(--soft); font: 500 12px/1.2 var(--font-text); cursor: pointer; }
    .lugar:hover { color: var(--texto); }
    .lugar { display: inline-flex; align-items: center; gap: 6px; } .lugar .icon, .datos .icon, .con-icono .icon { width: 16px; height: 16px; stroke-width: 1.5; }
    .con-icono { display: inline-flex; align-items: center; gap: 6px; } .datos li { display: flex; align-items: center; gap: 8px; } .datos .icon { color: var(--soft); }
    .sub { margin: 0; font-size: 13px; color: var(--soft); }
    .enlace { border: 0; background: none; padding: 0; color: var(--acento); font: inherit; font-size: 13px; cursor: pointer; text-decoration: underline; justify-self: start; }

    .buscar { display: grid; gap: 10px; }
    .buscar__fila { display: flex; gap: 8px; }
    .buscar input { flex: 1; min-width: 0; min-height: 44px; padding: 0 14px; border-radius: 12px; border: 1px solid var(--linea); background: var(--tarjeta); color: var(--texto); font: 16px var(--font-text); }
    .buscar input:focus { outline: 2px solid var(--brand); outline-offset: 1px; }
    .boton-ubicacion { flex: none; display: grid; place-items: center; width: 44px; height: 44px; border: 1px solid var(--linea); border-radius: 12px; background: var(--tarjeta); color: var(--texto); cursor: pointer; }
    .boton-ubicacion svg { width: 20px; height: 20px; fill: none; stroke: currentColor; stroke-width: 1.8; stroke-linecap: round; }
    .resultados { list-style: none; margin: 0; padding: 0; display: grid; gap: 4px; max-height: 180px; overflow: auto; }
    .resultados button { width: 100%; display: flex; flex-direction: column; align-items: flex-start; gap: 2px; padding: 9px 12px; border: 0; border-radius: 10px; background: var(--tarjeta-2); color: var(--texto); text-align: left; cursor: pointer; font: 14px var(--font-text); }
    .resultados button:hover { background: color-mix(in srgb, var(--brand) 14%, var(--tarjeta-2)); }
    .resultados span { font-size: 12px; color: var(--soft); }

    .ahora { display: flex; align-items: center; gap: 14px; }
    .ahora__icono { font-size: 52px; line-height: 1; filter: drop-shadow(0 6px 14px rgba(0, 0, 0, 0.18)); }
    .ahora__temp { display: flex; flex-direction: column; gap: 4px; flex: 1; min-width: 0; }
    .ahora__temp strong { font: 700 clamp(2.4rem, 1.8rem + 2vw, 3.2rem)/0.9 var(--font-display); letter-spacing: -0.05em; }
    .ahora__temp span { font-size: 14px; color: var(--soft); }
    .ahora__dia { display: flex; flex-direction: column; align-items: flex-end; gap: 4px; font-size: 14px; color: var(--soft); white-space: nowrap; }
    .ahora__dia b { color: var(--texto); }
    .datos { list-style: none; margin: 0; padding: 0; display: flex; flex-wrap: wrap; gap: 6px; }
    .datos li { display: inline-flex; align-items: center; gap: 5px; padding: 4px 10px; border-radius: 999px; background: var(--tarjeta-2); font-size: 12px; color: var(--soft); white-space: nowrap; }
    .datos li span { font-size: 12px; }

    .horas { list-style: none; margin: 0; padding: 2px 0 6px; display: flex; gap: 6px; overflow-x: auto; scrollbar-width: thin; scroll-snap-type: x proximity; }
    .horas li { flex: none; scroll-snap-align: start; display: flex; flex-direction: column; align-items: center; gap: 3px; min-width: 50px; padding: 8px 4px; border-radius: 12px; background: var(--tarjeta-2); }
    .horas li:first-child { background: color-mix(in srgb, var(--brand) 18%, var(--tarjeta-2)); }
    .horas__h { font-size: 11px; color: var(--soft); }
    .horas__i { font-size: 20px; }
    .horas b { font-size: 14px; font-variant-numeric: tabular-nums; }
    .horas small { font-size: 10.5px; color: #4aa3ff; font-weight: 600; }

    /* la semana en una sola fila de siete columnas: antes eran siete filas y la tarjeta se estiraba mucho más que las de al lado */
    .semana { list-style: none; margin: 0; padding: 0; display: grid; grid-template-columns: repeat(7, minmax(0, 1fr)); gap: 6px; }
    .semana li { display: flex; flex-direction: column; align-items: center; gap: 2px; min-width: 0; padding: 8px 2px 6px; border-radius: 12px; background: var(--tarjeta-2); font-variant-numeric: tabular-nums; }
    .semana__hoy { background: color-mix(in srgb, var(--brand) 18%, var(--tarjeta-2)) !important; }
    .semana__n { font-size: 11.5px; color: var(--soft); text-transform: capitalize; }
    .semana__i { font-size: 20px; line-height: 1.2; }
    .semana b { font-size: 14px; }
    .semana__min { font-size: 12px; color: var(--soft); }
    .semana small { font-size: 10.5px; color: #4aa3ff; font-weight: 600; }
    .oculto { visibility: hidden; }

    .avisos { flex: none; display: inline-flex; align-items: center; gap: 6px; border: 0; border-radius: 999px; padding: 5px 11px; background: var(--tarjeta-2); color: var(--soft); font: 500 12px/1.2 var(--font-text); cursor: pointer; }
    .avisos svg { width: 14px; height: 14px; fill: none; stroke: currentColor; stroke-width: 1.8; stroke-linecap: round; stroke-linejoin: round; }
    .avisos--on { background: color-mix(in srgb, var(--brand) 22%, var(--tarjeta-2)); color: var(--texto); }
    .avisos:focus-visible, .lugar:focus-visible { outline: 2px solid var(--acento); outline-offset: 2px; }
    .esqueleto { height: 180px; border-radius: 14px; background: linear-gradient(90deg, var(--tarjeta-2), var(--tarjeta), var(--tarjeta-2)); background-size: 200% 100%; animation: brillo 1.4s linear infinite; }
    @keyframes brillo { to { background-position: -200% 0; } }
    @media (max-width: 420px) { .avisos span { display: none; } .avisos { padding: 6px 8px; } .lugar { max-width: 42%; } .semana { gap: 4px; } }
  `,
  imports: [DatePipe, DecimalPipe],
})
export class Tiempo {
  private readonly http = inject(HttpClient);
  protected readonly iconos = ICONOS;
  protected readonly datos = signal<Respuesta | null>(null);
  protected readonly error = signal('');
  protected readonly buscando = signal(false);
  protected readonly cargando = signal(false);
  protected readonly q = signal('');
  protected readonly resultados = signal<Lugar[]>([]);
  private espera?: ReturnType<typeof setTimeout>;

  protected readonly hoy = computed(() => this.datos()?.dias?.[0] ?? null);
  protected readonly horas = computed(() => (this.datos()?.horas ?? []).slice(0, 13));
  protected readonly dias = computed(() => this.datos()?.dias ?? []);

  constructor() {
    this.cargar();
    inject(RecargaService).alRecargar(() => this.cargar(), inject(DestroyRef));
    const t = setInterval(() => { if (!document.hidden && !this.buscando()) this.cargar(); }, 15 * 60_000);
    inject(DestroyRef).onDestroy(() => { clearInterval(t); clearTimeout(this.espera); });
  }

  async cargar(): Promise<void> {
    this.error.set('');
    try { this.datos.set(await firstValueFrom(this.http.get<Respuesta>('/api/tiempo'))); }
    catch { this.error.set('No he podido consultar el tiempo.'); }
  }

  protected nombreDia(fecha: string): string {
    return new Date(fecha + 'T12:00').toLocaleDateString('es-ES', { weekday: 'short' }).replace('.', '');
  }

  protected empezarBusqueda(): void {
    this.q.set('');
    this.resultados.set([]);
    this.buscando.set(true);
  }

  protected escribir(v: string): void {
    this.q.set(v);
    clearTimeout(this.espera);
    if (v.trim().length < 2) { this.resultados.set([]); return; }
    this.cargando.set(true);
    this.espera = setTimeout(async () => {
      try { this.resultados.set(await firstValueFrom(this.http.get<Lugar[]>('/api/tiempo/buscar', { params: { q: v.trim() } }))); }
      catch { this.resultados.set([]); }
      finally { this.cargando.set(false); }
    }, 350);
  }

  protected async elegir(l: Lugar): Promise<void> {
    await firstValueFrom(this.http.put('/api/tiempo/lugar', l));
    this.buscando.set(false);
    this.datos.set(null);
    await this.cargar();
  }

  protected miUbicacion(): void {
    if (!navigator.geolocation) return;
    navigator.geolocation.getCurrentPosition(
      (p) => this.elegir({ nombre: 'Tu ubicación', lat: Math.round(p.coords.latitude * 1e4) / 1e4, lon: Math.round(p.coords.longitude * 1e4) / 1e4 }),
      () => this.error.set('No me has dado permiso para saber dónde estás: busca tu ciudad.'),
      { timeout: 10_000, maximumAge: 3_600_000 },
    );
  }

  protected async avisos(activos: boolean): Promise<void> {
    await firstValueFrom(this.http.put('/api/tiempo/avisos', { activos }));
    this.datos.update((d) => (d ? { ...d, avisos: activos } : d));
  }
}
