import { HttpClient } from '@angular/common/http';
import { Component, ElementRef, computed, effect, inject, signal, untracked, viewChild } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AgendaService, DIAS_SEMANA, EventoEntrada, Horario, textoHorario } from '../agenda/agenda.service';
import { AsistenteService } from '../core/asistente.service';
import { GastosService, MovimientoEntrada, erroresDe } from '../gastos/gastos.service';
import { seguirTeclado } from '../gastos/teclado';
import { CATEGORIAS, FRECUENCIAS, Movimiento, categoria, euros } from '../gastos/modelo';

type Clase = 'movimiento' | 'agenda' | 'hucha' | 'horario' | 'pregunta' | 'nada';
interface AporteHucha { id: number; nombre: string; emoji: string; importe: number; }
interface Propuesta {
  clase: Clase; resumen: string; movimiento: MovimientoEntrada | null; evento: EventoEntrada | null; hucha: AporteHucha | null;
  dudas: string[] | null; respuesta: string | null; horario: Horario | null;
}
interface Hecho { texto: string; deshacer: () => Promise<unknown>; ver?: () => void; deshecho?: boolean; }

interface Reconocedor {
  lang: string; interimResults: boolean; continuous: boolean; start(): void; stop(): void;
  onresult: ((e: { results: ArrayLike<{ isFinal: boolean; 0: { transcript: string } }> }) => void) | null;
  onend: (() => void) | null; onerror: ((e: { error: string }) => void) | null;
}
type ConstructorReconocedor = new () => Reconocedor;
const Reconocimiento = (globalThis as unknown as { SpeechRecognition?: ConstructorReconocedor; webkitSpeechRecognition?: ConstructorReconocedor });

const EJEMPLOS = [
  'ayer 12 € en el Mercadona', 'cobré la nómina 1.450 €', 'recuérdame mañana a las 9 llamar al taller', 'luz unos 60 € cada mes',
  'médico el martes de 10 a 11 en el centro de salud', 'curro de 8:30 a 17:30', '¿qué tengo mañana?',
  '50 € a la hucha', '¿cuánto llevo en restaurantes?', '¿cuánto tengo?',
];

// Dos dígitos: para pasar fechas a los campos de fecha y hora del navegador
const dd = (n: number) => String(n).padStart(2, '0');
const aLocal = (iso: string) => { const d = new Date(iso); return `${d.getFullYear()}-${dd(d.getMonth() + 1)}-${dd(d.getDate())}T${dd(d.getHours())}:${dd(d.getMinutes())}`; };

// El asistente: escribes o dices una frase («ayer 12 € en el Mercadona») y lo apunta donde toca.
// Antes de guardar enseña lo que ha entendido y deja retocarlo; después deja deshacerlo.
@Component({
  selector: 'app-asistente',
  host: { '(document:keydown)': 'atajo($event)' },
  templateUrl: './asistente.html',
  styleUrl: './asistente.css',
})
export class Asistente {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly gastos = inject(GastosService);
  private readonly agenda = inject(AgendaService);
  private readonly servicio = inject(AsistenteService);
  private readonly dlg = viewChild.required<ElementRef<HTMLDialogElement>>('dlg');
  private readonly campo = viewChild.required<ElementRef<HTMLInputElement>>('campo');

  protected readonly ejemplos = EJEMPLOS;
  protected readonly categoria = categoria;
  protected readonly aLocal = aLocal;
  protected readonly puedeHablar = !!(Reconocimiento.SpeechRecognition ?? Reconocimiento.webkitSpeechRecognition);

  protected readonly texto = signal('');
  protected readonly propuesta = signal<Propuesta | null>(null);
  protected readonly mov = signal<MovimientoEntrada | null>(null);
  protected readonly ev = signal<EventoEntrada | null>(null);
  protected readonly hu = signal<AporteHucha | null>(null);
  protected readonly hz = signal<Horario | null>(null);
  protected readonly diasSemana = DIAS_SEMANA;
  protected readonly textoHorario = textoHorario;
  protected readonly pensando = signal(false);
  protected readonly escuchando = signal(false);
  protected readonly guardando = signal(false);
  protected readonly error = signal('');
  protected readonly hecho = signal<Hecho | null>(null);
  protected readonly historial = signal<Hecho[]>([]);
  // el importe se pinta una vez al llegar la propuesta (para no mover el cursor mientras escribes) y el campo crece con lo escrito
  protected readonly importeTexto = signal('');
  protected readonly ancho = signal(4);

  protected readonly categorias = computed(() => CATEGORIAS.filter((c) => c.tipo === (this.mov()?.tipo ?? 'Gasto')));
  protected readonly accion = computed(() => {
    switch (this.propuesta()?.clase) {
      case 'movimiento': return this.mov()?.tipo === 'Ingreso' ? 'Apuntar ingreso' : 'Apuntar gasto';
      case 'agenda': return this.ev()?.tipo === 'Recordatorio' ? 'Guardar recordatorio' : 'Guardar evento';
      case 'hucha': return (this.hu()?.importe ?? 0) < 0 ? 'Sacar de la hucha' : 'Meter en la hucha';
      case 'horario': return 'Guardar horario';
      default: return null;
    }
  });

  private temporizador?: ReturnType<typeof setTimeout>;
  private turno = 0;
  private reconocedor?: Reconocedor;
  private soltarTeclado?: () => void;

  constructor() {
    effect(() => {
      const p = this.servicio.peticion();
      if (!p) return;
      untracked(() => {
        this.abrir();
        if (p.texto) this.usar(p.texto);
        if (p.voz) this.hablar();
      });
    });
  }

  abrir(): void {
    const d = this.dlg().nativeElement;
    if (!d.open) { d.showModal(); this.soltarTeclado = seguirTeclado(d); }
    setTimeout(() => this.campo().nativeElement.focus(), 40);
  }
  protected cerrar(): void { this.dlg().nativeElement.close(); }
  protected fuera(e: MouseEvent): void { if (e.target === this.dlg().nativeElement) this.cerrar(); }
  protected alCerrar(): void {
    this.reconocedor?.stop();
    this.soltarTeclado?.();
    this.soltarTeclado = undefined;
    this.limpiar();
    this.hecho.set(null);
  }

  protected atajo(e: KeyboardEvent): void {
    if (e.key.toLowerCase() === 'j' && (e.ctrlKey || e.metaKey)) { e.preventDefault(); this.abrir(); }
  }

  private limpiar(): void {
    this.texto.set('');
    this.propuesta.set(null);
    this.mov.set(null); this.ev.set(null); this.hu.set(null); this.hz.set(null);
    this.error.set('');
  }

  protected usar(t: string): void { this.texto.set(t); this.entender(t, 0); this.campo().nativeElement.focus(); }

  protected escribir(t: string): void {
    this.texto.set(t);
    if (!t.trim()) { this.limpiar(); return; }
    this.entender(t, 320);
  }

  private entender(t: string, espera: number): void {
    clearTimeout(this.temporizador);
    const turno = ++this.turno;
    this.temporizador = setTimeout(async () => {
      this.pensando.set(true);
      try {
        const p = await firstValueFrom(this.http.post<Propuesta>('/api/asistente/entender', { texto: t.trim() }));
        if (turno !== this.turno) return; // ya has seguido escribiendo
        this.error.set('');
        this.propuesta.set(p);
        this.mov.set(p.movimiento ? { ...p.movimiento } : null);
        this.ev.set(p.evento ? { ...p.evento } : null);
        this.hu.set(p.hucha ? { ...p.hucha } : null);
        this.hz.set(p.horario ? { ...p.horario } : null);
        const imp = p.movimiento?.importe ?? Math.abs(p.hucha?.importe ?? 0);
        this.importeTexto.set(imp.toFixed(2));
        this.ancho.set(Math.max(3, imp.toFixed(2).length - 0.2));
      } catch {
        if (turno === this.turno) this.error.set('No he podido pensarlo: ¿hay conexión?');
      } finally {
        if (turno === this.turno) this.pensando.set(false);
      }
    }, espera);
  }

  // ---- retoques antes de guardar ----
  protected cambiar(c: Partial<MovimientoEntrada>): void { this.mov.update((m) => (m ? { ...m, ...c } : m)); }
  protected ponerTipo(tipo: 'Gasto' | 'Ingreso'): void {
    const m = this.mov();
    if (!m || m.tipo === tipo) return;
    const vale = categoria(m.categoria).tipo === tipo;
    this.cambiar({ tipo, categoria: vale ? m.categoria : tipo === 'Gasto' ? 'otros' : 'otros-ingresos' });
  }
  protected cambiarEv(c: Partial<EventoEntrada>): void { this.ev.update((e) => (e ? { ...e, ...c } : e)); }
  protected ponerMomento(v: string): void {
    if (!v) return;
    const ini = new Date(v);
    // se conserva lo que dura
    this.ev.update((e) => (e ? { ...e, inicio: ini.toISOString(), fin: e.fin ? new Date(ini.getTime() + (new Date(e.fin).getTime() - new Date(e.inicio).getTime())).toISOString() : null } : e));
  }
  protected ponerFin(v: string): void {
    const e = this.ev();
    if (!v || !e) return;
    const [h, m] = v.split(':').map(Number);
    const fin = new Date(e.inicio);
    fin.setHours(h, m, 0, 0);
    if (fin > new Date(e.inicio)) this.cambiarEv({ fin: fin.toISOString() });
  }
  protected alternarDia(n: number): void {
    this.hz.update((h) => (h ? { ...h, dias: h.dias.includes(n) ? h.dias.filter((x) => x !== n) : [...h.dias, n].sort() } : h));
  }
  protected ponerDia(v: string): void {
    if (!v) return;
    const [a, m, d] = v.split('-').map(Number);
    this.cambiarEv({ inicio: new Date(a, m - 1, d).toISOString() });
  }
  protected teclearImporte(t: string): void {
    this.ancho.set(Math.max(3, t.length - 0.2));
    const v = Math.abs(+t.replace(',', '.'));
    if (this.mov()) this.cambiar({ importe: v });
    else this.ponerHucha(v);
  }
  protected ponerHucha(v: number): void { const sacar = (this.propuesta()?.hucha?.importe ?? 0) < 0; this.hu.update((h) => (h ? { ...h, importe: (sacar ? -1 : 1) * Math.abs(v) } : h)); }
  protected cada(f: string): string { return FRECUENCIAS.find((x) => x.id === f)?.cada ?? f; }
  protected repeticion(r: string): string { return ({ Diaria: 'cada día', Semanal: 'cada semana', Mensual: 'cada mes', Anual: 'cada año' } as Record<string, string>)[r] ?? r; }

  // ---- guardar ----
  protected async hacer(): Promise<void> {
    const p = this.propuesta();
    if (!p || !this.accion() || this.guardando()) return;
    const importe = p.clase === 'movimiento' ? this.mov()?.importe : p.clase === 'hucha' ? this.hu()?.importe : 1;
    if (!importe || !Number.isFinite(importe)) { this.error.set('Pon un importe mayor que cero.'); return; }
    this.guardando.set(true);
    this.error.set('');
    try {
      let h: Hecho | null = null;
      if (p.clase === 'movimiento' && this.mov()) h = await this.guardarMovimiento(this.mov()!);
      else if (p.clase === 'agenda' && this.ev()) h = await this.guardarEvento(this.ev()!);
      else if (p.clase === 'hucha' && this.hu()) h = await this.guardarHucha(this.hu()!);
      else if (p.clase === 'horario' && this.hz()) h = await this.guardarHorario(this.hz()!);
      if (h) {
        this.hecho.set(h);
        this.historial.update((l) => [h!, ...l].slice(0, 4));
        this.limpiar();
        this.campo().nativeElement.focus();
      }
    } catch (e) {
      this.error.set(Object.values(erroresDe(e)).flat()[0] ?? 'No se ha podido guardar.');
    } finally {
      this.guardando.set(false);
    }
  }

  private async guardarMovimiento(m: MovimientoEntrada): Promise<Hecho> {
    const creado: Movimiento = await this.gastos.guardarMovimiento(m);
    this.gastos.recargar();
    return {
      texto: `${m.tipo === 'Ingreso' ? 'Ingreso' : 'Gasto'} apuntado: ${creado.concepto} · ${euros(creado.importe)}`,
      deshacer: async () => {
        await this.gastos.borrarMovimiento(creado.id);
        if (creado.recurrenteId) await firstValueFrom(this.http.delete(`/api/gastos/recurrentes/${creado.recurrenteId}`)).catch(() => {});
        this.gastos.recargar();
      },
      ver: () => { this.cerrar(); this.router.navigate(['/gastos']).then(() => this.gastos.editandoMovimiento.set({ movimiento: creado })); },
    };
  }

  private async guardarEvento(e: EventoEntrada): Promise<Hecho> {
    const r = await this.agenda.guardar(e);
    window.dispatchEvent(new Event('mda:agenda'));
    const cuando = new Date(e.inicio).toLocaleString('es-ES', e.todoElDia ? { weekday: 'short', day: 'numeric', month: 'short' } : { weekday: 'short', day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' });
    return {
      texto: `${e.tipo === 'Recordatorio' ? 'Te lo recuerdo' : 'En la agenda'}: ${e.titulo} · ${cuando}`,
      deshacer: async () => { await firstValueFrom(this.http.delete(`/api/agenda/${r.id}`)); window.dispatchEvent(new Event('mda:agenda')); },
      ver: () => { this.cerrar(); this.router.navigate(['/agenda']); },
    };
  }

  private async guardarHucha(h: AporteHucha): Promise<Hecho> {
    await this.gastos.aportar(h.id, h.importe);
    this.gastos.recargar();
    return {
      texto: `${h.emoji} ${h.importe < 0 ? 'Sacado de' : 'Metido en'} ${h.nombre}: ${euros(Math.abs(h.importe))}`,
      deshacer: async () => { await this.gastos.aportar(h.id, -h.importe); this.gastos.recargar(); },
      ver: () => { this.cerrar(); this.router.navigate(['/gastos/huchas']); },
    };
  }

  private async guardarHorario(h: Horario): Promise<Hecho> {
    const antes = await this.agenda.horario().catch(() => null);
    await this.agenda.guardarHorario(h);
    window.dispatchEvent(new Event('mda:agenda'));
    return {
      texto: `💼 Horario guardado: ${textoHorario(h)}`,
      deshacer: async () => {
        await this.agenda.guardarHorario(antes ?? { ...h, activo: false });
        window.dispatchEvent(new Event('mda:agenda'));
      },
      ver: () => { this.cerrar(); this.router.navigate(['/agenda']); },
    };
  }

  protected async deshacer(h: Hecho): Promise<void> {
    try {
      await h.deshacer();
      h.deshecho = true;
      this.hecho.set({ ...h });
      this.historial.update((l) => l.map((x) => (x === h ? h : x)));
      this.campo().nativeElement.focus();
    } catch {
      this.error.set('No se ha podido deshacer.');
    }
  }

  // ---- voz (el dictado del navegador; en el iPhone también vale el micrófono del teclado) ----
  protected hablar(): void {
    if (this.escuchando()) { this.reconocedor?.stop(); return; }
    const R = Reconocimiento.SpeechRecognition ?? Reconocimiento.webkitSpeechRecognition;
    if (!R) return;
    const r = new R();
    r.lang = 'es-ES';
    r.interimResults = true;
    r.continuous = false;
    r.onresult = (e) => {
      const lista = Array.from(e.results);
      const t = lista.map((x) => x[0].transcript).join('');
      this.texto.set(t);
      if (lista.at(-1)?.isFinal) this.entender(t, 0);
    };
    r.onend = () => this.escuchando.set(false);
    r.onerror = (e) => {
      this.escuchando.set(false);
      if (e.error === 'not-allowed' || e.error === 'service-not-allowed') this.error.set('Dale permiso al micrófono para poder hablarme.');
    };
    this.reconocedor = r;
    this.error.set('');
    this.escuchando.set(true);
    try { r.start(); } catch { this.escuchando.set(false); }
  }
}
