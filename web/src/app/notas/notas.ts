import { NgTemplateOutlet } from '@angular/common';
import { Component, DestroyRef, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { seguirTeclado } from '../gastos/teclado';
import { alternarTarea, markdown, progresoTareas } from './markdown';
import { COLORES_NOTA, Nota, NotaEntrada, NotasService, colorNota } from './notas.service';

// Notas: tarjetas en mosaico (las fijadas arriba), búsqueda al instante, etiquetas, colores y tareas
// que se marcan desde la propia tarjeta. El editor guarda solo mientras escribes.
@Component({
  selector: 'app-notas',
  imports: [NgTemplateOutlet],
  templateUrl: './notas.html',
  styleUrls: ['../gastos/comun.css', '../gastos/dialogos.css', './notas.css'],
  host: { '(document:keydown)': 'atajo($event)' },
})
export class Notas {
  private readonly api = inject(NotasService);
  private readonly ruta = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly dlg = viewChild.required<ElementRef<HTMLDialogElement>>('editor');
  private readonly area = viewChild<ElementRef<HTMLTextAreaElement>>('area');

  protected readonly notas = signal<Nota[]>([]);
  protected readonly cargado = signal(false);
  protected readonly archivadas = signal(false);
  protected readonly q = signal('');
  protected readonly etiqueta = signal<string | null>(null);
  protected readonly colores = COLORES_NOTA;
  protected readonly colorNota = colorNota;
  protected readonly progreso = progresoTareas;

  // editor
  protected readonly abierta = signal<Nota | null>(null);
  protected readonly titulo = signal('');
  protected readonly contenido = signal('');
  protected readonly color = signal('');
  protected readonly etiquetas = signal('');
  protected readonly fijada = signal(false);
  protected readonly vista = signal(false);
  protected readonly estado = signal<'' | 'guardando' | 'guardado' | 'error'>('');
  protected readonly confirmar = signal(false);

  protected readonly todasEtiquetas = computed(() => [...new Set(this.notas().flatMap((n) => n.etiquetas))].sort());
  protected readonly visibles = computed(() => this.notas().filter((n) => !this.etiqueta() || n.etiquetas.includes(this.etiqueta()!)));
  protected readonly fijadas = computed(() => this.visibles().filter((n) => n.fijada));
  protected readonly otras = computed(() => this.visibles().filter((n) => !n.fijada));
  protected readonly previa = computed(() => markdown(this.contenido()));

  private temporizador?: ReturnType<typeof setTimeout>;
  private buscando?: ReturnType<typeof setTimeout>;
  private soltar?: () => void;
  private readonly html = new Map<string, string>();

  constructor() {
    this.cargar();
    // /notas?nueva=1 abre el editor (lo usan el buscador y el inicio)
    this.ruta.queryParamMap.subscribe((p) => { if (p.get('nueva')) { this.nueva(); this.router.navigate([], { queryParams: {}, replaceUrl: true }); } });
    inject(DestroyRef).onDestroy(() => { clearTimeout(this.temporizador); clearTimeout(this.buscando); this.soltar?.(); });
  }

  private async cargar(): Promise<void> {
    try { this.notas.set(await this.api.lista(this.archivadas(), this.q().trim())); } finally { this.cargado.set(true); }
  }

  protected buscar(t: string): void {
    this.q.set(t);
    clearTimeout(this.buscando);
    this.buscando = setTimeout(() => this.cargar(), 220);
  }

  protected verArchivadas(v: boolean): void { this.archivadas.set(v); this.etiqueta.set(null); this.cargar(); }

  // el HTML de cada nota se calcula una vez por contenido
  protected render(n: Nota): string {
    let h = this.html.get(n.contenido);
    if (h === undefined) { h = markdown(n.contenido); this.html.set(n.contenido, h); }
    return h;
  }

  protected fecha(iso: string): string {
    const d = new Date(iso), hoy = new Date();
    if (d.toDateString() === hoy.toDateString()) return `hoy, ${d.toLocaleTimeString('es-ES', { hour: '2-digit', minute: '2-digit' })}`;
    return d.toLocaleDateString('es-ES', { day: 'numeric', month: 'short', ...(d.getFullYear() !== hoy.getFullYear() ? { year: 'numeric' } : {}) });
  }

  // pulsar una casilla en la tarjeta la marca sin abrir el editor
  protected async clicTarjeta(e: MouseEvent, n: Nota): Promise<void> {
    const t = (e.target as HTMLElement).closest('.con-tarea');
    if ((e.target as HTMLElement).closest('a')) return;
    if (t) {
      e.stopPropagation();
      const i = [...(e.currentTarget as HTMLElement).querySelectorAll('.con-tarea')].indexOf(t);
      const contenido = alternarTarea(n.contenido, i);
      this.notas.update((l) => l.map((x) => (x.id === n.id ? { ...x, contenido } : x)));
      await this.api.guardar({ ...this.entrada(n), contenido }, n.id);
      return;
    }
    this.abrir(n);
  }

  protected async alternarFijada(e: Event, n: Nota): Promise<void> {
    e.stopPropagation();
    const g = await this.api.guardar({ ...this.entrada(n), fijada: !n.fijada }, n.id);
    this.notas.update((l) => l.map((x) => (x.id === n.id ? g : x)));
  }

  private entrada(n: Nota): NotaEntrada {
    return { titulo: n.titulo, contenido: n.contenido, color: n.color, etiquetas: n.etiquetas, fijada: n.fijada, archivada: n.archivada };
  }

  // ---- editor ----
  protected nueva(plantilla = ''): void {
    this.abrir(null, plantilla);
  }

  protected abrir(n: Nota | null, plantilla = ''): void {
    this.abierta.set(n);
    this.titulo.set(n?.titulo ?? '');
    this.contenido.set(n?.contenido ?? plantilla);
    this.color.set(n?.color ?? '');
    this.etiquetas.set(n?.etiquetas.join(', ') ?? (this.etiqueta() ?? ''));
    this.fijada.set(n?.fijada ?? false);
    this.vista.set(false);
    this.estado.set('');
    this.confirmar.set(false);
    const d = this.dlg().nativeElement;
    d.showModal();
    this.soltar?.();
    this.soltar = seguirTeclado(d);
    setTimeout(() => this.ajustarAlto(), 0);
  }

  protected cambio(campo: 'titulo' | 'contenido' | 'etiquetas', v: string): void {
    this[campo].set(v);
    if (campo === 'contenido') this.ajustarAlto();
    this.programar();
  }

  protected ponerColor(c: string): void { this.color.set(c); this.programar(); }
  protected alternarFijadaEditor(): void { this.fijada.update((f) => !f); this.programar(); }

  private programar(): void {
    clearTimeout(this.temporizador);
    this.estado.set('guardando');
    this.temporizador = setTimeout(() => this.guardar(), 700);
  }

  private async guardar(archivada?: boolean): Promise<void> {
    clearTimeout(this.temporizador);
    if (!this.titulo().trim() && !this.contenido().trim()) { this.estado.set(''); return; }
    const n = this.abierta();
    const datos: NotaEntrada = {
      titulo: this.titulo().trim(), contenido: this.contenido(), color: this.color(), fijada: this.fijada(),
      etiquetas: this.etiquetas().split(',').map((x) => x.trim()).filter(Boolean), archivada: archivada ?? n?.archivada ?? false,
    };
    try {
      const g = await this.api.guardar(datos, n?.id);
      this.abierta.set(g);
      this.notas.update((l) => (l.some((x) => x.id === g.id) ? l.map((x) => (x.id === g.id ? g : x)) : [g, ...l])
        .filter((x) => x.archivada === this.archivadas()).sort((a, b) => +b.fijada - +a.fijada || b.modificado.localeCompare(a.modificado)));
      this.estado.set('guardado');
    } catch { this.estado.set('error'); }
  }

  protected async cerrar(): Promise<void> {
    if (this.estado() === 'guardando') await this.guardar();
    this.dlg().nativeElement.close();
  }

  protected alCerrar(): void { this.soltar?.(); this.soltar = undefined; if (this.estado() === 'guardando') this.guardar(); }

  protected async archivar(): Promise<void> {
    await this.guardar(!(this.abierta()?.archivada ?? false));
    this.dlg().nativeElement.close();
  }

  protected async borrar(): Promise<void> {
    const n = this.abierta();
    if (!n) { this.dlg().nativeElement.close(); return; }
    if (!this.confirmar()) { this.confirmar.set(true); return; }
    await this.api.borrar(n.id);
    this.notas.update((l) => l.filter((x) => x.id !== n.id));
    this.estado.set('');
    this.dlg().nativeElement.close();
  }

  // botones de formato: envuelven la selección o añaden al principio de la línea
  protected formato(tipo: 'tarea' | 'negrita' | 'titulo' | 'lista'): void {
    const a = this.area()?.nativeElement;
    if (!a) return;
    const [i, f] = [a.selectionStart, a.selectionEnd];
    const t = this.contenido();
    let nuevo: string, cursor: number;
    if (tipo === 'negrita') {
      nuevo = `${t.slice(0, i)}**${t.slice(i, f) || 'texto'}**${t.slice(f)}`;
      cursor = f + (i === f ? 7 : 2);
    } else {
      const ini = t.lastIndexOf('\n', i - 1) + 1;
      const pre = tipo === 'tarea' ? '- [ ] ' : tipo === 'lista' ? '- ' : '## ';
      nuevo = `${t.slice(0, ini)}${pre}${t.slice(ini)}`;
      cursor = i + pre.length;
    }
    this.cambio('contenido', nuevo);
    a.value = nuevo;
    a.focus();
    a.setSelectionRange(cursor, cursor);
  }

  // al pulsar Intro en una lista o tarea, la siguiente línea sigue la lista
  protected tecla(e: KeyboardEvent): void {
    if (e.key !== 'Enter' || e.shiftKey || e.isComposing) return;
    const a = e.target as HTMLTextAreaElement;
    const t = a.value, i = a.selectionStart;
    const linea = t.slice(t.lastIndexOf('\n', i - 1) + 1, i);
    const m = linea.match(/^(\s*)([-*+] \[[ xX]\] |[-*+] |(\d+)[.)] )/);
    if (!m) return;
    e.preventDefault();
    // línea vacía de lista: se acaba la lista
    if (linea.trim() === m[2].trim()) {
      const ini = i - linea.length;
      const nuevo = t.slice(0, ini) + t.slice(i);
      this.cambio('contenido', nuevo); a.value = nuevo; a.setSelectionRange(ini, ini);
      return;
    }
    const sig = m[3] ? `${+m[3] + 1}. ` : m[2].replace(/\[[xX]\]/, '[ ]');
    const ins = `\n${m[1]}${sig}`;
    const nuevo = t.slice(0, i) + ins + t.slice(a.selectionEnd);
    this.cambio('contenido', nuevo);
    a.value = nuevo;
    a.setSelectionRange(i + ins.length, i + ins.length);
  }

  private ajustarAlto(): void {
    const a = this.area()?.nativeElement;
    if (!a) return;
    a.style.height = 'auto';
    a.style.height = `${Math.max(180, a.scrollHeight)}px`;
  }

  // «n» abre una nota nueva (fuera de los campos de texto)
  protected atajo(e: KeyboardEvent): void {
    const t = e.target as HTMLElement | null;
    if (e.key === 'n' && !e.ctrlKey && !e.metaKey && !e.altKey && !t?.closest?.('input, textarea, select, [contenteditable]') && !this.dlg().nativeElement.open) {
      e.preventDefault();
      this.nueva();
    }
  }

  protected fuera(e: MouseEvent): void { if (e.target === this.dlg().nativeElement) this.cerrar(); }
}
