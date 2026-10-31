import { Component, DestroyRef, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { Errores, erroresDe } from '../gastos/gastos.service';
import { seguirTeclado } from '../gastos/teclado';
import { MESES } from '../gastos/modelo';
import {
  AVISOS_DIA, AVISOS_HORA, AgendaService, Aparicion, COLORES_EVENTO, DIAS_SEMANA, EstadoAgenda, Horario, REPETICIONES, Repeticion, TipoEvento,
  colorEvento, diaHorario, hora, iso, mismoDia, textoHorario,
} from './agenda.service';

interface Dia { fecha: Date; delMes: boolean; hoy: boolean; eventos: Aparicion[]; }
interface Bloque { a: Aparicion; top: number; alto: number; izq: number; ancho: number; }

const PX_HORA = 48; // alto de una hora en la vista del día
const minutos = (t: string) => { const [h, m] = t.split(':').map(Number); return h * 60 + m; };
const leer = (k: string, d: string) => { try { return localStorage.getItem(k) ?? d; } catch { return d; } };

const MENSAJES: Record<string, string> = {
  ok: 'Google Calendar enlazado: sus eventos ya salen aquí.',
  cancelada: 'Has cancelado el enlace con Google.',
  caducada: 'El enlace caducó: vuelve a intentarlo.',
  error: 'Google no dejó completar el enlace. Revisa el ID y el secreto del cliente.',
  'sin-configurar': 'Falta configurar Google en el servidor (mira Seguridad › Apps conectadas).',
};

// Agenda: el mes en rejilla (lunes primero) con los eventos propios y los de Google, el día elegido por horas
// (con tu horario de trabajo de fondo) o en lista, y los recordatorios pendientes. Un evento puede repetirse y avisar por correo o WhatsApp.
@Component({
  selector: 'app-agenda',
  templateUrl: './agenda.html',
  styleUrls: ['../gastos/comun.css', '../gastos/dialogos.css', './agenda.css'],
})
export class Agenda {
  private readonly api = inject(AgendaService);
  private readonly router = inject(Router);
  private readonly dlg = viewChild.required<ElementRef<HTMLDialogElement>>('dialogo');

  protected readonly mes = signal(new Date(new Date().getFullYear(), new Date().getMonth(), 1));
  protected readonly elegido = signal(new Date());
  protected readonly apariciones = signal<Aparicion[]>([]);
  protected readonly pendientes = signal<Aparicion[]>([]);
  protected readonly estado = signal<EstadoAgenda | null>(null);
  protected readonly aviso = signal('');
  protected readonly semana = ['L', 'M', 'X', 'J', 'V', 'S', 'D'];
  protected readonly colores = COLORES_EVENTO;
  protected readonly repeticiones = REPETICIONES;
  protected readonly colorEvento = colorEvento;
  protected readonly hora = (s: string) => hora(new Date(s));

  protected readonly titulo = computed(() => `${MESES[this.mes().getMonth()]} ${this.mes().getFullYear()}`);
  protected readonly dias = computed<Dia[]>(() => {
    const m = this.mes();
    const ini = new Date(m);
    ini.setDate(1 - ((m.getDay() + 6) % 7));
    const hoy = new Date();
    return Array.from({ length: 42 }, (_, i) => {
      const f = new Date(ini.getFullYear(), ini.getMonth(), ini.getDate() + i);
      return { fecha: f, delMes: f.getMonth() === m.getMonth(), hoy: mismoDia(f, hoy), eventos: this.del(f) };
    });
  });
  protected readonly delDia = computed(() => this.del(this.elegido()));

  // ---- el día por horas, con el horario de trabajo de fondo ----
  protected readonly pxHora = PX_HORA;
  protected readonly vista = signal<'horas' | 'lista'>(leer('agenda-vista', 'horas') as 'horas' | 'lista');
  protected readonly reloj = signal(new Date());
  protected readonly horario = computed(() => this.estado()?.horario ?? null);
  protected readonly textoHorario = textoHorario;
  protected readonly trabajaHoy = computed(() => { const h = this.horario(); return !!h?.activo && h.dias.includes(diaHorario(this.elegido())); });
  protected readonly linea = computed(() => {
    const d = this.elegido();
    const inicioDia = new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();
    const eventos = this.delDia();
    const items = eventos.filter((a) => !a.todoElDia).map((a) => {
      const i = Math.max(0, (new Date(a.inicio).getTime() - inicioDia) / 60000);
      const f = Math.min(1440, Math.max(a.fin ? (new Date(a.fin).getTime() - inicioDia) / 60000 : i + 30, i + 25));
      return { a, i, f, col: 0 };
    }).sort((x, y) => x.i - y.i || y.f - x.f);
    const h = this.horario();
    const trabaja = this.trabajaHoy() && h ? { i: minutos(h.entrada), f: minutos(h.salida) } : null;
    let desde = 7 * 60, hasta = 22 * 60;
    if (trabaja) { desde = Math.min(desde, trabaja.i); hasta = Math.max(hasta, trabaja.f); }
    for (const x of items) { desde = Math.min(desde, x.i); hasta = Math.max(hasta, x.f); }
    const h0 = Math.floor(desde / 60), h1 = Math.min(24, Math.ceil(hasta / 60));
    // los que se pisan van en columnas, uno al lado del otro
    const finCol: number[] = [];
    for (const x of items) {
      let c = finCol.findIndex((fin) => fin <= x.i);
      if (c < 0) { c = finCol.length; finCol.push(x.f); } else finCol[c] = x.f;
      x.col = c;
    }
    const y = (min: number) => ((min - h0 * 60) / 60) * PX_HORA;
    const bloques: Bloque[] = items.map((x) => {
      const n = Math.max(...items.filter((o) => o.i < x.f && o.f > x.i).map((o) => o.col)) + 1;
      return { a: x.a, top: y(x.i), alto: Math.max(22, y(x.f) - y(x.i)), izq: (x.col / n) * 100, ancho: 100 / n };
    });
    const ahora = this.reloj();
    const minAhora = ahora.getHours() * 60 + ahora.getMinutes();
    return {
      h0, alto: (h1 - h0) * PX_HORA, horas: Array.from({ length: h1 - h0 }, (_, k) => h0 + k), bloques,
      todoDia: eventos.filter((a) => a.todoElDia),
      trabajo: trabaja && h ? { top: y(trabaja.i), alto: y(trabaja.f) - y(trabaja.i), texto: h.nombre + ' · ' + h.entrada.replace(/^0/, '') + '–' + h.salida.replace(/^0/, '') } : null,
      ahora: mismoDia(d, ahora) && minAhora >= h0 * 60 && minAhora <= h1 * 60 ? y(minAhora) : null,
    };
  });

  protected ponerVista(v: 'horas' | 'lista'): void {
    this.vista.set(v);
    try { localStorage.setItem('agenda-vista', v); } catch { /* sin almacenamiento */ }
  }

  // pulsar un hueco del día crea un evento a esa hora (redondeada a la media hora)
  protected nuevoEn(e: MouseEvent, h0: number): void {
    const t = e.target as HTMLElement;
    if (t !== e.currentTarget && !t.classList.contains('linea__hora') && !t.classList.contains('linea__trabajo')) return;
    const caja = (e.currentTarget as HTMLElement).getBoundingClientRect();
    const min = h0 * 60 + Math.floor((((e.clientY - caja.top) / PX_HORA) * 60) / 30) * 30;
    this.nuevo('Evento', this.elegido(), Math.min(23 * 60 + 30, min));
  }

  // ---- mi horario ----
  private readonly dlgHorario = viewChild.required<ElementRef<HTMLDialogElement>>('dialogoHorario');
  protected readonly diasSemana = DIAS_SEMANA;
  protected readonly hz = signal<Horario>({ activo: true, nombre: 'Trabajo', dias: [1, 2, 3, 4, 5], entrada: '08:30', salida: '17:30' });
  protected readonly erroresHorario = signal<string[]>([]);

  protected abrirHorario(): void {
    this.hz.set(this.horario() ? { ...this.horario()! } : { activo: true, nombre: 'Trabajo', dias: [1, 2, 3, 4, 5], entrada: '08:30', salida: '17:30' });
    this.erroresHorario.set([]);
    this.dlgHorario().nativeElement.showModal();
  }

  protected alternarDia(n: number): void {
    this.hz.update((h) => ({ ...h, dias: h.dias.includes(n) ? h.dias.filter((x) => x !== n) : [...h.dias, n].sort() }));
  }

  protected async guardarHorario(e: Event): Promise<void> {
    e.preventDefault();
    try {
      const h = await this.api.guardarHorario(this.hz());
      this.estado.update((s) => (s ? { ...s, horario: h } : s));
      this.dlgHorario().nativeElement.close();
      this.aviso.set(h.activo ? 'Horario guardado: ' + textoHorario(h) + '. Te aviso si una cita cae dentro.' : 'Horario desactivado.');
    } catch (err) {
      this.erroresHorario.set(Object.values(erroresDe(err)).flat());
    }
  }
  protected readonly recordatorios = computed(() => this.pendientes().filter((a) => a.tipo === 'Recordatorio'));

  // ---- diálogo ----
  protected readonly editando = signal<Aparicion | null>(null);
  protected readonly tipo = signal<TipoEvento>('Evento');
  protected readonly tituloEv = signal('');
  protected readonly fecha = signal(iso(new Date()));
  protected readonly horaIni = signal('10:00');
  protected readonly horaFin = signal('11:00');
  protected readonly todoElDia = signal(false);
  protected readonly lugar = signal('');
  protected readonly notas = signal('');
  protected readonly color = signal('');
  protected readonly repetir = signal<Repeticion>('No');
  protected readonly hasta = signal('');
  protected readonly avisoMin = signal<number | null>(null);
  protected readonly porCorreo = signal(true);
  protected readonly porWhatsApp = signal(false);
  protected readonly porTelegram = signal(false);
  protected readonly porPush = signal(false);
  protected readonly enGoogle = signal(false);
  protected readonly errores = signal<Errores>({});
  protected readonly erroresLista = computed(() => Object.values(this.errores()).flat());
  protected readonly guardando = signal(false);
  protected readonly confirmar = signal(false);
  protected readonly avisos = computed(() => (this.todoElDia() ? AVISOS_DIA : AVISOS_HORA));

  constructor() {
    const q = inject(ActivatedRoute).snapshot.queryParamMap;
    if (q.get('conexion')) { this.aviso.set(MENSAJES[q.get('conexion')!] ?? ''); this.router.navigate([], { queryParams: {}, replaceUrl: true }); }
    if (q.get('nuevo')) setTimeout(() => this.nuevo(q.get('nuevo') === 'recordatorio' ? 'Recordatorio' : 'Evento'));
    this.cargar();
    this.api.estado().then((e) => this.estado.set(e)).catch(() => {});
    // lo que apunta el asistente desde cualquier pantalla
    const recargar = () => { this.cargar(); this.api.estado().then((e) => this.estado.set(e)).catch(() => {}); };
    window.addEventListener('mda:agenda', recargar);
    // la raya de «ahora» en la vista del día
    const t = setInterval(() => this.reloj.set(new Date()), 60_000);
    inject(DestroyRef).onDestroy(() => { window.removeEventListener('mda:agenda', recargar); clearInterval(t); });
  }

  private del(f: Date): Aparicion[] {
    const ini = new Date(f.getFullYear(), f.getMonth(), f.getDate()), fin = new Date(ini.getFullYear(), ini.getMonth(), ini.getDate() + 1);
    return this.apariciones().filter((a) => {
      const i = new Date(a.inicio), e = a.fin ? new Date(a.fin) : i;
      return i < fin && (a.todoElDia ? e >= ini : e > ini || mismoDia(i, ini));
    });
  }

  private async cargar(): Promise<void> {
    const d = this.dias();
    const desde = d[0].fecha, hasta = new Date(d[41].fecha.getFullYear(), d[41].fecha.getMonth(), d[41].fecha.getDate() + 1);
    try {
      const [r, p] = await Promise.all([this.api.rango(desde, hasta), this.api.proximos(30)]);
      this.apariciones.set(r.apariciones);
      this.pendientes.set(p);
    } catch { /* sin conexión: se queda lo que había */ }
  }

  protected moverMes(n: number): void {
    const m = this.mes();
    this.mes.set(new Date(m.getFullYear(), m.getMonth() + n, 1));
    this.cargar();
  }

  protected hoy(): void {
    const h = new Date();
    this.mes.set(new Date(h.getFullYear(), h.getMonth(), 1));
    this.elegido.set(h);
    this.cargar();
  }

  protected elegir(d: Dia): void {
    if (!d.delMes) { this.mes.set(new Date(d.fecha.getFullYear(), d.fecha.getMonth(), 1)); this.cargar(); }
    this.elegido.set(d.fecha);
  }

  protected esElegido(d: Dia): boolean { return mismoDia(d.fecha, this.elegido()); }
  protected diaLargo(f: Date): string { return f.toLocaleDateString('es-ES', { weekday: 'long', day: 'numeric', month: 'long' }); }
  protected cuando(a: Aparicion): string {
    const i = new Date(a.inicio), hoy = new Date(), man = new Date(hoy.getFullYear(), hoy.getMonth(), hoy.getDate() + 1);
    const dia = mismoDia(i, hoy) ? 'Hoy' : mismoDia(i, man) ? 'Mañana' : i < hoy ? `${i.toLocaleDateString('es-ES', { day: 'numeric', month: 'short' })} (atrasado)` : i.toLocaleDateString('es-ES', { weekday: 'short', day: 'numeric', month: 'short' });
    return a.todoElDia ? dia : `${dia}, ${hora(i)}`;
  }

  protected async marcar(a: Aparicion, e?: Event): Promise<void> {
    e?.stopPropagation();
    if (!a.id) return;
    await this.api.hecho(a.id, !a.hecho);
    this.cargar();
  }

  // ---- crear y editar ----
  protected nuevo(tipo: TipoEvento = 'Evento', dia?: Date, enMinuto?: number): void {
    const f = dia ?? this.elegido();
    const ahora = new Date();
    const h = enMinuto != null ? Math.floor(enMinuto / 60) : mismoDia(f, ahora) ? Math.min(23, ahora.getHours() + 1) : 10;
    const m = String(enMinuto != null ? enMinuto % 60 : 0).padStart(2, '0');
    this.editando.set(null);
    this.tipo.set(tipo);
    this.tituloEv.set('');
    this.fecha.set(iso(f));
    this.horaIni.set(`${String(h).padStart(2, '0')}:${m}`);
    this.horaFin.set(`${String((h + 1) % 24).padStart(2, '0')}:${m}`);
    this.todoElDia.set(false);
    this.lugar.set('');
    this.notas.set('');
    this.color.set('');
    this.repetir.set('No');
    this.hasta.set('');
    this.avisoMin.set(tipo === 'Recordatorio' ? 0 : 30);
    this.porCorreo.set(!!this.estado()?.canales.correo);
    this.porWhatsApp.set(tipo === 'Recordatorio' && !!this.estado()?.canales.whatsApp);
    this.porTelegram.set(!!this.estado()?.canales.telegram);
    this.porPush.set(true);
    this.enGoogle.set(!!this.estado()?.google.conectado && tipo === 'Evento');
    this.abrirDialogo();
  }

  protected abrir(a: Aparicion): void {
    if (a.origen === 'google') { if (a.url) window.open(a.url, '_blank', 'noopener'); return; }
    const i = new Date(a.inicio), f = a.fin ? new Date(a.fin) : null;
    this.editando.set(a);
    this.tipo.set(a.tipo);
    this.tituloEv.set(a.titulo);
    this.fecha.set(iso(i));
    this.horaIni.set(hora(i));
    this.horaFin.set(f ? hora(f) : hora(new Date(i.getTime() + 3600_000)));
    this.todoElDia.set(a.todoElDia);
    this.lugar.set(a.lugar ?? '');
    this.notas.set(a.notas ?? '');
    this.color.set(a.color);
    this.repetir.set(a.repetir);
    this.hasta.set('');
    this.avisoMin.set(a.avisoMinutos);
    this.porCorreo.set(a.porCorreo);
    this.porWhatsApp.set(a.porWhatsApp);
    this.porTelegram.set(!!a.porTelegram);
    this.porPush.set(a.porPush);
    this.enGoogle.set(a.enGoogle);
    this.abrirDialogo();
  }

  private soltar?: () => void;
  private abrirDialogo(): void {
    this.errores.set({});
    this.confirmar.set(false);
    const d = this.dlg().nativeElement;
    d.showModal();
    this.soltar?.();
    this.soltar = seguirTeclado(d);
  }

  protected ponerTodoElDia(v: boolean): void {
    this.todoElDia.set(v);
    // el aviso elegido tiene que existir en la lista nueva
    if (this.avisoMin() != null) this.avisoMin.set(v ? -540 : 30);
  }

  protected ponerAviso(v: string): void { this.avisoMin.set(v === '' ? null : Number(v)); }
  protected valor(e: Event): string { return (e.target as HTMLInputElement).value; }
  protected marcado(e: Event): boolean { return (e.target as HTMLInputElement).checked; }

  protected async guardar(e: Event): Promise<void> {
    e.preventDefault();
    const [a, m, d] = this.fecha().split('-').map(Number);
    const [hi, mi] = this.horaIni().split(':').map(Number);
    const [hf, mf] = this.horaFin().split(':').map(Number);
    const ini = this.todoElDia() ? new Date(a, m - 1, d) : new Date(a, m - 1, d, hi || 0, mi || 0);
    let fin: Date | null = this.tipo() === 'Recordatorio' || this.todoElDia() ? null : new Date(a, m - 1, d, hf || 0, mf || 0);
    if (fin && fin < ini) fin = new Date(fin.getTime() + 86400_000); // acaba de madrugada al día siguiente
    this.guardando.set(true);
    try {
      const r = await this.api.guardar({
        tipo: this.tipo(), titulo: this.tituloEv().trim(), notas: this.notas() || null, lugar: this.lugar() || null,
        inicio: ini.toISOString(), fin: fin?.toISOString() ?? null, todoElDia: this.todoElDia(), color: this.color(),
        repetir: this.repetir(), repetirHasta: this.repetir() !== 'No' && this.hasta() ? this.hasta() : null,
        avisoMinutos: this.avisoMin(), porCorreo: this.porCorreo(), porWhatsApp: this.porWhatsApp(), porTelegram: this.porTelegram(), porPush: this.porPush(), enGoogle: this.enGoogle(),
        hecho: this.editando()?.hecho ?? false,
      }, this.editando()?.id ?? undefined);
      this.aviso.set(r.googleError ?? '');
      this.dlg().nativeElement.close();
      this.elegido.set(ini);
      if (ini.getMonth() !== this.mes().getMonth() || ini.getFullYear() !== this.mes().getFullYear()) this.mes.set(new Date(ini.getFullYear(), ini.getMonth(), 1));
      this.cargar();
    } catch (err) { this.errores.set(erroresDe(err)); } finally { this.guardando.set(false); }
  }

  protected async borrar(): Promise<void> {
    if (!this.confirmar()) { this.confirmar.set(true); return; }
    await this.api.borrar(this.editando()!.id!);
    this.dlg().nativeElement.close();
    this.cargar();
  }

  protected conectarGoogle(): void { location.href = '/api/conexiones/google/conectar'; }
  protected alCerrar(): void { this.soltar?.(); this.soltar = undefined; }
  protected fuera(e: MouseEvent): void { if (e.target === this.dlg().nativeElement) this.dlg().nativeElement.close(); }
}
