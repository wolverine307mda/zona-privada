import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { EstadoError, EstadoVacio, Esqueleto } from '../ui';

interface Tarea {
  id: number; titulo: string; notas: string; prioridad: number; vence: string | null; hecha: boolean;
  vencida: boolean; hoy: boolean; dias: number | null; hechaEn: string | null;
}

const PRIORIDADES = ['Baja', 'Normal', 'Alta'];

// Tareas por hacer: cosas sueltas con o sin fecha límite. Las vencidas y las de hoy van arriba y avisan una vez al día (app y Telegram).
// Rápido: escribe y pulsa Intro. Por Telegram: «/tarea llamar al seguro», «/tareas» y «/listo 12».
@Component({
  selector: 'app-tareas',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [EstadoError, EstadoVacio, Esqueleto],
  styleUrls: ['../gastos/comun.css', '../gastos/dialogos.css'],
  template: `
    <header class="cab">
      <p class="cab__sub" aria-live="polite">{{ resumen() }}</p>
      <div class="pestanas" role="tablist" aria-label="Qué tareas ver">
        <button type="button" role="tab" [attr.aria-selected]="!verHechas()" (click)="cambiar(false)">Pendientes</button>
        <button type="button" role="tab" [attr.aria-selected]="verHechas()" (click)="cambiar(true)">Hechas</button>
      </div>
    </header>

    @if (!verHechas()) {
      <form class="rapida" (submit)="agregar($event)">
        <input class="campo__input" type="text" maxlength="120" placeholder="Nueva tarea… (Intro para añadir)" aria-label="Nueva tarea" [value]="rapida()" (input)="rapida.set(valor($event))">
        <button class="button" type="submit" [disabled]="!rapida().trim() || guardando()">Añadir</button>
      </form>
    }

    @if (fallo()) { <app-estado-error titulo="No pude cargar las tareas" (reintentar)="cargar()" /> }
    @else if (lista() === null) { <section class="caja"><app-esqueleto forma="bloque" alto="56px" /><app-esqueleto forma="bloque" alto="56px" /></section> }
    @else if (lista()!.length === 0) {
      <app-estado-vacio icono="M5 12.5l4.2 4.2L19 7" [titulo]="verHechas() ? 'Aún no has terminado ninguna' : 'Nada pendiente'" [texto]="verHechas() ? 'Las tareas que completes aparecerán aquí 90 días.' : 'Apunta arriba lo que tengas por hacer.'" />
    } @else {
      <ul class="lista" [attr.aria-label]="verHechas() ? 'Tareas hechas' : 'Tareas pendientes'">
        @for (t of lista()!; track t.id) {
          <li class="tarea" [class.tarea--hecha]="t.hecha" [class.tarea--vencida]="t.vencida">
            <button type="button" class="check" [class.check--hecha]="t.hecha" [attr.aria-pressed]="t.hecha" [attr.aria-label]="(t.hecha ? 'Reabrir ' : 'Completar ') + t.titulo" [disabled]="ocupado() === t.id" (click)="alternar(t)">
              <svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M5 12.5l4.2 4.2L19 7" /></svg>
            </button>
            <button type="button" class="tarea__texto" (click)="editar(t)" [attr.aria-label]="'Editar ' + t.titulo">
              <strong>{{ t.titulo }}</strong>
              @if (t.notas) { <small>{{ t.notas }}</small> }
            </button>
            @if (t.prioridad === 2 && !t.hecha) { <span class="chip chip--alta" title="Prioridad alta">Alta</span> }
            @if (t.vence && !t.hecha) { <span class="chip" [class.chip--mal]="t.vencida" [class.chip--hoy]="t.hoy">{{ cuando(t) }}</span> }
          </li>
        }
      </ul>
      @if (verHechas()) { <button type="button" class="button button--suave vaciar" (click)="vaciar()">{{ confirmarVaciar() ? 'Seguro: vaciar la lista' : 'Vaciar hechas' }}</button> }
    }

    <dialog class="dlg" #dialogo (click)="fuera($event)" (close)="cerrado()" aria-labelledby="dlg-tarea">
      <form class="dlg__caja" (submit)="guardar($event)" novalidate>
        <header class="dlg__cabeza">
          <h2 id="dlg-tarea">Editar tarea</h2>
          <button class="dlg__x" type="button" (click)="cerrar()" aria-label="Cerrar"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6l12 12M18 6 6 18" /></svg></button>
        </header>
        <label class="campo"><span class="campo__etiqueta">Título</span>
          <input class="campo__input" type="text" maxlength="120" [value]="titulo()" (input)="titulo.set(valor($event))"></label>
        <label class="campo"><span class="campo__etiqueta">Notas <em>(opcional)</em></span>
          <textarea class="campo__input" rows="3" maxlength="1000" [value]="notas()" (input)="notas.set(valor($event))"></textarea></label>
        <div class="fila2">
          <label class="campo"><span class="campo__etiqueta">Fecha límite <em>(opcional)</em></span>
            <input class="campo__input" type="date" [value]="vence()" (input)="vence.set(valor($event))"></label>
          <label class="campo"><span class="campo__etiqueta">Prioridad</span>
            <select class="campo__input" [value]="prioridad()" (change)="prioridad.set(valor($event))">
              @for (p of prioridades; track $index) { <option [value]="$index">{{ p }}</option> }
            </select></label>
        </div>
        @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
        <footer class="dlg__pie">
          <button type="button" class="boton-borrar" (click)="borrar()">{{ confirmarBorrado() ? 'Seguro: borrar' : 'Borrar' }}</button>
          <button class="button" type="submit" [attr.aria-busy]="guardando() || null" [disabled]="guardando()">Guardar</button>
        </footer>
      </form>
    </dialog>
  `,
  styles: `
    .cab__sub { margin: 0; font: var(--peso-ligero) 1.3rem/1.2 var(--font-text); }
    .cab { display: flex; align-items: center; justify-content: space-between; gap: 12px; flex-wrap: wrap; margin-bottom: var(--esp-4); }
    .pestanas { display: inline-flex; gap: 4px; padding: 4px; border-radius: 999px; background: color-mix(in srgb, var(--texto) 7%, transparent); }
    .pestanas button { min-height: 40px; padding: 0 16px; border: 0; border-radius: 999px; background: transparent; color: var(--soft); font: inherit; cursor: pointer; }
    .pestanas button[aria-selected='true'] { background: var(--brand); color: var(--on-brand); }
    .rapida { display: flex; gap: 10px; margin-bottom: var(--esp-4); }
    .rapida input { flex: 1; min-width: 0; }
    .lista { list-style: none; margin: 0; padding: 0; display: grid; gap: 8px; }
    .tarea { display: flex; align-items: center; gap: 12px; padding: 10px 14px; border-radius: var(--radio-l); border: 1px solid var(--vidrio-borde); background: var(--vidrio); }
    .tarea--vencida { border-color: color-mix(in srgb, var(--bad) 50%, transparent); }
    .tarea--hecha .tarea__texto strong { text-decoration: line-through; color: var(--soft); }
    .check { flex: none; display: grid; place-items: center; width: 44px; height: 44px; border-radius: 50%; border: 2px solid var(--linea); background: transparent; color: transparent; cursor: pointer; transition: background var(--dur-2) var(--ease-expo), border-color var(--dur-2), transform var(--dur-1); }
    .check .icon { width: 22px; height: 22px; stroke-width: 2.4; }
    .check:active { transform: scale(.92); }
    .check--hecha { background: var(--ok); border-color: var(--ok); color: var(--on-negra, #05210f); }
    .tarea__texto { flex: 1; display: grid; gap: 2px; min-width: 0; padding: 4px 0; border: 0; background: none; color: inherit; font: inherit; text-align: left; cursor: pointer; }
    .tarea__texto strong { font-weight: 500; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .tarea__texto small { color: var(--soft); font-size: .78rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .chip { flex: none; padding: 3px 10px; border-radius: 999px; font-size: .75rem; background: color-mix(in srgb, var(--texto) 8%, transparent); color: var(--soft); }
    .chip--alta { background: color-mix(in srgb, var(--acento) 22%, transparent); color: var(--texto); }
    .chip--hoy { background: color-mix(in srgb, var(--brand) 25%, transparent); color: var(--texto); }
    .chip--mal { background: color-mix(in srgb, var(--bad) 28%, transparent); color: var(--texto); }
    .vaciar { margin-top: var(--esp-4); }
    @media (prefers-reduced-motion: reduce) { .check { transition: none; } }
  `,
})
export class Tareas {
  private readonly http = inject(HttpClient);
  private readonly dialogo = viewChild.required<ElementRef<HTMLDialogElement>>('dialogo');

  protected readonly prioridades = PRIORIDADES;
  protected readonly lista = signal<Tarea[] | null>(null);
  protected readonly fallo = signal(false);
  protected readonly verHechas = signal(false);
  protected readonly ocupado = signal<number | null>(null);
  protected readonly rapida = signal('');

  protected readonly editando = signal<Tarea | null>(null);
  protected readonly titulo = signal('');
  protected readonly notas = signal('');
  protected readonly vence = signal('');
  protected readonly prioridad = signal('1');
  protected readonly error = signal('');
  protected readonly guardando = signal(false);
  protected readonly confirmarBorrado = signal(false);
  protected readonly confirmarVaciar = signal(false);

  protected readonly resumen = computed(() => {
    const l = this.lista();
    if (!l) return 'Lo que tienes por hacer';
    if (this.verHechas()) return l.length ? `${l.length} hecha${l.length === 1 ? '' : 's'} recientemente` : 'Nada terminado todavía';
    if (l.length === 0) return 'Todo al día 🎉';
    const vencidas = l.filter((t) => t.vencida).length;
    const hoy = l.filter((t) => t.hoy).length;
    const partes = [`${l.length} pendiente${l.length === 1 ? '' : 's'}`];
    if (vencidas) partes.push(`${vencidas} vencida${vencidas === 1 ? '' : 's'}`);
    if (hoy) partes.push(`${hoy} para hoy`);
    return partes.join(' · ');
  });

  constructor() { void this.cargar(); }

  protected async cargar(): Promise<void> {
    this.fallo.set(false);
    try { this.lista.set(await firstValueFrom(this.http.get<Tarea[]>('/api/tareas', { params: { hechas: this.verHechas() } }))); } catch { this.fallo.set(true); }
  }

  protected cambiar(hechas: boolean): void {
    if (this.verHechas() === hechas) return;
    this.verHechas.set(hechas); this.lista.set(null); this.confirmarVaciar.set(false);
    void this.cargar();
  }

  protected cuando(t: Tarea): string {
    const d = t.dias ?? 0;
    if (d === 0) return 'Hoy';
    if (d === 1) return 'Mañana';
    if (d === -1) return 'Ayer';
    if (d < 0) return `Hace ${-d} días`;
    if (d < 7) return `En ${d} días`;
    const f = new Date(`${t.vence}T00:00:00`);
    return f.toLocaleDateString('es-ES', { day: 'numeric', month: 'short' });
  }

  protected async agregar(ev: Event): Promise<void> {
    ev.preventDefault();
    const titulo = this.rapida().trim();
    if (!titulo || this.guardando()) return;
    this.guardando.set(true);
    try { await firstValueFrom(this.http.post('/api/tareas', { titulo })); this.rapida.set(''); await this.cargar(); } catch { this.fallo.set(true); } finally { this.guardando.set(false); }
  }

  protected async alternar(t: Tarea): Promise<void> {
    if (this.ocupado() !== null) return;
    this.ocupado.set(t.id);
    try {
      await firstValueFrom(this.http.put(`/api/tareas/${t.id}`, { hecha: !t.hecha }));
      this.lista.update((l) => l?.filter((x) => x.id !== t.id) ?? null);   // pasa a la otra pestaña
    } catch { void this.cargar(); } finally { this.ocupado.set(null); }
  }

  protected async vaciar(): Promise<void> {
    if (!this.confirmarVaciar()) { this.confirmarVaciar.set(true); return; }
    try { await firstValueFrom(this.http.delete('/api/tareas/hechas')); this.confirmarVaciar.set(false); await this.cargar(); } catch { this.fallo.set(true); }
  }

  // ---- diálogo ----
  protected editar(t: Tarea): void {
    this.editando.set(t);
    this.titulo.set(t.titulo); this.notas.set(t.notas); this.vence.set(t.vence ?? ''); this.prioridad.set(String(t.prioridad));
    this.error.set(''); this.confirmarBorrado.set(false);
    this.dialogo().nativeElement.showModal();
  }

  protected cerrar(): void { this.dialogo().nativeElement.close(); }
  protected cerrado(): void { this.editando.set(null); }
  protected fuera(e: MouseEvent): void { if (e.target === this.dialogo().nativeElement) this.cerrar(); }
  protected valor(e: Event): string { return (e.target as HTMLInputElement).value; }

  protected async guardar(ev: Event): Promise<void> {
    ev.preventDefault();
    const e = this.editando();
    if (!e || this.guardando()) return;
    if (!this.titulo().trim()) { this.error.set('Ponle un título.'); return; }
    this.guardando.set(true); this.error.set('');
    const cuerpo = { titulo: this.titulo().trim(), notas: this.notas().trim(), vence: this.vence(), prioridad: Number(this.prioridad()) };
    try { await firstValueFrom(this.http.put(`/api/tareas/${e.id}`, cuerpo)); this.cerrar(); await this.cargar(); } catch (x) { this.error.set(this.mensaje(x)); } finally { this.guardando.set(false); }
  }

  protected async borrar(): Promise<void> {
    const e = this.editando(); if (!e) return;
    if (!this.confirmarBorrado()) { this.confirmarBorrado.set(true); return; }
    try { await firstValueFrom(this.http.delete(`/api/tareas/${e.id}`)); this.cerrar(); await this.cargar(); } catch (x) { this.error.set(this.mensaje(x)); }
  }

  private mensaje(x: unknown): string {
    const e = x as HttpErrorResponse;
    const errores = e.error?.errors as Record<string, string[]> | undefined;
    return (errores ? Object.values(errores).flat()[0] : null) ?? 'No se ha podido guardar.';
  }
}
