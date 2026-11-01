import { RecargaService } from '../core/recarga';
import { Component, DestroyRef, ElementRef, OnDestroy, computed, effect, inject, signal, untracked, viewChild } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { DecimalPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router } from '@angular/router';
import { map } from 'rxjs';
import { seguirTeclado } from '../gastos/teclado';
import {
  ArchivosService, Elemento, EnPapelera, ICONO_PAPELERA, IMAGEN_WEB, RAICES, Raiz, TIPOS, TipoArchivo,
  extension, fecha, seVe, tamano,
} from './archivos.service';

type Orden = 'nombre' | 'fecha' | 'tamano';
type Filtro = 'todo' | 'carpeta' | 'imagen' | 'audio' | 'video' | 'docs';

const FILTROS: { id: Filtro; nombre: string }[] = [
  { id: 'todo', nombre: 'Todo' }, { id: 'carpeta', nombre: 'Carpetas' }, { id: 'imagen', nombre: 'Imágenes' },
  { id: 'audio', nombre: 'Música' }, { id: 'video', nombre: 'Vídeos' }, { id: 'docs', nombre: 'Documentos' },
];
const DOCS: TipoArchivo[] = ['pdf', 'texto', 'hoja', 'presentacion'];

// Documentos: las carpetas del equipo de casa (documentos, escritorio, descargas, imágenes, música y vídeos).
// Navegar, buscar, ordenar, ver fotos, escuchar música, ver vídeos, subir (también arrastrando),
// descargar, crear carpetas, renombrar, mover (arrastrando a una carpeta) y borrar a una papelera.
// La carpeta abierta va en la dirección (?raiz=&ruta=), así que «atrás» funciona como en el explorador.
@Component({
  selector: 'app-documentos',
  imports: [DecimalPipe],
  templateUrl: './documentos.html',
  styleUrls: ['../gastos/comun.css', './documentos.css'],
  host: {
    '(dragenter)': 'arrastrando($event)', '(dragover)': 'arrastrando($event)',
    '(dragleave)': 'salirArrastre($event)', '(drop)': 'soltar($event)',
    '(document:keydown)': 'tecla($event)',
  },
})
export class Documentos implements OnDestroy {
  protected readonly api = inject(ArchivosService);
  private readonly ruta = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly elegir = viewChild.required<ElementRef<HTMLInputElement>>('elegir');
  private readonly dlgNombre = viewChild.required<ElementRef<HTMLDialogElement>>('dlgNombre');
  private readonly dlgVer = viewChild.required<ElementRef<HTMLDialogElement>>('dlgVer');
  private readonly dlgMover = viewChild.required<ElementRef<HTMLDialogElement>>('dlgMover');
  private readonly reproductor = viewChild<ElementRef<HTMLAudioElement>>('reproductor');
  private soltarTeclado?: () => void;

  private readonly query = toSignal(this.ruta.queryParamMap.pipe(map((q) => ({ raiz: q.get('raiz'), ruta: q.get('ruta') ?? '' }))), { requireSync: true });
  protected readonly raices = signal<Raiz[] | null>(null);
  protected readonly raiz = computed(() => {
    const r = this.query().raiz;
    return r === 'papelera' ? 'papelera' : this.raices()?.find((x) => x.id === r)?.id ?? this.raices()?.[0]?.id ?? null;
  });
  protected readonly carpeta = computed(() => this.query().ruta.replace(/^\/+|\/+$/g, ''));
  protected readonly migas = computed(() => {
    const partes = this.carpeta() ? this.carpeta().split('/') : [];
    return partes.map((nombre, i) => ({ nombre, ruta: partes.slice(0, i + 1).join('/') }));
  });

  protected readonly elementos = signal<Elemento[]>([]);
  protected readonly papelera = signal<EnPapelera[]>([]);
  protected readonly cargando = signal(true);
  protected readonly error = signal<string | null>(null);
  private readonly version = signal(0);

  // búsqueda, filtro, orden y vista (estas dos se recuerdan en el navegador)
  protected readonly q = signal('');
  protected readonly resultados = signal<Elemento[] | null>(null);
  protected readonly filtro = signal<Filtro>('todo');
  protected readonly orden = signal<Orden>(this.leer('docs.orden', 'nombre') as Orden);
  protected readonly vista = signal<'rejilla' | 'lista'>(this.leer('docs.vista', 'rejilla') as 'rejilla' | 'lista');
  protected readonly filtros = FILTROS;

  protected readonly visibles = computed(() => {
    const f = this.filtro(), o = this.orden();
    const lista = (this.resultados() ?? this.elementos()).filter((e) =>
      f === 'todo' ? true : f === 'docs' ? DOCS.includes(e.tipo) : e.tipo === f);
    const cmp = (a: Elemento, b: Elemento) =>
      o === 'fecha' ? b.modificado.localeCompare(a.modificado) : o === 'tamano' ? b.tamano - a.tamano : a.nombre.localeCompare(b.nombre, 'es', { numeric: true });
    return [...lista].sort((a, b) => Number(b.carpeta) - Number(a.carpeta) || cmp(a, b));
  });
  // una carpeta con miles de archivos no pinta todo de golpe: de 200 en 200
  protected readonly limite = signal(200);
  protected readonly mostrados = computed(() => this.visibles().slice(0, this.limite()));
  protected readonly totales = computed(() => {
    const l = this.elementos();
    return { carpetas: l.filter((e) => e.carpeta).length, archivos: l.filter((e) => !e.carpeta).length, peso: l.reduce((s, e) => s + e.tamano, 0) };
  });
  protected readonly imagenes = computed(() => this.visibles().filter((e) => IMAGEN_WEB.test(e.nombre)));
  protected readonly canciones = computed(() => this.visibles().filter((e) => e.tipo === 'audio'));

  // menús, diálogos y visor
  protected readonly menu = signal<string | null>(null);
  protected readonly confirmar = signal<string | null>(null);
  protected readonly nombreDlg = signal<{ modo: 'carpeta' | 'renombrar'; elemento?: Elemento; valor: string; error?: string } | null>(null);
  protected readonly viendo = signal<Elemento | null>(null);
  protected readonly zoom = signal(1);
  protected readonly posicion = computed(() => {
    const v = this.viendo(), l = this.imagenes();
    const i = v ? l.findIndex((x) => x.ruta === v.ruta) : -1;
    return i < 0 ? '' : `${i + 1} de ${l.length}`;
  });
  // «Mover a…»: el mismo cambio de carpeta que arrastrando, pero con teclado y en el móvil
  protected readonly moverDlg = signal<{ elemento: Elemento; ruta: string; carpetas: Elemento[]; cargando: boolean; error?: string } | null>(null);
  protected readonly sonando = signal<Elemento | null>(null);
  protected readonly soltando = signal(false);
  protected readonly moviendo = signal<string | null>(null);
  protected readonly aviso = signal<string | null>(null);
  private toques = 0;
  private avisoT?: ReturnType<typeof setTimeout>;
  private buscarT?: ReturnType<typeof setTimeout>;

  protected readonly TIPOS = TIPOS;
  protected readonly RAICES = RAICES;
  protected readonly ICONO_PAPELERA = ICONO_PAPELERA;
  protected readonly tamano = tamano;
  protected readonly fecha = fecha;
  protected readonly extension = extension;
  protected readonly esImagen = (e: Elemento) => IMAGEN_WEB.test(e.nombre);

  constructor() {
    inject(RecargaService).alRecargar(() => this.recargar(), inject(DestroyRef));
    this.api.raices().then((r) => this.raices.set(r)).catch(() => { this.raices.set([]); this.error.set('No se pudo leer las carpetas del equipo.'); });

    // cargar la carpeta abierta (o la papelera) cuando cambia la dirección o algo se modifica
    effect(() => {
      const raiz = this.raiz(), ruta = this.carpeta();
      this.version();
      if (!raiz) { if (this.raices()) this.cargando.set(false); return; }
      untracked(() => this.cargar(raiz, ruta));
    });
    // al cambiar de carpeta se limpia la búsqueda
    effect(() => { this.raiz(); this.carpeta(); untracked(() => { this.q.set(''); this.resultados.set(null); this.menu.set(null); }); });
    effect(() => this.guardar('docs.orden', this.orden()));
    effect(() => this.guardar('docs.vista', this.vista()));
  }

  private async cargar(raiz: string, ruta: string): Promise<void> {
    this.cargando.set(true);
    this.error.set(null);
    try {
      if (raiz === 'papelera') this.papelera.set(await this.api.papelera());
      else this.elementos.set((await this.api.lista(raiz, ruta)).elementos);
    } catch (e) {
      this.elementos.set([]);
      this.error.set(e instanceof HttpErrorResponse && e.status === 404 ? 'Esta carpeta ya no existe.' : 'No hay conexión con el equipo.');
    } finally {
      this.cargando.set(false);
    }
  }

  protected recargar(): void { this.version.update((v) => v + 1); }

  // ---- navegación ----
  protected ir(raiz: string, ruta = ''): void {
    this.router.navigate([], { relativeTo: this.ruta, queryParams: { raiz, ruta: ruta || null } });
  }

  protected abrir(e: Elemento): void {
    this.menu.set(null);
    const raiz = this.raiz()!;
    if (e.carpeta) { this.ir(raiz, e.ruta); return; }
    if (e.tipo === 'audio') { this.empezar(e); return; }
    if (this.esImagen(e) || e.tipo === 'video') { this.ver(e); return; }
    if (seVe(e)) { window.open(this.api.url(raiz, e.ruta, true), '_blank', 'noopener'); return; }
    this.descargar(e);
  }

  protected descargar(e: Elemento): void {
    this.menu.set(null);
    const a = document.createElement('a');
    a.href = this.api.url(this.raiz()!, e.ruta);
    a.download = e.nombre;
    a.click();
  }

  protected miniatura(e: Elemento): string { return this.api.url(this.raiz()!, e.ruta, true); }

  // ---- búsqueda (en toda la carpeta raíz) ----
  protected buscar(texto: string): void {
    this.q.set(texto);
    clearTimeout(this.buscarT);
    if (texto.trim().length < 2) { this.resultados.set(null); return; }
    this.buscarT = setTimeout(async () => {
      try { this.resultados.set(await this.api.buscar(this.raiz()!, texto.trim())); } catch { this.resultados.set([]); }
    }, 280);
  }

  protected carpetaDe(e: Elemento): string {
    const i = e.ruta.lastIndexOf('/');
    return i < 0 ? this.nombreRaiz() : e.ruta.slice(0, i);
  }

  protected nombreRaiz(): string { return this.raices()?.find((r) => r.id === this.raiz())?.nombre ?? ''; }

  // ---- visor de fotos y vídeos ----
  protected ver(e: Elemento): void {
    this.zoom.set(1);
    this.viendo.set(e);
    this.precargarVecina(e);
    const d = this.dlgVer().nativeElement;
    if (!d.open) d.showModal();
  }

  protected pasar(delta: number): void {
    const l = this.imagenes(), e = this.viendo();
    if (!e || !l.length) return;
    const i = l.findIndex((x) => x.ruta === e.ruta);
    if (i < 0) return;
    const nueva = l[(i + delta + l.length) % l.length];
    this.zoom.set(1);
    this.viendo.set(nueva);
    this.precargarVecina(nueva);
  }

  // la siguiente foto se va bajando mientras miras esta
  private precargarVecina(e: Elemento): void {
    const l = this.imagenes();
    const i = l.findIndex((x) => x.ruta === e.ruta);
    if (i < 0 || l.length < 2) return;
    new Image().src = this.miniatura(l[(i + 1) % l.length]);
  }

  protected cambiarZoom(delta: number): void { this.zoom.set(Math.min(4, Math.max(1, Math.round((this.zoom() + delta) * 4) / 4))); }
  protected alternarZoom(): void { this.zoom.set(this.zoom() > 1 ? 1 : 2); }

  // ---- mover a otra carpeta sin arrastrar ----
  protected async pedirMover(e: Elemento): Promise<void> {
    this.menu.set(null);
    this.moverDlg.set({ elemento: e, ruta: '', carpetas: [], cargando: true });
    this.dlgMover().nativeElement.showModal();
    await this.entrarMover('');
  }

  protected async entrarMover(ruta: string): Promise<void> {
    const d = this.moverDlg();
    if (!d) return;
    this.moverDlg.set({ ...d, ruta, cargando: true, error: undefined });
    try {
      const l = await this.api.lista(this.raiz()!, ruta);
      // no se puede mover una carpeta dentro de sí misma
      const carpetas = l.elementos.filter((x) => x.carpeta && x.ruta !== d.elemento.ruta && !x.ruta.startsWith(d.elemento.ruta + '/'));
      this.moverDlg.set({ elemento: d.elemento, ruta, carpetas, cargando: false });
    } catch { this.moverDlg.set({ ...d, ruta, carpetas: [], cargando: false, error: 'No se pudo leer esa carpeta.' }); }
  }

  protected subirMover(): void {
    const d = this.moverDlg();
    if (d?.ruta) this.entrarMover(d.ruta.includes('/') ? d.ruta.slice(0, d.ruta.lastIndexOf('/')) : '');
  }

  protected async confirmarMover(): Promise<void> {
    const d = this.moverDlg();
    if (!d) return;
    try {
      await this.api.mover(this.raiz()!, d.elemento.ruta, d.ruta);
      this.dlgMover().nativeElement.close();
      this.avisar(`Movido a ${d.ruta.split('/').at(-1) || this.nombreRaiz()}`);
      this.recargar();
    } catch (e) {
      this.moverDlg.set({ ...d, error: e instanceof HttpErrorResponse ? (Object.values(e.error?.errors ?? {})[0] as string[] | undefined)?.[0] ?? 'No se pudo mover.' : 'No se pudo mover.' });
    }
  }

  private x0 = 0;
  protected tocarVisor(ev: PointerEvent): void { this.x0 = ev.clientX; }
  protected soltarVisor(ev: PointerEvent): void {
    const dx = ev.clientX - this.x0;
    if (Math.abs(dx) > 50) this.pasar(dx < 0 ? 1 : -1);
  }

  protected cerrarVisor(): void { this.dlgVer().nativeElement.close(); }
  protected alCerrarVisor(): void { this.viendo.set(null); }
  protected fueraVisor(ev: MouseEvent): void { if (ev.target === this.dlgVer().nativeElement) this.cerrarVisor(); }

  // ---- música: reproduce y sigue con la siguiente de la carpeta ----
  protected tocar(e: Elemento): void {
    this.sonando.set(e);
    setTimeout(() => this.reproductor()?.nativeElement.play().catch(() => {}));
  }

  protected siguiente(): void {
    const l = this.canciones(), e = this.sonando();
    const i = e ? l.findIndex((x) => x.ruta === e.ruta) : -1;
    if (i >= 0 && i < l.length - 1) this.tocar(l[i + 1]);
  }

  protected urlSonando(): string { const e = this.sonando(); return e ? this.api.url(this.raizSonando, e.ruta, true) : ''; }
  private raizSonando = '';
  protected empezar(e: Elemento): void { this.raizSonando = this.raiz()!; this.tocar(e); }

  // ---- subir ----
  protected elegirArchivos(): void { this.elegir().nativeElement.click(); }

  protected async alElegir(ev: Event): Promise<void> {
    const input = ev.target as HTMLInputElement;
    const files = [...(input.files ?? [])];
    input.value = '';
    await this.subir(files);
  }

  private async subir(files: File[]): Promise<void> {
    const raiz = this.raiz();
    if (!files.length || !raiz || raiz === 'papelera') return;
    const bien = await this.api.subir(raiz, this.carpeta(), files);
    this.avisar(bien === files.length ? `${bien === 1 ? 'Subido' : `${bien} archivos subidos`} a ${this.migas().at(-1)?.nombre ?? this.nombreRaiz()}` : `Se subieron ${bien} de ${files.length}`);
    this.recargar();
  }

  protected arrastrando(ev: DragEvent): void {
    if (!ev.dataTransfer?.types.includes('Files') || this.raiz() === 'papelera') return;
    ev.preventDefault();
    if (ev.type === 'dragenter') this.toques++;
    this.soltando.set(true);
  }

  protected salirArrastre(ev: DragEvent): void {
    if (!ev.dataTransfer?.types.includes('Files')) return;
    if (--this.toques <= 0) { this.toques = 0; this.soltando.set(false); }
  }

  protected soltar(ev: DragEvent): void {
    this.toques = 0;
    this.soltando.set(false);
    if (!ev.dataTransfer?.files.length) return;
    ev.preventDefault();
    this.subir([...ev.dataTransfer.files]);
  }

  // ---- mover arrastrando un elemento sobre una carpeta (o una miga) ----
  protected cogerElemento(ev: DragEvent, e: Elemento): void {
    ev.dataTransfer?.setData('application/x-mda-ruta', e.ruta);
    if (ev.dataTransfer) ev.dataTransfer.effectAllowed = 'move';
  }

  protected sobreCarpeta(ev: DragEvent, destino: string): void {
    if (!ev.dataTransfer?.types.includes('application/x-mda-ruta')) return;
    ev.preventDefault();
    this.moviendo.set(destino);
  }

  protected async soltarEnCarpeta(ev: DragEvent, destino: string): Promise<void> {
    const ruta = ev.dataTransfer?.getData('application/x-mda-ruta');
    this.moviendo.set(null);
    if (!ruta) return;
    ev.preventDefault();
    ev.stopPropagation();
    if (ruta === destino) return;
    try {
      await this.api.mover(this.raiz()!, ruta, destino);
      this.avisar(`Movido a ${destino.split('/').at(-1) || this.nombreRaiz()}`);
    } catch (e) {
      this.avisar(e instanceof HttpErrorResponse ? (Object.values(e.error?.errors ?? {})[0] as string[] | undefined)?.[0] ?? 'No se pudo mover.' : 'No se pudo mover.');
    }
    this.recargar();
  }

  // ---- carpeta nueva y renombrar ----
  protected pedirNombre(modo: 'carpeta' | 'renombrar', elemento?: Elemento): void {
    this.menu.set(null);
    this.nombreDlg.set({ modo, elemento, valor: elemento?.nombre ?? '' });
    const d = this.dlgNombre().nativeElement;
    d.showModal();
    this.soltarTeclado?.();
    this.soltarTeclado = seguirTeclado(d);
    // seleccionar el nombre sin la extensión, como en el explorador
    setTimeout(() => {
      const i = d.querySelector('input')!;
      i.focus();
      const punto = elemento && !elemento.carpeta ? elemento.nombre.lastIndexOf('.') : -1;
      i.setSelectionRange(0, punto > 0 ? punto : i.value.length);
    });
  }

  protected async guardarNombre(ev: Event): Promise<void> {
    ev.preventDefault();
    const n = this.nombreDlg();
    if (!n) return;
    const valor = n.valor.trim();
    try {
      if (n.modo === 'carpeta') await this.api.nuevaCarpeta(this.raiz()!, this.carpeta(), valor);
      else if (n.elemento && valor !== n.elemento.nombre) await this.api.renombrar(this.raiz()!, n.elemento.ruta, valor);
      this.dlgNombre().nativeElement.close();
      this.recargar();
    } catch (e) {
      const msg = e instanceof HttpErrorResponse ? (Object.values(e.error?.errors ?? {})[0] as string[] | undefined)?.[0] : null;
      this.nombreDlg.set({ ...n, error: msg ?? 'No se pudo guardar.' });
    }
  }

  protected escribirNombre(ev: Event): void {
    const n = this.nombreDlg();
    if (n) this.nombreDlg.set({ ...n, valor: (ev.target as HTMLInputElement).value, error: undefined });
  }

  protected alCerrarNombre(): void { this.soltarTeclado?.(); this.soltarTeclado = undefined; this.nombreDlg.set(null); }
  protected fueraNombre(ev: MouseEvent): void { if (ev.target === this.dlgNombre().nativeElement) this.dlgNombre().nativeElement.close(); }

  // ---- borrar (a la papelera) ----
  protected async borrar(e: Elemento): Promise<void> {
    if (this.confirmar() !== e.ruta) { this.confirmar.set(e.ruta); return; }
    this.confirmar.set(null);
    this.menu.set(null);
    await this.api.borrar(this.raiz()!, e.ruta);
    this.avisar(`«${e.nombre}» está en la papelera`);
    this.recargar();
  }

  protected abrirMenu(ev: Event, ruta: string): void {
    ev.stopPropagation();
    this.confirmar.set(null);
    this.menu.update((m) => (m === ruta ? null : ruta));
  }

  // ---- papelera ----
  protected async recuperar(p: EnPapelera): Promise<void> {
    const r = await this.api.recuperar(p.raiz, p.id);
    this.avisar(`«${p.nombre}» ha vuelto a ${r.ruta.includes('/') ? r.ruta.slice(0, r.ruta.lastIndexOf('/')) : this.raices()?.find((x) => x.id === p.raiz)?.nombre}`);
    this.recargar();
  }

  protected async eliminar(p?: EnPapelera): Promise<void> {
    const clave = p ? `${p.raiz}/${p.id}` : 'todo';
    if (this.confirmar() !== clave) { this.confirmar.set(clave); return; }
    this.confirmar.set(null);
    await this.api.borrarParaSiempre(p?.raiz, p?.id);
    this.recargar();
  }

  protected nombreDeRaiz(id: string): string { return this.raices()?.find((r) => r.id === id)?.nombre ?? id; }

  // ---- varios ----
  protected tecla(ev: KeyboardEvent): void {
    if (this.viendo() && this.esImagen(this.viendo()!)) {
      if (ev.key === 'ArrowRight') this.pasar(1);
      if (ev.key === 'ArrowLeft') this.pasar(-1);
      if (ev.key === '+' || ev.key === '=') this.cambiarZoom(0.5);
      if (ev.key === '-') this.cambiarZoom(-0.5);
      if (ev.key === '0') this.zoom.set(1);
    }
    if (ev.key === 'Escape') this.menu.set(null);
  }

  protected cerrarMenus(): void { this.menu.set(null); this.confirmar.set(null); }

  private avisar(texto: string): void {
    this.aviso.set(texto);
    clearTimeout(this.avisoT);
    this.avisoT = setTimeout(() => this.aviso.set(null), 3200);
  }

  private leer(clave: string, defecto: string): string { try { return localStorage.getItem(clave) ?? defecto; } catch { return defecto; } }
  private guardar(clave: string, valor: string): void { try { localStorage.setItem(clave, valor); } catch { /* sin almacenamiento */ } }

  ngOnDestroy(): void { this.soltarTeclado?.(); clearTimeout(this.avisoT); clearTimeout(this.buscarT); }
}
