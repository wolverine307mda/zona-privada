import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import { firstValueFrom } from 'rxjs';

interface Persona {
  id: number; nombre: string; apodo: string; relacion: string; telefonos: string[]; correos: string[]; direccion: string; notas: string;
  cumpleDia: number | null; cumpleMes: number | null; cumpleAnio: number | null; gustos: string[]; avisar: boolean; ultimaVez: string | null;
  proximo: string | null; dias: number | null; edad: number | null;
}
interface Regalo { id: number; idea: string; precio: number | null; ocasion: string; fecha: string | null; estado: 'idea' | 'comprado' | 'regalado'; sento: string; }
interface Idea { idea: string; etiquetas: string[]; precio: number; }
interface Candidato { indice: number; nombre: string; telefonos: string[]; correos: string[]; direccion: string; cumple: string | null; notas: string; duplicadoDe: string | null; }
type Filtro = 'todos' | 'mes' | 'sinregalo' | 'sinhablar';

const MESES = ['enero', 'febrero', 'marzo', 'abril', 'mayo', 'junio', 'julio', 'agosto', 'septiembre', 'octubre', 'noviembre', 'diciembre'];

// Personas: cumpleaños (con aviso a 14, 7 y 1 días y el mismo día), datos de contacto, gustos e ideas de regalo. Lo de otras personas se guarda cifrado.
// Nada se importa sin que lo marques: un archivo de contactos (.vcf) se ve primero y eliges cuáles y qué datos de cada uno.
@Component({
  selector: 'app-personas',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrls: ['../gastos/comun.css', '../gastos/dialogos.css'],
  template: `
    <header class="cab">
      <p class="cab__sub" aria-live="polite">{{ resumen() }}</p>
      <div class="cab__acc"><button class="button button--ghost" type="button" (click)="abrirImportar()">Importar contactos</button><button class="button" type="button" (click)="nueva()">Nueva persona</button></div>
    </header>

    <div class="filtros">
      <input class="buscar" type="search" placeholder="Buscar…" aria-label="Buscar personas" [value]="busqueda()" (input)="busqueda.set($any($event.target).value)">
      <div class="selector" role="group" aria-label="Filtro">
        @for (f of filtros; track f.id) { <button type="button" [attr.aria-pressed]="filtro() === f.id" (click)="filtro.set(f.id)">{{ f.nombre }}</button> }
      </div>
    </div>

    @if (fallo()) { <p class="sub">No se pudo cargar.</p> }
    @else if (visibles().length === 0) {
      <p class="sub">{{ lista().length ? 'Nadie con ese filtro.' : 'Aún no hay nadie. Añade a alguien o importa tus contactos (tú eliges cuáles) y te aviso de los cumpleaños.' }}</p>
    } @else {
      <ul class="lista">
        @for (p of visibles(); track p.id) {
          <li><button type="button" class="persona" (click)="abrir(p)" [attr.aria-label]="'Abrir ' + p.nombre">
            <span class="persona__nombre"><strong>{{ p.apodo || p.nombre }}</strong>@if (p.relacion) { <small>{{ p.relacion }}</small> }</span>
            <span class="persona__cumple" [class.hoy]="p.dias === 0">{{ cumpleTexto(p) }}</span>
            @if (p.gustos.length) { <span class="persona__gustos">{{ p.gustos.join(' · ') }}</span> }
          </button></li>
        }
      </ul>
    }

    <dialog class="dlg" #dlg (click)="fuera($event, dlg)" aria-labelledby="dlg-per">
      <form class="dlg__caja" (submit)="guardar($event)" novalidate>
        <header class="dlg__cabeza"><h2 id="dlg-per">{{ editando() ? editando()!.nombre : 'Nueva persona' }}</h2>
          <button class="dlg__x" type="button" (click)="dlg.close()" aria-label="Cerrar"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6l12 12M18 6 6 18" /></svg></button></header>
        <div class="fila2">
          <label class="campo"><span class="campo__etiqueta">Nombre</span><input class="campo__input" type="text" maxlength="80" [value]="f.nombre()" (input)="f.nombre.set($any($event.target).value)"></label>
          <label class="campo"><span class="campo__etiqueta">Apodo <em>(opcional)</em></span><input class="campo__input" type="text" maxlength="40" [value]="f.apodo()" (input)="f.apodo.set($any($event.target).value)"></label>
        </div>
        <div class="fila2">
          <label class="campo"><span class="campo__etiqueta">Relación</span><input class="campo__input" type="text" maxlength="40" placeholder="hermana, amigo…" [value]="f.relacion()" (input)="f.relacion.set($any($event.target).value)"></label>
          <label class="campo"><span class="campo__etiqueta">Cumpleaños <em>(día/mes/año)</em></span>
            <span class="trio"><input class="campo__input" type="number" min="1" max="31" placeholder="día" [value]="f.dia()" (input)="f.dia.set($any($event.target).value)">
              <input class="campo__input" type="number" min="1" max="12" placeholder="mes" [value]="f.mes()" (input)="f.mes.set($any($event.target).value)">
              <input class="campo__input" type="number" min="1900" max="2100" placeholder="año" [value]="f.anio()" (input)="f.anio.set($any($event.target).value)"></span></label>
        </div>
        <label class="campo"><span class="campo__etiqueta">Gustos <em>(separados por comas)</em></span><input class="campo__input" type="text" placeholder="café, libros, montaña" [value]="f.gustos()" (input)="f.gustos.set($any($event.target).value)"></label>
        <div class="fila2">
          <label class="campo"><span class="campo__etiqueta">Teléfonos <em>(separados por «;»)</em></span><input class="campo__input" type="text" [value]="f.telefonos()" (input)="f.telefonos.set($any($event.target).value)"></label>
          <label class="campo"><span class="campo__etiqueta">Correos</span><input class="campo__input" type="text" [value]="f.correos()" (input)="f.correos.set($any($event.target).value)"></label>
        </div>
        <label class="campo"><span class="campo__etiqueta">Notas <em>(alergias, tallas, lo que sea)</em></span><textarea class="campo__input" rows="3" maxlength="1000" [value]="f.notas()" (input)="f.notas.set($any($event.target).value)"></textarea></label>
        <label class="marca"><input type="checkbox" [checked]="f.avisar()" (change)="f.avisar.set($any($event.target).checked)"> Avisarme de su cumpleaños</label>
        @if (error()) { <p class="error" role="alert">{{ error() }}</p> }

        @if (editando(); as p) {
          <section class="regalos">
            <h3>Regalos</h3>
            <ul>
              @for (r of regalos(); track r.id) {
                <li><span><strong>{{ r.idea }}</strong> <small>{{ r.estado }}{{ r.precio ? ' · ' + r.precio + ' €' : '' }}{{ r.fecha ? ' · ' + r.fecha : '' }}{{ r.sento ? ' · ' + r.sento : '' }}</small></span>
                  <span class="acc">
                    @if (r.estado !== 'regalado') { <button type="button" class="enlace" (click)="estadoRegalo(r, 'regalado')">Regalado</button> }
                    <button type="button" class="enlace enlace--mal" (click)="quitarRegalo(r)">Quitar</button></span></li>
              } @empty { <li class="sub">Aún nada apuntado.</li> }
            </ul>
            <div class="fila2"><input class="campo__input" type="text" placeholder="Nueva idea de regalo" maxlength="120" [value]="nuevaIdea()" (input)="nuevaIdea.set($any($event.target).value)">
              <button type="button" class="button button--ghost" (click)="guardarIdea(nuevaIdea())">Guardar idea</button></div>
            <button type="button" class="enlace" (click)="pedirIdeas(p)">Sugerir ideas según sus gustos</button>
            @if (ideas().length) {
              <ul class="ideas">@for (i of ideas(); track i.idea) { <li><span>{{ i.idea }} <small>~{{ i.precio }} €</small></span><button type="button" class="enlace" (click)="guardarIdea(i.idea, i.precio)">Guardar</button></li> }</ul>
            } @else if (pidioIdeas()) { <p class="sub">Sin ideas para esos gustos: añade alguno más.</p> }
          </section>
        }

        <footer class="dlg__pie">
          @if (editando()) { <button type="button" class="boton-borrar" (click)="borrar()">{{ confirmar() ? 'Seguro: borrar a esta persona y sus regalos' : 'Borrar' }}</button> }
          <button class="button" type="submit">Guardar</button>
        </footer>
      </form>
    </dialog>

    <dialog class="dlg" #dlgImp (click)="fuera($event, dlgImp)" aria-labelledby="dlg-imp">
      <div class="dlg__caja">
        <header class="dlg__cabeza"><h2 id="dlg-imp">Importar contactos</h2>
          <button class="dlg__x" type="button" (click)="dlgImp.close()" aria-label="Cerrar"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6l12 12M18 6 6 18" /></svg></button></header>
        <p class="dlg__lead">En el iPhone: Contactos → selecciona los que quieras → Compartir → «Guardar en Archivos», y súbelo aquí (.vcf). Primero lo ves y marcas cuáles traer.</p>
        <input type="file" accept=".vcf,text/vcard,text/x-vcard" (change)="leerArchivo($event)">
        @if (candidatos().length) {
          <div class="traer"><label><input type="radio" name="traer" [checked]="traer() === 'basico'" (change)="traer.set('basico')"> Solo nombre y cumpleaños</label>
            <label><input type="radio" name="traer" [checked]="traer() === 'completo'" (change)="traer.set('completo')"> También teléfono, correo, dirección y notas</label></div>
          <input class="buscar" type="search" placeholder="Buscar en el archivo…" [value]="filtroImp()" (input)="filtroImp.set($any($event.target).value)">
          <ul class="cand">
            @for (c of candidatosVisibles(); track c.indice) {
              <li><label><input type="checkbox" [checked]="marcados().has(c.indice)" [disabled]="!!c.duplicadoDe" (change)="marcar(c.indice, $any($event.target).checked)">
                <span><strong>{{ c.nombre }}</strong> <small>{{ c.cumple ? '🎂 ' + c.cumple : '' }}{{ c.duplicadoDe ? ' · ya está como «' + c.duplicadoDe + '»' : '' }}</small></span></label></li>
            }
          </ul>
          <p class="sub">{{ marcados().size }} marcados de {{ candidatos().length }}.</p>
        }
        @if (aviso()) { <p class="aviso" role="status">{{ aviso() }}</p> }
        <footer class="dlg__pie"><button class="button" type="button" [disabled]="marcados().size === 0" (click)="importar()">Traer los marcados</button></footer>
      </div>
    </dialog>
  `,
  styles: `
    .cab { display: flex; align-items: center; justify-content: space-between; gap: 12px; flex-wrap: wrap; margin-bottom: 12px; }
    .cab__sub { margin: 0; font: var(--peso-ligero, 300) 1.3rem/1.2 var(--font-text); }
    .cab__acc { display: flex; gap: 8px; flex-wrap: wrap; }
    .filtros { display: flex; gap: 10px; flex-wrap: wrap; align-items: center; margin-bottom: 12px; }
    .buscar { flex: 1; min-width: 160px; min-height: 44px; padding: 0 14px; border-radius: 12px; border: 1px solid var(--linea); background: var(--tarjeta-2); color: var(--texto); font: inherit; }
    .selector { display: inline-flex; flex-wrap: wrap; gap: 4px; }
    .selector button { border: 1px solid var(--vidrio-borde); background: none; color: var(--soft); border-radius: 999px; padding: 6px 12px; font: 500 .78rem/1 var(--font-text); min-height: 36px; cursor: pointer; }
    .selector button[aria-pressed='true'] { background: var(--brand); border-color: var(--brand); color: var(--on-brand); }
    .sub { margin: 0; color: var(--soft); font-size: .88rem; line-height: 1.5; }
    .lista { list-style: none; margin: 0; padding: 0; display: grid; gap: 8px; grid-template-columns: repeat(auto-fill, minmax(260px, 1fr)); }
    .persona { width: 100%; display: grid; gap: 4px; padding: 12px 16px; text-align: left; border-radius: var(--radio-l, 16px); border: 1px solid var(--vidrio-borde); background: var(--vidrio); color: inherit; font: inherit; cursor: pointer; }
    .persona__nombre { display: flex; align-items: baseline; gap: 8px; } .persona__nombre small { color: var(--soft); }
    .persona__cumple { font-size: .85rem; color: var(--soft); } .persona__cumple.hoy { color: var(--brand); font-weight: 600; }
    .persona__gustos { font-size: .78rem; color: var(--soft); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .trio { display: grid; grid-template-columns: 1fr 1fr 1.3fr; gap: 6px; }
    .marca, .traer label { display: flex; align-items: center; gap: 10px; min-height: 40px; }
    .regalos { display: grid; gap: 8px; padding-top: 8px; border-top: 1px solid var(--linea); }
    .regalos h3 { margin: 0; font: 500 .95rem/1.2 var(--font-text); }
    .regalos ul, .ideas, .cand { list-style: none; margin: 0; padding: 0; display: grid; gap: 4px; }
    .regalos li, .ideas li { display: flex; justify-content: space-between; align-items: center; gap: 10px; font-size: .88rem; }
    .regalos small, .ideas small, .cand small { color: var(--soft); }
    .acc { display: flex; gap: 12px; }
    .enlace { background: none; border: 0; color: var(--brand); font: inherit; cursor: pointer; min-height: 36px; text-align: left; }
    .enlace--mal { color: var(--bad, #ff5a5a); }
    .cand { max-height: 38vh; overflow: auto; } .cand label { display: flex; align-items: center; gap: 10px; min-height: 40px; }
    .aviso { margin: 0; padding: 8px 12px; border-radius: 10px; background: var(--tarjeta-2); font-size: .88rem; }
  `,
})
export class Personas {
  private readonly http = inject(HttpClient);
  private readonly dlg = viewChild.required<ElementRef<HTMLDialogElement>>('dlg');
  private readonly dlgImp = viewChild.required<ElementRef<HTMLDialogElement>>('dlgImp');

  protected readonly filtros: { id: Filtro; nombre: string }[] = [{ id: 'todos', nombre: 'Todos' }, { id: 'mes', nombre: 'Cumples del mes' }, { id: 'sinregalo', nombre: 'Sin regalo pensado' }, { id: 'sinhablar', nombre: 'Sin hablar hace tiempo' }];
  protected readonly lista = signal<Persona[]>([]);
  protected readonly fallo = signal(false);
  protected readonly busqueda = signal('');
  protected readonly filtro = signal<Filtro>('todos');
  protected readonly conRegalo = signal<Set<number>>(new Set());

  protected readonly visibles = computed(() => {
    const q = this.busqueda().trim().toLowerCase(), hoy = new Date();
    return this.lista().filter((p) => {
      if (q && !`${p.nombre} ${p.apodo} ${p.relacion} ${p.gustos.join(' ')}`.toLowerCase().includes(q)) return false;
      switch (this.filtro()) {
        case 'mes': return p.cumpleMes === hoy.getMonth() + 1;
        case 'sinregalo': return p.dias !== null && p.dias <= 60 && !this.conRegalo().has(p.id);
        case 'sinhablar': return !p.ultimaVez || (Date.now() - new Date(p.ultimaVez).getTime()) / 86_400_000 > 60;
        default: return true;
      }
    });
  });
  protected readonly resumen = computed(() => {
    const l = this.lista();
    if (l.length === 0) return 'Nadie apuntado todavía';
    const cerca = l.filter((p) => p.dias !== null && p.dias <= 7).length;
    return cerca ? `${cerca} cumpleaños esta semana` : `${l.length} personas`;
  });

  // ---- ficha ----
  protected readonly editando = signal<Persona | null>(null);
  protected readonly f = {
    nombre: signal(''), apodo: signal(''), relacion: signal(''), dia: signal(''), mes: signal(''), anio: signal(''), gustos: signal(''), telefonos: signal(''), correos: signal(''), notas: signal(''), avisar: signal(true),
  };
  protected readonly error = signal('');
  protected readonly confirmar = signal(false);
  protected readonly regalos = signal<Regalo[]>([]);
  protected readonly ideas = signal<Idea[]>([]);
  protected readonly pidioIdeas = signal(false);
  protected readonly nuevaIdea = signal('');

  // ---- importar ----
  protected readonly candidatos = signal<Candidato[]>([]);
  protected readonly marcados = signal<Set<number>>(new Set());
  protected readonly traer = signal<'basico' | 'completo'>('basico');
  protected readonly filtroImp = signal('');
  protected readonly aviso = signal('');
  private vcf = '';
  protected readonly candidatosVisibles = computed(() => { const q = this.filtroImp().toLowerCase(); return this.candidatos().filter((c) => !q || c.nombre.toLowerCase().includes(q)); });

  constructor() { void this.cargar(); }

  protected async cargar(): Promise<void> {
    this.fallo.set(false);
    try { this.lista.set(await firstValueFrom(this.http.get<Persona[]>('/api/personas'))); } catch { this.fallo.set(true); }
  }

  protected cumpleTexto(p: Persona): string {
    if (p.cumpleDia === null || p.cumpleMes === null) return 'Sin cumpleaños';
    const fecha = `${p.cumpleDia} de ${MESES[p.cumpleMes - 1]}`;
    const cuando = p.dias === 0 ? 'HOY' : p.dias === 1 ? 'mañana' : `en ${p.dias} días`;
    return `🎂 ${fecha} · ${cuando}${p.edad ? ` · cumple ${p.edad}` : ''}`;
  }

  protected nueva(): void { this.abrirFicha(null); }
  protected abrir(p: Persona): void { this.abrirFicha(p); }

  private async abrirFicha(p: Persona | null): Promise<void> {
    this.editando.set(p); this.error.set(''); this.confirmar.set(false); this.ideas.set([]); this.pidioIdeas.set(false); this.nuevaIdea.set(''); this.regalos.set([]);
    this.f.nombre.set(p?.nombre ?? ''); this.f.apodo.set(p?.apodo ?? ''); this.f.relacion.set(p?.relacion ?? '');
    this.f.dia.set(p?.cumpleDia?.toString() ?? ''); this.f.mes.set(p?.cumpleMes?.toString() ?? ''); this.f.anio.set(p?.cumpleAnio?.toString() ?? '');
    this.f.gustos.set(p?.gustos.join(', ') ?? ''); this.f.telefonos.set(p?.telefonos.join('; ') ?? ''); this.f.correos.set(p?.correos.join('; ') ?? ''); this.f.notas.set(p?.notas ?? ''); this.f.avisar.set(p?.avisar ?? true);
    this.dlg().nativeElement.showModal();
    if (p) await this.cargarRegalos(p.id);
  }

  private async cargarRegalos(id: number): Promise<void> {
    try { const r = await firstValueFrom(this.http.get<Regalo[]>(`/api/personas/${id}/regalos`)); this.regalos.set(r); this.conRegalo.update((s) => { const n = new Set(s); if (r.length) n.add(id); else n.delete(id); return n; }); } catch { /* sin regalos */ }
  }

  protected async guardar(e: Event): Promise<void> {
    e.preventDefault();
    const num = (s: string) => (s.trim() === '' ? undefined : Number(s));
    const cuerpo = {
      nombre: this.f.nombre().trim(), apodo: this.f.apodo(), relacion: this.f.relacion(), cumpleDia: num(this.f.dia()), cumpleMes: num(this.f.mes()), cumpleAnio: num(this.f.anio()),
      quitarCumple: this.f.dia().trim() === '' && this.f.mes().trim() === '', gustos: this.f.gustos(), telefonos: this.f.telefonos(), correos: this.f.correos(), notas: this.f.notas(), avisar: this.f.avisar(),
    };
    if (!cuerpo.nombre) { this.error.set('Ponle un nombre.'); return; }
    try {
      const p = this.editando();
      if (p) await firstValueFrom(this.http.put(`/api/personas/${p.id}`, cuerpo)); else await firstValueFrom(this.http.post('/api/personas', cuerpo));
      this.dlg().nativeElement.close(); await this.cargar();
    } catch (x) { const er = (x as HttpErrorResponse).error?.errors as Record<string, string[]> | undefined; this.error.set(er ? Object.values(er)[0]?.[0] ?? 'Revisa los datos.' : 'No se pudo guardar.'); }
  }

  protected async borrar(): Promise<void> {
    const p = this.editando(); if (!p) return;
    if (!this.confirmar()) { this.confirmar.set(true); return; }
    try { await firstValueFrom(this.http.delete(`/api/personas/${p.id}`)); this.dlg().nativeElement.close(); await this.cargar(); } catch { this.error.set('No se pudo borrar.'); }
  }

  protected async guardarIdea(idea: string, precio?: number): Promise<void> {
    const p = this.editando(); if (!p || !idea.trim()) return;
    try { await firstValueFrom(this.http.post(`/api/personas/${p.id}/regalos`, { idea: idea.trim(), precio, estado: 'idea' })); this.nuevaIdea.set(''); await this.cargarRegalos(p.id); } catch { this.error.set('No se pudo guardar la idea.'); }
  }
  protected async estadoRegalo(r: Regalo, estado: string): Promise<void> {
    const p = this.editando(); if (!p) return;
    try { await firstValueFrom(this.http.put(`/api/personas/regalos/${r.id}`, { estado, fecha: new Date().toISOString().slice(0, 10) })); await this.cargarRegalos(p.id); } catch { /* se queda como estaba */ }
  }
  protected async quitarRegalo(r: Regalo): Promise<void> {
    const p = this.editando(); if (!p) return;
    try { await firstValueFrom(this.http.delete(`/api/personas/regalos/${r.id}`)); await this.cargarRegalos(p.id); } catch { /* ya no estaba */ }
  }
  protected async pedirIdeas(p: Persona): Promise<void> {
    try { this.ideas.set(await firstValueFrom(this.http.get<Idea[]>(`/api/personas/${p.id}/ideas`))); } catch { this.ideas.set([]); }
    this.pidioIdeas.set(true);
  }

  // ---- importar ----
  protected abrirImportar(): void { this.candidatos.set([]); this.marcados.set(new Set()); this.aviso.set(''); this.vcf = ''; this.dlgImp().nativeElement.showModal(); }

  protected async leerArchivo(e: Event): Promise<void> {
    const f = (e.target as HTMLInputElement).files?.[0]; if (!f) return;
    if (f.size > 5 * 1024 * 1024) { this.aviso.set('El archivo pasa de 5 MB.'); return; }
    this.vcf = await f.text();
    try {
      const c = await firstValueFrom(this.http.post<Candidato[]>('/api/personas/importar/vista-previa', { vcf: this.vcf }));
      this.candidatos.set(c); this.marcados.set(new Set()); this.aviso.set(c.length ? '' : 'No he encontrado contactos en ese archivo.');
    } catch { this.aviso.set('No se pudo leer el archivo.'); }
  }

  protected marcar(i: number, v: boolean): void { this.marcados.update((s) => { const n = new Set(s); if (v) n.add(i); else n.delete(i); return n; }); }

  protected async importar(): Promise<void> {
    try {
      const r = await firstValueFrom(this.http.post<{ nuevas: number; saltadas: number }>('/api/personas/importar', { vcf: this.vcf, indices: [...this.marcados()], traer: this.traer() }));
      this.aviso.set(`Traídas ${r.nuevas}${r.saltadas ? ` (${r.saltadas} ya estaban)` : ''}.`); this.candidatos.set([]); this.marcados.set(new Set()); await this.cargar();
    } catch { this.aviso.set('No se pudo importar.'); }
  }

  protected fuera(e: MouseEvent, d: HTMLDialogElement): void { if (e.target === d) d.close(); }
}
