import { HttpClient } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CuentaAtras } from './cuenta-atras';
import { PersonalizarInicio } from './personalizar-inicio';
import { CumplesChip } from './cumples-chip';
import { RopaChip } from './ropa-chip';
import { Tiempo } from './tiempo';
import { firstValueFrom } from 'rxjs';
import { Aparicion, colorEvento, hora, mismoDia } from '../agenda/agenda.service';
import { AsistenteService } from '../core/asistente.service';
import { RAICES } from '../documentos/archivos.service';
import { Deudas, Gasolina, ResumenMes, euros, eurosCorto, numero } from '../gastos/modelo';
import { progresoTareas } from '../notas/markdown';
import { Nota, colorNota } from '../notas/notas.service';
import { RecargaService } from '../core/recarga';

interface Cancion { nombre: string; artistas: string; imagen: string | null; duracion: number; }
interface Sonando { sonando: boolean; cancion: Cancion | null; progreso: number; dispositivo: string | null; }
interface Seguridad { dosPasos: boolean; fallosMes: number; }
interface Raiz { id: string; nombre: string; elementos: number; }
interface VentanaLuz { tipo: string; titulo: string; inicio: string; fin: string; puntuacion: number | null; motivo?: string | null; }
interface AparatoRed { id: number; nombre: string; enLinea: boolean | null; }
interface EstadoPcInicio { agenteConectado: boolean; salud: { volumenes: { nombre: string; libreBytes: number; totalBytes: number }[]; reinicioPendiente: boolean } | null; }

// Panel del inicio: un vistazo a todo — el dinero del mes, lo que suena, el coche, la agenda,
// los recordatorios, las notas fijadas, la seguridad, la gasolina y las carpetas. Cada tarjeta carga sola
// y, si su sección aún no tiene datos, invita a empezar.
@Component({
  selector: 'app-panel',
  imports: [RouterLink, Tiempo, CuentaAtras, RopaChip, CumplesChip, PersonalizarInicio],
  templateUrl: './panel.html',
  styleUrl: './panel.css',
})
export class Panel {
  private readonly http = inject(HttpClient);
  protected readonly asistente = inject(AsistenteService);
  protected readonly euros = euros;
  protected readonly eurosCorto = eurosCorto;
  protected readonly numero = numero;
  protected readonly colorEvento = colorEvento;
  protected readonly colorNota = colorNota;
  protected readonly progresoTareas = progresoTareas;
  protected readonly raicesInfo = RAICES;

  protected readonly mes = signal<ResumenMes | null>(null);
  protected readonly saldo = signal<{ configurado: boolean; actual: number; libre: number; enHuchas: number } | null>(null);
  protected readonly porDia = signal<number | null>(null);
  // tu próximo cobro (si se conoce): el «por día» se cuenta hasta entonces, no hasta fin de mes
  protected readonly cobro = signal<{ dia: number | null; proximo: string | null; diasHasta: number | null; porDia: number | null; retrasado: boolean } | null>(null);
  protected readonly sonando = signal<Sonando | null>(null);
  protected readonly spotify = signal<'si' | 'no' | null>(null);
  protected readonly progreso = signal(0);
  protected readonly deudas = signal<Deudas | null>(null);
  protected readonly agenda = signal<Aparicion[] | null>(null);
  protected readonly notas = signal<Nota[] | null>(null);
  protected readonly sesiones = signal<{ enLinea: boolean }[]>([]);
  protected readonly seguridad = signal<Seguridad | null>(null);
  protected readonly gasolina = signal<Gasolina | null>(null);
  protected readonly raices = signal<Raiz[]>([]);
  protected readonly luz = signal<{ lugar: string | null; hoy: VentanaLuz[] } | null>(null);
  protected readonly pc = signal<EstadoPcInicio | null>(null);
  protected readonly llaves = signal(0);
  protected readonly red = signal<AparatoRed[] | null>(null);
  protected readonly habitos = signal<{ tocaHoy: boolean; hecho: boolean; actual: number }[] | null>(null);
  // los hábitos que tocan hoy: cuántos están hechos y la racha más larga en marcha
  protected readonly habitosHoy = computed(() => {
    const l = (this.habitos() ?? []).filter((h) => h.tocaHoy);
    return { total: l.length, hechos: l.filter((h) => h.hecho).length, racha: Math.max(0, ...(this.habitos() ?? []).map((h) => h.actual)) };
  });
  protected readonly redCaida = computed(() => (this.red() ?? []).filter((a) => a.enLinea === false));

  // la mejor ventana de luz de hoy que aún no ha terminado
  protected readonly luzHoy = computed(() => {
    const ahora = Date.now();
    return [...(this.luz()?.hoy ?? [])].filter((v) => new Date(v.fin).getTime() > ahora).sort((a, b) => (b.puntuacion ?? -1) - (a.puntuacion ?? -1))[0] ?? null;
  });
  protected readonly discoPc = computed(() => {
    const p = this.pc()?.salud;
    if (!p) return 'Aún sin lecturas de salud';
    const v = p.volumenes.find((x) => x.totalBytes) ;
    const usado = v ? Math.round(100 - (100 * v.libreBytes) / v.totalBytes) : null;
    return `${usado != null ? `Disco ${v!.nombre} al ${usado} %` : 'Discos sanos'}${p.reinicioPendiente ? ' · reinicio pendiente' : ''}`;
  });
  protected horaLuz(iso: string): string { return iso.slice(11, 16); }

  protected readonly deuda = computed(() => this.deudas()?.deudas.find((d) => d.restante > 0) ?? this.deudas()?.deudas[0] ?? null);
  protected readonly eventos = computed(() => (this.agenda() ?? []).filter((a) => a.tipo === 'Evento').slice(0, 4));
  protected readonly recordatorios = computed(() => (this.agenda() ?? []).filter((a) => a.tipo === 'Recordatorio' && !a.hecho));
  protected readonly fijadas = computed(() => (this.notas() ?? []).filter((n) => n.fijada).slice(0, 3));
  protected readonly enLinea = computed(() => this.sesiones().filter((s) => s.enLinea).length);
  protected readonly totalArchivos = computed(() => this.raices().reduce((t, r) => t + r.elementos, 0));
  protected readonly pct = computed(() => { const c = this.sonando()?.cancion; return c?.duracion ? Math.min(100, (this.progreso() / c.duracion) * 100) : 0; });

  // barras del gasto de cada día del mes (hasta hoy), escaladas al día que más
  protected readonly dias = computed(() => {
    const m = this.mes();
    if (!m) return [];
    const hoy = new Date();
    const hasta = m.anio === hoy.getFullYear() && m.mes === hoy.getMonth() + 1 ? hoy.getDate() : m.gastoPorDia.length;
    const max = Math.max(1, ...m.gastoPorDia);
    return m.gastoPorDia.map((v, i) => ({ v, h: Math.max(3, (v / max) * 100), futuro: i >= hasta, hoy: i === hasta - 1 }));
  });
  protected readonly diferencia = computed(() => {
    const m = this.mes();
    return m && m.anterior.gastos ? Math.round(((m.gastos - m.anterior.gastos) / m.anterior.gastos) * 100) : null;
  });
  protected readonly nombreMes = new Date().toLocaleDateString('es-ES', { month: 'long' });

  constructor() {
    this.cargar();
    inject(RecargaService).alRecargar(() => this.cargar(), inject(DestroyRef));
    const t = setInterval(() => { if (!document.hidden && this.spotify() === 'si') this.leerMusica(); }, 8000);
    const s = setInterval(() => { if (this.sonando()?.sonando) this.progreso.update((p) => p + 1000); }, 1000);
    inject(DestroyRef).onDestroy(() => { clearInterval(t); clearInterval(s); });
  }

  private cargar(): Promise<unknown> {
    const get = <T>(url: string) => firstValueFrom(this.http.get<T>(url));
    const hoy = new Date();
    return Promise.allSettled([
      get<ResumenMes>(`/api/gastos/mes/${hoy.getFullYear()}/${hoy.getMonth() + 1}`).then((m) => this.mes.set(m)),
      get<{ configurado: boolean; actual: number; libre: number; enHuchas: number }>('/api/gastos/saldo').then((x) => this.saldo.set(x)),
      get<{ porDia: number }>('/api/gastos/consejos').then((c) => this.porDia.set(c.porDia)),
      get<{ dia: number | null; proximo: string | null; diasHasta: number | null; porDia: number | null; retrasado: boolean }>('/api/gastos/cobro').then((c) => this.cobro.set(c)).catch(() => this.cobro.set(null)),
      get<Deudas>('/api/gastos/deudas').then((d) => this.deudas.set(d)),
      get<Aparicion[]>('/api/agenda/proximos?dias=14').then((a) => this.agenda.set(a)).catch(() => this.agenda.update((v) => v ?? [])),
      get<Nota[]>('/api/notas').then((n) => this.notas.set(n)).catch(() => this.notas.update((v) => v ?? [])),
      get<{ enLinea: boolean }[]>('/api/seguridad/sesiones').then((s) => this.sesiones.set(s)),
      get<Seguridad>('/api/seguridad/estado').then((s) => this.seguridad.set(s)),
      get<Gasolina>('/api/gastos/gasolina').then((g) => this.gasolina.set(g)),
      get<Raiz[]>('/api/archivos/raices').then((r) => this.raices.set(r)),
      get<{ lugar: string | null; hoy: VentanaLuz[] }>('/api/luz').then((l) => this.luz.set(l)).catch(() => this.luz.set({ lugar: null, hoy: [] })),
      get<EstadoPcInicio>('/api/pc').then((p) => this.pc.set(p)).catch(() => {}),
      get<{ tocaHoy: boolean; hecho: boolean; actual: number }[]>('/api/habitos').then((h) => this.habitos.set(h)).catch(() => this.habitos.set([])),
      get<unknown[]>('/api/passkeys').then((l) => this.llaves.set(l.length)).catch(() => {}),
      get<AparatoRed[]>('/api/red').then((r) => this.red.set(r)).catch(() => {}),
      this.leerMusica(),
    ]);
  }

  private async leerMusica(): Promise<void> {
    try {
      const s = await firstValueFrom(this.http.get<Sonando>('/api/musica/ahora'));
      this.sonando.set(s);
      this.progreso.set(s.progreso);
      this.spotify.set('si');
    } catch { this.spotify.set('no'); }
  }

  protected async control(e: Event, accion: 'play' | 'pause' | 'next'): Promise<void> {
    e.preventDefault();
    e.stopPropagation();
    if (accion !== 'next') this.sonando.update((s) => (s ? { ...s, sonando: accion === 'play' } : s));
    try { await firstValueFrom(this.http.post(`/api/musica/control/${accion}`, null)); } catch { /* sin Premium o sin dispositivo */ }
    setTimeout(() => this.leerMusica(), 500);
  }

  protected async hecho(e: Event, a: Aparicion): Promise<void> {
    e.preventDefault();
    e.stopPropagation();
    if (!a.id) return;
    this.agenda.update((l) => (l ?? []).map((x) => (x.clave === a.clave ? { ...x, hecho: true } : x)));
    try { await firstValueFrom(this.http.post(`/api/agenda/${a.id}/hecho`, null, { params: { hecho: true } })); } catch { /* se verá al recargar */ }
  }

  protected cuando(a: Aparicion): string {
    const i = new Date(a.inicio), hoy = new Date(), man = new Date(hoy.getFullYear(), hoy.getMonth(), hoy.getDate() + 1);
    const dia = mismoDia(i, hoy) ? 'Hoy' : mismoDia(i, man) ? 'Mañana' : i < hoy ? 'Atrasado' : i.toLocaleDateString('es-ES', { weekday: 'short', day: 'numeric' });
    return a.todoElDia ? dia : `${dia} · ${hora(i)}`;
  }
  // 10458 -> «10 mil»: los números de las carpetas caben en su casilla
  protected compacto(n: number): string { return n < 10000 ? n.toLocaleString('es-ES') : new Intl.NumberFormat('es-ES', { notation: 'compact', maximumFractionDigits: 0 }).format(n); }
  protected anillo(pct: number): string { const l = 2 * Math.PI * 40; return `${(Math.min(pct, 100) / 100) * l} ${l}`; }
  protected icono(id: string): string { return this.raicesInfo[id]?.icono ?? 'M3 6.5A1.5 1.5 0 0 1 4.5 5H9l2 2.5h8.5A1.5 1.5 0 0 1 21 9v9.5a1.5 1.5 0 0 1-1.5 1.5h-15A1.5 1.5 0 0 1 3 18.5z'; }
  protected color(id: string): string { return this.raicesInfo[id]?.color ?? 'var(--brand)'; }
}
