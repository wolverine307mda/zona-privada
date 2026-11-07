import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, ElementRef, OnDestroy, computed, inject, signal, viewChild } from '@angular/core';
import { firstValueFrom } from 'rxjs';

interface Ingrediente { nombre: string; cantidad: number | null; unidad: string; texto: string; }
interface Receta { id: number; nombre: string; raciones: number; minutos: number; etiquetas: string[]; pasos: string[]; origen: string; ingredientes: Ingrediente[]; }
interface Celda { fecha: string; momento: 'comida' | 'cena'; recetaId: number | null; receta: string | null; texto: string; sobras: boolean; }
interface Menu { desde: string; celdas: Celda[]; }
interface Ajustes { alergias: string[]; horaAviso: string; }

const DIAS = ['Lunes', 'Martes', 'Miércoles', 'Jueves', 'Viernes', 'Sábado', 'Domingo'];
const iso = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

// Cocina: el menú de la semana (a mano o generado por reglas: etiquetas, alergias, sin repetir en 14 días y con días de sobras), tus recetas
// (a mano o leídas de una página web), la lista de la compra que sale del menú y «cocinar hoy» con los pasos en grande y la pantalla encendida.
@Component({
  selector: 'app-cocina',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrls: ['../gastos/comun.css', '../gastos/dialogos.css'],
  template: `
    <header class="cab">
      <p class="cab__sub">Semana del {{ fechaLarga(menu()?.desde) }}</p>
      <div class="cab__acc">
        <button class="button button--ghost" type="button" (click)="semana(-1)" aria-label="Semana anterior">←</button>
        <button class="button button--ghost" type="button" (click)="semana(0)">Hoy</button>
        <button class="button button--ghost" type="button" (click)="semana(1)" aria-label="Semana siguiente">→</button>
        <button class="button" type="button" (click)="abrirGenerar()">Generar menú</button>
      </div>
    </header>
    @if (aviso()) { <p class="aviso" role="status">{{ aviso() }}</p> }

    <section class="semana" aria-label="Menú de la semana">
      @for (d of dias(); track d.fecha) {
        <article class="dia" [class.dia--hoy]="d.fecha === hoy">
          <h3>{{ d.nombre }} <small>{{ d.corto }}</small></h3>
          @for (m of momentos; track m) {
            <button type="button" class="celda" [class.celda--vacia]="!celda(d.fecha, m)" (click)="editarCelda(d.fecha, m)">
              <span class="celda__m">{{ m }}</span>
              <span class="celda__t">{{ textoCelda(d.fecha, m) }}</span>
            </button>
          }
          @if (d.fecha === hoy && celdaHoy()) { <button type="button" class="enlace" (click)="cocinarHoy()">🍳 Cocinar hoy</button> }
        </article>
      }
    </section>
    <div class="acciones">
      <button class="button button--ghost" type="button" (click)="compra()">Mandar la lista de la semana a Compra</button>
      <button class="button button--ghost" type="button" (click)="abrirAjustes()">Alergias y aviso</button>
    </div>

    <section class="caja recetas">
      <header class="caja__cab"><h3>Recetas ({{ recetas().length }})</h3>
        <div class="cab__acc"><button class="button button--ghost" type="button" (click)="abrirWeb()">Leer de una web</button><button class="button" type="button" (click)="nuevaReceta()">Nueva</button></div></header>
      <ul class="lista">
        @for (r of recetas(); track r.id) {
          <li><button type="button" class="receta" (click)="verReceta(r)"><strong>{{ r.nombre }}</strong><small>{{ r.minutos ? r.minutos + ' min · ' : '' }}{{ r.raciones }} raciones{{ r.etiquetas.length ? ' · ' + r.etiquetas.join(', ') : '' }}</small></button></li>
        } @empty { <li class="sub">Aún no hay recetas. Lee una de una página de cocina o escribe la primera.</li> }
      </ul>
    </section>

    <!-- una receta: raciones ajustables, ingredientes y pasos; editar y «cocinar» -->
    <dialog class="dlg" #dlgReceta (click)="fuera($event, dlgReceta)" aria-labelledby="dlg-rec">
      <div class="dlg__caja">
        @if (actual(); as r) {
          <header class="dlg__cabeza"><h2 id="dlg-rec">{{ r.nombre }}</h2><button class="dlg__x" type="button" (click)="dlgReceta.close()" aria-label="Cerrar"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6l12 12M18 6 6 18" /></svg></button></header>
          <p class="sub">{{ r.minutos ? r.minutos + ' min · ' : '' }}<button class="mini" type="button" (click)="raciones.set(max1(raciones() - 1))" aria-label="Menos raciones">−</button> {{ raciones() }} raciones <button class="mini" type="button" (click)="raciones.set(raciones() + 1)" aria-label="Más raciones">+</button>
            @if (r.origen) { · <a [href]="r.origen" target="_blank" rel="noopener noreferrer">origen</a> }</p>
          <h4>Ingredientes</h4>
          <ul class="ing">@for (i of r.ingredientes; track $index) { <li>{{ ingrediente(i, r) }}</li> }</ul>
          <h4>Pasos</h4>
          <ol class="pasos">@for (p of r.pasos; track $index) { <li>{{ p }}</li> }</ol>
          <footer class="dlg__pie"><button class="boton-borrar" type="button" (click)="borrarReceta(r)">{{ confirmar() ? 'Seguro: borrar' : 'Borrar' }}</button>
            <button class="button button--ghost" type="button" (click)="editarReceta(r)">Editar</button><button class="button" type="button" (click)="cocinar(r)">Cocinar</button></footer>
        }
      </div>
    </dialog>

    <dialog class="dlg" #dlgForm (click)="fuera($event, dlgForm)" aria-labelledby="dlg-form">
      <form class="dlg__caja" (submit)="guardarReceta($event)" novalidate>
        <header class="dlg__cabeza"><h2 id="dlg-form">{{ editId() ? 'Editar receta' : 'Nueva receta' }}</h2><button class="dlg__x" type="button" (click)="dlgForm.close()" aria-label="Cerrar"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6l12 12M18 6 6 18" /></svg></button></header>
        <label class="campo"><span class="campo__etiqueta">Nombre</span><input class="campo__input" type="text" maxlength="120" [value]="f.nombre()" (input)="f.nombre.set($any($event.target).value)"></label>
        <div class="fila2">
          <label class="campo"><span class="campo__etiqueta">Raciones</span><input class="campo__input" type="number" min="1" max="50" [value]="f.raciones()" (input)="f.raciones.set($any($event.target).value)"></label>
          <label class="campo"><span class="campo__etiqueta">Minutos</span><input class="campo__input" type="number" min="0" max="1440" [value]="f.minutos()" (input)="f.minutos.set($any($event.target).value)"></label>
        </div>
        <label class="campo"><span class="campo__etiqueta">Etiquetas <em>(rápida, vegetariana…)</em></span><input class="campo__input" type="text" [value]="f.etiquetas()" (input)="f.etiquetas.set($any($event.target).value)"></label>
        <label class="campo"><span class="campo__etiqueta">Ingredientes <em>(uno por línea: «200 g de harina»)</em></span><textarea class="campo__input" rows="6" [value]="f.ingredientes()" (input)="f.ingredientes.set($any($event.target).value)"></textarea></label>
        <label class="campo"><span class="campo__etiqueta">Pasos <em>(uno por línea)</em></span><textarea class="campo__input" rows="6" [value]="f.pasos()" (input)="f.pasos.set($any($event.target).value)"></textarea></label>
        @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
        <footer class="dlg__pie"><button class="button" type="submit">Guardar</button></footer>
      </form>
    </dialog>

    <dialog class="dlg" #dlgWeb (click)="fuera($event, dlgWeb)" aria-labelledby="dlg-web">
      <form class="dlg__caja" (submit)="leerWeb($event)" novalidate>
        <header class="dlg__cabeza"><h2 id="dlg-web">Leer una receta de una web</h2><button class="dlg__x" type="button" (click)="dlgWeb.close()" aria-label="Cerrar"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6l12 12M18 6 6 18" /></svg></button></header>
        <p class="dlg__lead">Pega la dirección de la receta. Funciona con las páginas de cocina que traen sus datos estructurados (casi todas). Después puedes corregirla.</p>
        <input class="campo__input" type="url" placeholder="https://…" [value]="url()" (input)="url.set($any($event.target).value)">
        @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
        <footer class="dlg__pie"><button class="button" type="submit" [disabled]="ocupado()">{{ ocupado() ? 'Leyendo…' : 'Traer la receta' }}</button></footer>
      </form>
    </dialog>

    <!-- editar una casilla del menú -->
    <dialog class="dlg" #dlgCelda (click)="fuera($event, dlgCelda)" aria-labelledby="dlg-cel">
      <div class="dlg__caja">
        <header class="dlg__cabeza"><h2 id="dlg-cel">{{ celdaEd()?.momento }} del {{ fechaLarga(celdaEd()?.fecha) }}</h2><button class="dlg__x" type="button" (click)="dlgCelda.close()" aria-label="Cerrar"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6l12 12M18 6 6 18" /></svg></button></header>
        <label class="campo"><span class="campo__etiqueta">Receta</span>
          <select class="campo__input" (change)="celdaReceta.set(+$any($event.target).value || null)"><option value="">— ninguna —</option>
            @for (r of recetas(); track r.id) { <option [value]="r.id" [selected]="celdaReceta() === r.id">{{ r.nombre }}</option> }</select></label>
        <label class="campo"><span class="campo__etiqueta">O un texto libre <em>(fuera de casa, sobras…)</em></span><input class="campo__input" type="text" maxlength="120" [value]="celdaTexto()" (input)="celdaTexto.set($any($event.target).value)"></label>
        <footer class="dlg__pie"><button class="button button--ghost" type="button" (click)="guardarCelda(true)">Vaciar</button><button class="button" type="button" (click)="guardarCelda(false)">Guardar</button></footer>
      </div>
    </dialog>

    <dialog class="dlg" #dlgGen (click)="fuera($event, dlgGen)" aria-labelledby="dlg-gen">
      <div class="dlg__caja">
        <header class="dlg__cabeza"><h2 id="dlg-gen">Generar el menú de la semana</h2><button class="dlg__x" type="button" (click)="dlgGen.close()" aria-label="Cerrar"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6l12 12M18 6 6 18" /></svg></button></header>
        <p class="dlg__lead">Con tus recetas, sin repetir lo de los últimos 14 días y respetando tus alergias.</p>
        <label class="campo"><span class="campo__etiqueta">Solo recetas con estas etiquetas <em>(opcional, separadas por comas)</em></span><input class="campo__input" type="text" placeholder="rápida, vegetariana" [value]="genEtiquetas()" (input)="genEtiquetas.set($any($event.target).value)"></label>
        <label class="campo"><span class="campo__etiqueta">Días de sobras: {{ genSobras() }}</span><input type="range" min="0" max="3" [value]="genSobras()" (input)="genSobras.set(+$any($event.target).value)"></label>
        <label class="marca"><input type="checkbox" [checked]="genReemplazar()" (change)="genReemplazar.set($any($event.target).checked)"> Reemplazar lo que ya hay puesto</label>
        @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
        <footer class="dlg__pie"><button class="button" type="button" (click)="generar()">Generar</button></footer>
      </div>
    </dialog>

    <dialog class="dlg" #dlgAj (click)="fuera($event, dlgAj)" aria-labelledby="dlg-aj">
      <div class="dlg__caja">
        <header class="dlg__cabeza"><h2 id="dlg-aj">Alergias y aviso diario</h2><button class="dlg__x" type="button" (click)="dlgAj.close()" aria-label="Cerrar"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6l12 12M18 6 6 18" /></svg></button></header>
        <label class="campo"><span class="campo__etiqueta">Lo que no se puede comer <em>(separado por comas, como se escribe en las recetas: «nueces, gluten»)</em></span><input class="campo__input" type="text" [value]="ajAlergias()" (input)="ajAlergias.set($any($event.target).value)"></label>
        <label class="campo"><span class="campo__etiqueta">Avisarme de «hoy toca…» a las <em>(vacío = no avisar)</em></span><input class="campo__input" type="time" [value]="ajHora()" (input)="ajHora.set($any($event.target).value)"></label>
        <footer class="dlg__pie"><button class="button" type="button" (click)="guardarAjustes()">Guardar</button></footer>
      </div>
    </dialog>

    <!-- cocinar: pasos en grande y la pantalla encendida -->
    <dialog class="cocinando" #dlgCocinar aria-labelledby="coc-t" (close)="soltarPantalla()">
      @if (cocinando(); as r) {
        <header><h2 id="coc-t">{{ r.nombre }}</h2><button class="button button--ghost" type="button" (click)="dlgCocinar.close()">Cerrar</button></header>
        <p class="coc__paso-n">Paso {{ paso() + 1 }} de {{ r.pasos.length || 1 }}</p>
        <p class="coc__paso">{{ r.pasos[paso()] ?? 'Esta receta no tiene pasos escritos.' }}</p>
        <footer><button class="button button--ghost" type="button" (click)="paso.set(max0(paso() - 1))" [disabled]="paso() === 0">Anterior</button>
          <button class="button" type="button" (click)="paso.set(paso() + 1 < r.pasos.length ? paso() + 1 : paso())" [disabled]="paso() + 1 >= r.pasos.length">Siguiente</button></footer>
      }
    </dialog>
  `,
  styles: `
    .cab { display: flex; align-items: center; justify-content: space-between; gap: 12px; flex-wrap: wrap; margin-bottom: 12px; }
    .cab__sub { margin: 0; font: var(--peso-ligero, 300) 1.3rem/1.2 var(--font-text); }
    .cab__acc { display: flex; gap: 8px; flex-wrap: wrap; }
    .aviso { margin: 0 0 12px; padding: 8px 12px; border-radius: 10px; background: var(--tarjeta-2); font-size: .9rem; }
    .sub { margin: 0; color: var(--soft); font-size: .88rem; line-height: 1.6; }
    .semana { display: grid; grid-template-columns: repeat(7, minmax(0, 1fr)); gap: 8px; margin-bottom: 12px; }
    .dia { display: grid; align-content: start; gap: 6px; padding: 8px; border-radius: 14px; border: 1px solid var(--vidrio-borde); background: var(--vidrio); min-width: 0; }
    .dia--hoy { border-color: var(--brand); }
    .dia h3 { margin: 0; font: 600 .85rem/1.2 var(--font-text); } .dia h3 small { color: var(--soft); font-weight: 400; }
    .celda { display: grid; gap: 2px; padding: 8px; border-radius: 10px; border: 0; background: var(--tarjeta-2); color: var(--texto); font: inherit; text-align: left; cursor: pointer; min-height: 56px; }
    .celda--vacia { opacity: .55; }
    .celda__m { font-size: .68rem; text-transform: uppercase; letter-spacing: .06em; color: var(--soft); }
    .celda__t { font-size: .85rem; line-height: 1.25; overflow-wrap: anywhere; }
    .acciones { display: flex; flex-wrap: wrap; gap: 8px; margin-bottom: 16px; }
    .recetas { display: grid; gap: 8px; }
    .lista { list-style: none; margin: 0; padding: 0; display: grid; gap: 6px; grid-template-columns: repeat(auto-fill, minmax(240px, 1fr)); }
    .receta { width: 100%; display: grid; gap: 2px; padding: 10px 14px; text-align: left; border-radius: 12px; border: 1px solid var(--vidrio-borde); background: var(--tarjeta-2); color: inherit; font: inherit; cursor: pointer; }
    .receta small { color: var(--soft); }
    .enlace { background: none; border: 0; color: var(--brand); font: inherit; cursor: pointer; min-height: 36px; text-align: left; }
    .mini { width: 36px; height: 36px; border-radius: 50%; border: 1px solid var(--vidrio-borde); background: none; color: var(--texto); cursor: pointer; }
    h4 { margin: 10px 0 4px; font: 500 .9rem/1.2 var(--font-text); }
    .ing, .pasos { margin: 0; padding-left: 20px; display: grid; gap: 4px; font-size: .92rem; line-height: 1.45; }
    .marca { display: flex; align-items: center; gap: 10px; min-height: 40px; }
    .cocinando { width: min(100vw, 900px); height: min(100dvh, 640px); max-width: 100vw; margin: auto; padding: 24px; border-radius: 20px; border: 1px solid var(--vidrio-borde); background: var(--tarjeta); color: var(--texto); }
    .cocinando[open] { display: grid; grid-template-rows: auto auto 1fr auto; gap: 12px; }
    .cocinando header, .cocinando footer { display: flex; justify-content: space-between; align-items: center; gap: 12px; }
    .cocinando h2 { margin: 0; font: 500 1.3rem/1.2 var(--font-text); }
    .coc__paso-n { margin: 0; color: var(--soft); }
    .coc__paso { margin: 0; font: 500 clamp(1.5rem, 4.5vw, 2.4rem)/1.35 var(--font-text); align-self: center; }
    @media (max-width: 900px) { .semana { grid-template-columns: repeat(2, minmax(0, 1fr)); } .dia { grid-template-columns: minmax(0, 1fr); } }
    @media (max-width: 480px) { .semana { grid-template-columns: minmax(0, 1fr); } }
  `,
})
export class Cocina implements OnDestroy {
  private readonly http = inject(HttpClient);
  private readonly dlgReceta = viewChild.required<ElementRef<HTMLDialogElement>>('dlgReceta');
  private readonly dlgForm = viewChild.required<ElementRef<HTMLDialogElement>>('dlgForm');
  private readonly dlgWeb = viewChild.required<ElementRef<HTMLDialogElement>>('dlgWeb');
  private readonly dlgCelda = viewChild.required<ElementRef<HTMLDialogElement>>('dlgCelda');
  private readonly dlgGen = viewChild.required<ElementRef<HTMLDialogElement>>('dlgGen');
  private readonly dlgAj = viewChild.required<ElementRef<HTMLDialogElement>>('dlgAj');
  private readonly dlgCocinar = viewChild.required<ElementRef<HTMLDialogElement>>('dlgCocinar');

  protected readonly momentos: ('comida' | 'cena')[] = ['comida', 'cena'];
  protected readonly hoy = iso(new Date());
  private lunes = this.lunesDe(new Date());
  protected readonly menu = signal<Menu | null>(null);
  protected readonly recetas = signal<Receta[]>([]);
  protected readonly aviso = signal('');
  protected readonly error = signal('');
  protected readonly ocupado = signal(false);
  protected readonly dias = computed(() => {
    const base = new Date(`${this.menu()?.desde ?? iso(this.lunes)}T12:00:00`);
    return DIAS.map((nombre, i) => { const d = new Date(base); d.setDate(d.getDate() + i); return { nombre, fecha: iso(d), corto: `${d.getDate()}/${d.getMonth() + 1}` }; });
  });
  protected readonly celdaHoy = computed(() => !!this.celda(this.hoy, 'comida') || !!this.celda(this.hoy, 'cena'));

  // receta abierta
  protected readonly actual = signal<Receta | null>(null);
  protected readonly raciones = signal(4);
  protected readonly confirmar = signal(false);
  // formulario de receta
  protected readonly editId = signal<number | null>(null);
  protected readonly f = { nombre: signal(''), raciones: signal('4'), minutos: signal('0'), etiquetas: signal(''), ingredientes: signal(''), pasos: signal('') };
  protected readonly url = signal('');
  // casilla
  protected readonly celdaEd = signal<{ fecha: string; momento: 'comida' | 'cena' } | null>(null);
  protected readonly celdaReceta = signal<number | null>(null);
  protected readonly celdaTexto = signal('');
  // generar y ajustes
  protected readonly genEtiquetas = signal('');
  protected readonly genSobras = signal(1);
  protected readonly genReemplazar = signal(false);
  protected readonly ajAlergias = signal('');
  protected readonly ajHora = signal('');
  // cocinar
  protected readonly cocinando = signal<Receta | null>(null);
  protected readonly paso = signal(0);
  private bloqueo: { release(): Promise<void> } | null = null;

  constructor() { void this.cargar(); }

  private lunesDe(d: Date): Date { const l = new Date(d); l.setDate(l.getDate() - ((l.getDay() + 6) % 7)); return l; }
  protected max0(n: number): number { return Math.max(0, n); }
  protected max1(n: number): number { return Math.max(1, n); }
  protected fechaLarga(f?: string | null): string { return f ? new Date(`${f}T12:00:00`).toLocaleDateString('es-ES', { day: 'numeric', month: 'long' }) : ''; }

  private async cargar(): Promise<void> {
    try {
      const [m, r] = await Promise.all([firstValueFrom(this.http.get<Menu>(`/api/cocina/menu?desde=${iso(this.lunes)}`)), firstValueFrom(this.http.get<Receta[]>('/api/cocina/recetas'))]);
      this.menu.set(m); this.recetas.set(r);
    } catch { this.aviso.set('No se pudo cargar la cocina.'); }
  }

  protected semana(n: number): void { this.lunes = n === 0 ? this.lunesDe(new Date()) : new Date(this.lunes.getFullYear(), this.lunes.getMonth(), this.lunes.getDate() + 7 * n); void this.cargar(); }

  protected celda(fecha: string, m: string): Celda | undefined { return this.menu()?.celdas.find((c) => c.fecha === fecha && c.momento === m); }
  protected textoCelda(fecha: string, m: string): string { const c = this.celda(fecha, m); return c ? `${c.receta ?? c.texto}${c.sobras ? ' (sobras)' : ''}` : '—'; }
  protected ingrediente(i: Ingrediente, r: Receta): string {
    if (i.cantidad === null) return i.texto;
    const v = i.cantidad * this.raciones() / r.raciones;
    const n = v % 1 === 0 ? String(v) : v.toLocaleString('es-ES', { maximumFractionDigits: 2 });
    return `${n}${i.unidad ? ' ' + i.unidad : ''} ${i.nombre}`;
  }

  // ---- casillas ----
  protected editarCelda(fecha: string, momento: 'comida' | 'cena'): void {
    const c = this.celda(fecha, momento);
    this.celdaEd.set({ fecha, momento }); this.celdaReceta.set(c?.recetaId ?? null); this.celdaTexto.set(c?.recetaId ? '' : c?.texto ?? '');
    this.dlgCelda().nativeElement.showModal();
  }
  protected async guardarCelda(vaciar: boolean): Promise<void> {
    const e = this.celdaEd(); if (!e) return;
    try { await firstValueFrom(this.http.put(`/api/cocina/menu/${e.fecha}/${e.momento}`, vaciar ? {} : { recetaId: this.celdaReceta(), texto: this.celdaTexto() })); this.dlgCelda().nativeElement.close(); await this.cargar(); } catch { this.aviso.set('No se pudo guardar.'); }
  }

  // ---- generar y compra ----
  protected abrirGenerar(): void { this.error.set(''); this.dlgGen().nativeElement.showModal(); }
  protected async generar(): Promise<void> {
    this.error.set('');
    try {
      const r = await firstValueFrom(this.http.post<{ puestas: number }>('/api/cocina/menu/generar', { desde: iso(this.lunes), etiquetas: this.genEtiquetas().split(',').map((x) => x.trim()).filter(Boolean), sobras: this.genSobras(), reemplazar: this.genReemplazar() }));
      this.dlgGen().nativeElement.close(); this.aviso.set(`Menú generado: ${r.puestas} casillas.`); await this.cargar();
    } catch (e) { this.error.set((e as HttpErrorResponse).error?.detalle ?? 'No se pudo generar.'); }
  }
  protected async compra(): Promise<void> {
    try { const r = await firstValueFrom(this.http.post<{ añadidos: number; yaEstaban: number }>(`/api/cocina/menu/compra?desde=${iso(this.lunes)}`, null)); this.aviso.set(`A la lista de la compra: ${r.añadidos} cosas${r.yaEstaban ? ` (${r.yaEstaban} ya estaban pendientes)` : ''}.`); }
    catch (e) { this.aviso.set((e as HttpErrorResponse).error?.detalle ?? 'No se pudo mandar la lista.'); }
  }

  // ---- recetas ----
  protected verReceta(r: Receta): void { this.actual.set(r); this.raciones.set(r.raciones); this.confirmar.set(false); this.dlgReceta().nativeElement.showModal(); }
  protected nuevaReceta(): void { this.abrirForm(null); }
  protected editarReceta(r: Receta): void { this.dlgReceta().nativeElement.close(); this.abrirForm(r); }
  private abrirForm(r: Receta | null): void {
    this.editId.set(r?.id ?? null); this.error.set('');
    this.f.nombre.set(r?.nombre ?? ''); this.f.raciones.set(String(r?.raciones ?? 4)); this.f.minutos.set(String(r?.minutos ?? 0)); this.f.etiquetas.set(r?.etiquetas.join(', ') ?? '');
    this.f.ingredientes.set(r?.ingredientes.map((i) => i.texto).join('\n') ?? ''); this.f.pasos.set(r?.pasos.join('\n') ?? '');
    this.dlgForm().nativeElement.showModal();
  }
  protected async guardarReceta(e: Event): Promise<void> {
    e.preventDefault();
    if (!this.f.nombre().trim()) { this.error.set('Ponle un nombre.'); return; }
    // cada línea de ingredientes se reparte en cantidad, unidad y nombre en el servidor al leerla de la web; aquí se guarda el texto tal cual como nombre
    const ingredientes = this.f.ingredientes().split('\n').map((l) => l.trim()).filter(Boolean).map((l) => this.partir(l));
    const cuerpo = { nombre: this.f.nombre().trim(), raciones: +this.f.raciones() || 4, minutos: +this.f.minutos() || 0, etiquetas: this.f.etiquetas(), pasos: this.f.pasos(), ingredientes };
    try {
      const id = this.editId();
      if (id) await firstValueFrom(this.http.put(`/api/cocina/recetas/${id}`, cuerpo)); else await firstValueFrom(this.http.post('/api/cocina/recetas', cuerpo));
      this.dlgForm().nativeElement.close(); await this.cargar();
    } catch (x) { const er = (x as HttpErrorResponse).error?.errors as Record<string, string[]> | undefined; this.error.set(er ? Object.values(er)[0]?.[0] ?? 'Revisa los datos.' : 'No se pudo guardar.'); }
  }
  // «200 g de harina» → {cantidad 200, unidad g, nombre harina}; lo que no se entiende se queda como nombre
  private partir(l: string): { nombre: string; cantidad: number | null; unidad: string } {
    const m = /^(\d+(?:[.,]\d+)?)\s*(kg|g|ml|l|cucharadas?|cucharaditas?|dientes?)?\s*(?:de\s+)?(.+)$/i.exec(l);
    if (!m) return { nombre: l, cantidad: null, unidad: '' };
    let c = parseFloat(m[1].replace(',', '.')); let u = (m[2] ?? '').toLowerCase().replace(/s$/, '');
    if (u === 'kg') { c *= 1000; u = 'g'; } else if (u === 'l') { c *= 1000; u = 'ml'; } else if (u === 'cucharada') u = 'cucharada';
    return { nombre: m[3].trim(), cantidad: c, unidad: u };
  }
  protected async borrarReceta(r: Receta): Promise<void> {
    if (!this.confirmar()) { this.confirmar.set(true); return; }
    try { await firstValueFrom(this.http.delete(`/api/cocina/recetas/${r.id}`)); this.dlgReceta().nativeElement.close(); await this.cargar(); } catch { this.aviso.set('No se pudo borrar.'); }
  }

  protected abrirWeb(): void { this.url.set(''); this.error.set(''); this.dlgWeb().nativeElement.showModal(); }
  protected async leerWeb(e: Event): Promise<void> {
    e.preventDefault(); this.error.set(''); this.ocupado.set(true);
    try { await firstValueFrom(this.http.post('/api/cocina/recetas/desde-web', { url: this.url().trim() })); this.dlgWeb().nativeElement.close(); this.aviso.set('Receta traída. Revísala y corrige lo que haga falta.'); await this.cargar(); }
    catch (x) { const er = (x as HttpErrorResponse).error?.errors as Record<string, string[]> | undefined; this.error.set(er?.['url']?.[0] ?? 'No se pudo leer esa página.'); }
    finally { this.ocupado.set(false); }
  }

  // ---- ajustes ----
  protected async abrirAjustes(): Promise<void> {
    try { const a = await firstValueFrom(this.http.get<Ajustes>('/api/cocina/ajustes')); this.ajAlergias.set((a.alergias ?? []).join(', ')); this.ajHora.set(a.horaAviso ?? ''); } catch { /* vacío */ }
    this.dlgAj().nativeElement.showModal();
  }
  protected async guardarAjustes(): Promise<void> {
    try { await firstValueFrom(this.http.put('/api/cocina/ajustes', { alergias: this.ajAlergias().split(',').map((x) => x.trim()).filter(Boolean), horaAviso: this.ajHora() })); this.dlgAj().nativeElement.close(); this.aviso.set('Guardado.'); } catch { this.aviso.set('No se pudo guardar.'); }
  }

  // ---- cocinar ----
  protected cocinarHoy(): void {
    const c = this.celda(this.hoy, 'cena') ?? this.celda(this.hoy, 'comida');
    const r = this.recetas().find((x) => x.id === c?.recetaId);
    if (r) this.cocinar(r); else this.aviso.set('Hoy no hay una receta con pasos en el menú.');
  }
  protected async cocinar(r: Receta): Promise<void> {
    this.dlgReceta().nativeElement.close();
    this.cocinando.set(r); this.paso.set(0);
    this.dlgCocinar().nativeElement.showModal();
    try { const wl = (navigator as unknown as { wakeLock?: { request(t: 'screen'): Promise<{ release(): Promise<void> }> } }).wakeLock; this.bloqueo = (await wl?.request('screen')) ?? null; } catch { /* sin bloqueo de pantalla: se apagará sola */ }
  }
  protected soltarPantalla(): void { void this.bloqueo?.release().catch(() => undefined); this.bloqueo = null; }

  protected fuera(e: MouseEvent, d: HTMLDialogElement): void { if (e.target === d) d.close(); }
  ngOnDestroy(): void { this.soltarPantalla(); }
}
