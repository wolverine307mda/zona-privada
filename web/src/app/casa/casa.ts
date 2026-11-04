import { Component, DestroyRef, ElementRef, OnInit, computed, inject, signal, viewChild } from '@angular/core';
import { CasaService } from './casa.service';
import { AparatoCasa, DispositivoTuya, EscenaCasa, PANEL_VACIO, PanelGuardado, alternarOculto, aparatosDe, encendido, leerPanel, mover, numero } from './modelo';
import { ReglasCasa } from './reglas-casa';
import { TarjetaAparato } from './tarjeta-aparato';
import { EstadoError, EstadoVacio } from '../ui';

type Pestana = 'todo' | number;
const CLAVE_PESTANA = 'mda.casa.pestana';

// Tu casa: habitaciones con sus aparatos en tarjetas que puedes ordenar, ocultar y cambiar de sitio. Cada tarjeta es un aparato
// (termómetro, lámpara, aire, tele…) que responde a un toque; lo que se ve se actualiza solo cada 15 segundos.
@Component({
  selector: 'app-casa',
  imports: [TarjetaAparato, ReglasCasa, EstadoVacio, EstadoError],
  templateUrl: './casa.html',
  styleUrls: ['./casa.css'],
})
export class Casa implements OnInit {
  protected readonly casa = inject(CasaService);
  private readonly dlgAparato = viewChild<ElementRef<HTMLDialogElement>>('dlgAparato');
  private readonly dlgAjustes = viewChild<ElementRef<HTMLDialogElement>>('dlgAjustes');
  private readonly dlgEstancia = viewChild<ElementRef<HTMLDialogElement>>('dlgEstancia');
  private readonly dlgEscena = viewChild<ElementRef<HTMLDialogElement>>('dlgEscena');

  protected readonly estado = this.casa.estado;
  protected readonly editando = signal(false);
  protected readonly vista = signal<'panel' | 'reglas'>('panel');
  protected readonly pestana = signal<Pestana>('todo');
  protected readonly panel = signal<PanelGuardado>(PANEL_VACIO);
  protected readonly ocupado = signal(false);
  protected readonly aviso = signal('');
  protected readonly escenas = signal<EscenaCasa[]>([]);
  protected readonly mensaje = signal('');
  protected readonly escNombre = signal('');
  protected readonly escIcono = signal('✨');
  protected readonly aplicando = signal<number | null>(null);
  protected readonly quitandoEscena = signal<number | null>(null);

  // formularios de los diálogos
  protected readonly nuevoNombre = signal('');
  protected readonly nuevoTipo = signal('luz');
  protected readonly nuevoDriver = signal('demo');
  protected readonly nuevaEstancia = signal<number | null>(null);
  protected readonly tuyaId = signal('');
  protected readonly tuyaIr = signal('');
  protected readonly tuyaRemoto = signal('');
  protected readonly tuyaLista = signal<DispositivoTuya[] | null>(null);
  protected readonly buscandoTuya = signal(false);
  protected readonly estNombre = signal('');
  protected readonly estIcono = signal('🏠');
  protected readonly ajustando = signal<AparatoCasa | null>(null);
  protected readonly ajNombre = signal('');
  protected readonly ajEstancia = signal<number | null>(null);
  protected readonly confirmarBorrar = signal(false);
  protected readonly confirmarEstancia = signal(false);

  protected readonly aparatos = computed(() => this.estado()?.aparatos ?? []);
  protected readonly visibles = computed(() => aparatosDe(this.aparatos(), this.pestana(), this.panel(), this.editando()));
  protected readonly encendidos = computed(() => this.aparatos().filter((a) => ['luz', 'enchufe', 'clima', 'tv'].includes(a.tipo) && encendido(a)).length);
  protected readonly temperatura = computed(() => {
    const t = this.aparatos().filter((a) => a.tipo === 'sensor').map((a) => numero(a, 'temperatura')).filter((n): n is number => n !== null);
    return t.length ? t.reduce((x, y) => x + y, 0) / t.length : null;
  });
  protected readonly puertaAbierta = computed(() => this.aparatos().some((a) => a.tipo === 'puerta' && a.entidades.some((e) => e.clave === 'abierto' && e.valor === 'true')));
  protected readonly tipos = computed(() => this.casa.catalogo()?.tipos ?? []);
  protected readonly drivers = computed(() => this.casa.catalogo()?.drivers ?? ['demo']);
  protected readonly saludo = (() => { const h = new Date().getHours(); return h < 6 ? 'Buenas noches' : h < 13 ? 'Buenos días' : h < 21 ? 'Buenas tardes' : 'Buenas noches'; })();

  constructor() {
    const t = setInterval(() => { if (!document.hidden && !this.editando()) void this.casa.cargar(); }, 15000);
    inject(DestroyRef).onDestroy(() => clearInterval(t));
    try {
      const g = localStorage.getItem(CLAVE_PESTANA);
      if (g && g !== 'todo' && Number.isInteger(Number(g))) this.pestana.set(Number(g));
    } catch { /* sin almacenamiento */ }
  }

  protected reintentar(): void { void this.casa.cargar(); }

  async ngOnInit(): Promise<void> {
    await this.casa.cargar();
    void this.cargarEscenas();
    this.panel.set(leerPanel(this.estado()?.panel ?? null));
    // si la habitación recordada ya no existe, se vuelve a «Todo»
    const p = this.pestana();
    if (p !== 'todo' && !this.estado()?.estancias.some((e) => e.id === p)) this.pestana.set('todo');
  }

  private async cargarEscenas(): Promise<void> {
    try { this.escenas.set(await this.casa.escenas()); } catch { /* sin escenas */ }
  }

  // un toque y los aparatos vuelven a como estaban al guardarla
  protected async aplicarEscena(e: EscenaCasa): Promise<void> {
    this.aplicando.set(e.id);
    try {
      const r = await this.casa.aplicarEscena(e.id);
      this.mensaje.set(r.fallos.length ? `«${e.nombre}»: ${r.hechas} hechas, ${r.fallos.length} sin conexión (${r.fallos[0]}).` : `«${e.nombre}» puesta.`);
      await this.casa.cargar();
    } catch { this.mensaje.set('No se pudo poner la escena.'); } finally { this.aplicando.set(null); }
  }

  protected abrirEscena(): void {
    this.escNombre.set(''); this.escIcono.set('✨'); this.aviso.set('');
    this.dlgEscena()?.nativeElement.showModal();
  }

  // guarda cómo están ahora los aparatos de esta pestaña (o todos en «Todo»)
  protected async crearEscena(e: Event): Promise<void> {
    e.preventDefault();
    if (!this.escNombre().trim()) { this.aviso.set('Ponle un nombre.'); return; }
    this.ocupado.set(true);
    try {
      const ids = this.pestana() === 'todo' ? null : this.aparatos().filter((a) => a.estanciaId === this.pestana()).map((a) => a.id);
      await this.casa.guardarEscena(this.escNombre().trim(), this.escIcono().trim() || '✨', ids);
      await this.cargarEscenas();
      this.dlgEscena()?.nativeElement.close();
    } catch (x) {
      const errores = (x as { error?: { errors?: Record<string, string[]> } }).error?.errors;
      this.aviso.set(errores ? Object.values(errores)[0]?.[0] ?? 'Revisa los datos.' : 'No se pudo guardar la escena.');
    } finally { this.ocupado.set(false); }
  }

  protected async borrarEscena(e: EscenaCasa): Promise<void> {
    if (this.quitandoEscena() !== e.id) { this.quitandoEscena.set(e.id); return; }
    try { await this.casa.borrarEscena(e.id); this.quitandoEscena.set(null); await this.cargarEscenas(); } catch { this.mensaje.set('No se pudo quitar la escena.'); }
  }

  protected ponerPestana(p: Pestana): void {
    this.pestana.set(p);
    this.confirmarEstancia.set(false);
    try { localStorage.setItem(CLAVE_PESTANA, String(p)); } catch { /* sin almacenamiento */ }
  }

  protected nombreEstancia(id: number | null): string { return this.estado()?.estancias.find((e) => e.id === id)?.nombre ?? 'Sin habitación'; }
  protected esOculto(a: AparatoCasa): boolean { return this.panel().ocultos.includes(a.id); }

  // ---- orden y visibilidad (se guardan en el servidor) ----
  protected async guardar(p: PanelGuardado): Promise<void> {
    this.panel.set(p);
    try { await this.casa.guardarPanel(p); } catch { this.aviso.set('No se pudo guardar la colocación.'); }
  }
  protected subir(a: AparatoCasa): void { void this.guardar(mover(this.aparatos(), this.pestana(), this.panel(), a.id, -1)); }
  protected bajar(a: AparatoCasa): void { void this.guardar(mover(this.aparatos(), this.pestana(), this.panel(), a.id, 1)); }
  protected ocultar(a: AparatoCasa): void { void this.guardar(alternarOculto(this.panel(), a.id)); }

  // ---- diálogos ----
  protected async abrirAparato(): Promise<void> {
    await this.casa.cargarCatalogo();
    this.nuevoNombre.set('');
    this.nuevoTipo.set('luz');
    this.nuevoDriver.set('demo');
    this.nuevaEstancia.set(typeof this.pestana() === 'number' ? (this.pestana() as number) : null);
    this.tuyaId.set(''); this.tuyaIr.set(''); this.tuyaRemoto.set(''); this.tuyaLista.set(null);
    this.aviso.set('');
    this.dlgAparato()?.nativeElement.showModal();
  }

  protected async crearAparato(e: Event): Promise<void> {
    e.preventDefault();
    if (!this.nuevoNombre().trim()) { this.aviso.set('Ponle un nombre.'); return; }
    this.ocupado.set(true);
    try {
      await this.casa.crearAparato(this.nuevoNombre().trim(), this.nuevoTipo(), this.nuevoDriver(), this.nuevaEstancia(),
        this.nuevoDriver() === 'tuya' ? { deviceId: this.tuyaId(), infraredId: this.tuyaIr(), remoteId: this.tuyaRemoto() } : undefined);
      await this.casa.cargar();
      this.dlgAparato()?.nativeElement.close();
    } catch { this.aviso.set('No se pudo añadir el aparato.'); } finally { this.ocupado.set(false); }
  }

  // los aparatos de tu cuenta de Tuya: se elige uno y rellena el nombre, el tipo y el identificador
  protected async buscarTuya(): Promise<void> {
    this.buscandoTuya.set(true);
    this.aviso.set('');
    try { this.tuyaLista.set(await this.casa.tuyaDispositivos()); }
    catch (e) {
      const d = (e as { error?: { detalle?: string } }).error?.detalle;
      this.aviso.set(d ?? 'No se pudo leer tu cuenta de Tuya. Revisa las claves en Seguridad → Claves.');
    } finally { this.buscandoTuya.set(false); }
  }
  protected elegirTuya(d: DispositivoTuya): void {
    this.tuyaId.set(d.id);
    if (!this.nuevoNombre().trim()) this.nuevoNombre.set(d.nombre);
    if (d.tipoSugerido) this.nuevoTipo.set(d.tipoSugerido);
    this.tuyaLista.set(null);
  }

  protected abrirAjustes(a: AparatoCasa): void {
    this.ajustando.set(a);
    this.ajNombre.set(a.nombre);
    this.ajEstancia.set(a.estanciaId);
    this.confirmarBorrar.set(false);
    this.aviso.set('');
    this.dlgAjustes()?.nativeElement.showModal();
  }

  protected async guardarAjustes(e: Event): Promise<void> {
    e.preventDefault();
    const a = this.ajustando();
    if (!a || !this.ajNombre().trim()) { this.aviso.set('Ponle un nombre.'); return; }
    this.ocupado.set(true);
    try {
      await this.casa.editarAparato(a.id, this.ajNombre().trim(), this.ajEstancia());
      await this.casa.cargar();
      this.dlgAjustes()?.nativeElement.close();
    } catch { this.aviso.set('No se pudo guardar.'); } finally { this.ocupado.set(false); }
  }

  protected async borrarAparato(): Promise<void> {
    const a = this.ajustando();
    if (!a) return;
    if (!this.confirmarBorrar()) { this.confirmarBorrar.set(true); return; }
    this.ocupado.set(true);
    try {
      await this.casa.borrarAparato(a.id);
      await this.casa.cargar();
      this.dlgAjustes()?.nativeElement.close();
    } catch { this.aviso.set('No se pudo quitar.'); } finally { this.ocupado.set(false); }
  }

  protected abrirEstancia(): void {
    this.estNombre.set('');
    this.estIcono.set('🏠');
    this.aviso.set('');
    this.dlgEstancia()?.nativeElement.showModal();
  }

  protected async crearEstancia(e: Event): Promise<void> {
    e.preventDefault();
    if (!this.estNombre().trim()) { this.aviso.set('Ponle un nombre.'); return; }
    this.ocupado.set(true);
    try {
      await this.casa.crearEstancia(this.estNombre().trim(), this.estIcono().trim() || '🏠');
      await this.casa.cargar();
      this.dlgEstancia()?.nativeElement.close();
    } catch { this.aviso.set('No se pudo crear la habitación.'); } finally { this.ocupado.set(false); }
  }

  protected async borrarEstancia(): Promise<void> {
    const p = this.pestana();
    if (p === 'todo') return;
    if (!this.confirmarEstancia()) { this.confirmarEstancia.set(true); return; }
    try {
      await this.casa.borrarEstancia(p);
      this.ponerPestana('todo');
      await this.casa.cargar();
    } catch { this.aviso.set('No se pudo quitar la habitación.'); }
  }

  protected async ejemplo(): Promise<void> {
    this.ocupado.set(true);
    try { await this.casa.cargarEjemplo(); await this.casa.cargar(); } catch { this.aviso.set('No se pudo cargar el ejemplo.'); } finally { this.ocupado.set(false); }
  }

  protected valor(e: Event): string { return (e.target as HTMLInputElement).value; }
  protected numeroOrNull(e: Event): number | null { const v = (e.target as HTMLSelectElement).value; return v === '' ? null : Number(v); }
}
