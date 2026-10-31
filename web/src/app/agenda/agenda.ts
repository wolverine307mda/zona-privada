import { Component, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { Errores, erroresDe } from '../gastos/gastos.service';
import { seguirTeclado } from '../gastos/teclado';
import { MESES } from '../gastos/modelo';
import {
  AVISOS_DIA, AVISOS_HORA, AgendaService, Aparicion, COLORES_EVENTO, EstadoAgenda, REPETICIONES, Repeticion, TipoEvento,
  colorEvento, hora, iso, mismoDia,
} from './agenda.service';

interface Dia { fecha: Date; delMes: boolean; hoy: boolean; eventos: Aparicion[]; }

const MENSAJES: Record<string, string> = {
  ok: 'Google Calendar enlazado: sus eventos ya salen aquí.',
  cancelada: 'Has cancelado el enlace con Google.',
  caducada: 'El enlace caducó: vuelve a intentarlo.',
  error: 'Google no dejó completar el enlace. Revisa el ID y el secreto del cliente.',
  'sin-configurar': 'Falta configurar Google en el servidor (mira Seguridad › Apps conectadas).',
};

// Agenda: el mes en rejilla (lunes primero) con los eventos propios y los de Google, el día elegido en lista
// y los recordatorios pendientes. Un evento puede repetirse y avisar por correo o WhatsApp.
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
  protected nuevo(tipo: TipoEvento = 'Evento', dia?: Date): void {
    const f = dia ?? this.elegido();
    const ahora = new Date();
    const h = mismoDia(f, ahora) ? Math.min(23, ahora.getHours() + 1) : 10;
    this.editando.set(null);
    this.tipo.set(tipo);
    this.tituloEv.set('');
    this.fecha.set(iso(f));
    this.horaIni.set(`${String(h).padStart(2, '0')}:00`);
    this.horaFin.set(`${String((h + 1) % 24).padStart(2, '0')}:00`);
    this.todoElDia.set(false);
    this.lugar.set('');
    this.notas.set('');
    this.color.set('');
    this.repetir.set('No');
    this.hasta.set('');
    this.avisoMin.set(tipo === 'Recordatorio' ? 0 : 30);
    this.porCorreo.set(!!this.estado()?.canales.correo);
    this.porWhatsApp.set(tipo === 'Recordatorio' && !!this.estado()?.canales.whatsApp);
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
        avisoMinutos: this.avisoMin(), porCorreo: this.porCorreo(), porWhatsApp: this.porWhatsApp(), porPush: this.porPush(), enGoogle: this.enGoogle(),
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
