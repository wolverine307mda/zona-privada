import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { EstadoError, EstadoVacio, Esqueleto } from '../ui';
import { Garantias } from './garantias';

interface Papel { id: number; nombre: string; categoria: string; vence: string; avisarDias: number; notas: string; dias: number; estado: 'bien' | 'pronto' | 'caducado'; }
interface Respuesta { categorias: string[]; papeles: Papel[]; }

// Papeles que caducan: DNI, ITV, seguro del coche, garantías, carné… Avisa (app y Telegram) al entrar en su plazo, a la semana y al caducar.
// Para renovar uno, edítalo y cambia la fecha: los avisos vuelven a empezar. Por Telegram: /caducidades.
@Component({
  selector: 'app-papeles',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [EstadoError, EstadoVacio, Esqueleto, Garantias],
  styleUrls: ['../gastos/comun.css', '../gastos/dialogos.css'],
  template: `
    <header class="cab">
      <p class="cab__sub" aria-live="polite">{{ resumen() }}</p>
      <button class="button" type="button" (click)="nuevo()">Nuevo papel</button>
    </header>

    @if (fallo()) { <app-estado-error titulo="No pude cargar los papeles" (reintentar)="cargar()" /> }
    @else if (datos() === null) { <section class="caja"><app-esqueleto forma="bloque" alto="64px" /><app-esqueleto forma="bloque" alto="64px" /></section> }
    @else if (datos()!.papeles.length === 0) {
      <app-estado-vacio icono="M7 3h8l4 4v14H7zM14 3v5h5M10 13h6M10 17h6" titulo="Aún no hay papeles" texto="Apunta el DNI, la ITV, el seguro, una garantía… y te aviso antes de que caduquen.">
        <button class="button" type="button" (click)="nuevo()">Apuntar el primero</button>
      </app-estado-vacio>
    } @else {
      <ul class="lista" aria-label="Papeles">
        @for (p of datos()!.papeles; track p.id) {
          <li>
            <button type="button" class="papel" [attr.data-e]="p.estado" (click)="editar(p)" [attr.aria-label]="'Editar ' + p.nombre">
              <span class="papel__cat">{{ p.categoria }}</span>
              <strong>{{ p.nombre }}</strong>
              <small>{{ fecha(p.vence) }}</small>
              <span class="papel__dias">{{ cuando(p) }}</span>
            </button>
          </li>
        }
      </ul>
    }

    <app-garantias />

    <dialog class="dlg" #dialogo (click)="fuera($event)" (close)="cerrado()" aria-labelledby="dlg-papel">
      <form class="dlg__caja" (submit)="guardar($event)" novalidate>
        <header class="dlg__cabeza">
          <h2 id="dlg-papel">{{ editando() ? 'Editar papel' : 'Nuevo papel' }}</h2>
          <button class="dlg__x" type="button" (click)="cerrar()" aria-label="Cerrar"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6l12 12M18 6 6 18" /></svg></button>
        </header>
        <label class="campo"><span class="campo__etiqueta">Nombre</span>
          <input class="campo__input" type="text" maxlength="80" placeholder="DNI, ITV del coche, seguro del hogar…" [value]="nombre()" (input)="nombre.set(valor($event))"></label>
        <div class="fila2">
          <label class="campo"><span class="campo__etiqueta">Categoría</span>
            <select class="campo__input" [value]="categoria()" (change)="categoria.set(valor($event))">
              @for (c of datos()?.categorias ?? []; track c) { <option [value]="c">{{ c }}</option> }
            </select></label>
          <label class="campo"><span class="campo__etiqueta">Caduca el</span>
            <input class="campo__input" type="date" [value]="vence()" (input)="vence.set(valor($event))"></label>
        </div>
        <label class="campo"><span class="campo__etiqueta">Avisarme con <em>(días de antelación)</em></span>
          <input class="campo__input" type="number" inputmode="numeric" min="1" max="365" [value]="avisar()" (input)="avisar.set(valor($event))"></label>
        <label class="campo"><span class="campo__etiqueta">Notas <em>(opcional: dónde está, a quién llamar…)</em></span>
          <textarea class="campo__input" rows="3" maxlength="500" [value]="notas()" (input)="notas.set(valor($event))"></textarea></label>
        @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
        <footer class="dlg__pie">
          @if (editando()) { <button type="button" class="boton-borrar" (click)="borrar()">{{ confirmarBorrado() ? 'Seguro: borrar' : 'Borrar' }}</button> }
          <button class="button" type="submit" [attr.aria-busy]="guardando() || null" [disabled]="guardando()">Guardar</button>
        </footer>
      </form>
    </dialog>
  `,
  styles: `
    .cab__sub { margin: 0; font: var(--peso-ligero) 1.3rem/1.2 var(--font-text); }
    .cab { display: flex; align-items: center; justify-content: space-between; gap: 12px; flex-wrap: wrap; margin-bottom: var(--esp-4); }
    .lista { list-style: none; margin: 0; padding: 0; display: grid; gap: 8px; }
    .papel { width: 100%; display: grid; grid-template-columns: minmax(0, 1fr) auto; grid-template-areas: 'c d' 'n d' 'f d'; gap: 2px 12px; align-items: center; padding: 12px 16px; border-radius: var(--radio-l); border: 1px solid var(--vidrio-borde); background: var(--vidrio); color: inherit; font: inherit; text-align: left; cursor: pointer; }
    .papel[data-e='pronto'] { border-color: color-mix(in srgb, var(--acento) 55%, transparent); }
    .papel[data-e='caducado'] { border-color: color-mix(in srgb, var(--bad) 60%, transparent); }
    .papel__cat { grid-area: c; color: var(--soft); font-size: .72rem; text-transform: uppercase; letter-spacing: .06em; }
    .papel strong { grid-area: n; font-weight: 500; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .papel small { grid-area: f; color: var(--soft); font-size: .78rem; }
    .papel__dias { grid-area: d; padding: 4px 12px; border-radius: 999px; font-size: .8rem; background: color-mix(in srgb, var(--ok) 20%, transparent); }
    .papel[data-e='pronto'] .papel__dias { background: color-mix(in srgb, var(--acento) 25%, transparent); }
    .papel[data-e='caducado'] .papel__dias { background: color-mix(in srgb, var(--bad) 30%, transparent); }
  `,
})
export class Papeles {
  private readonly http = inject(HttpClient);
  private readonly dialogo = viewChild.required<ElementRef<HTMLDialogElement>>('dialogo');

  protected readonly datos = signal<Respuesta | null>(null);
  protected readonly fallo = signal(false);

  protected readonly editando = signal<Papel | null>(null);
  protected readonly nombre = signal('');
  protected readonly categoria = signal('Otro');
  protected readonly vence = signal('');
  protected readonly avisar = signal('30');
  protected readonly notas = signal('');
  protected readonly error = signal('');
  protected readonly guardando = signal(false);
  protected readonly confirmarBorrado = signal(false);

  protected readonly resumen = computed(() => {
    const l = this.datos()?.papeles;
    if (!l) return 'Lo que caduca, antes de que caduque';
    if (l.length === 0) return 'Nada apuntado todavía';
    const caducados = l.filter((p) => p.estado === 'caducado').length;
    const pronto = l.filter((p) => p.estado === 'pronto').length;
    if (caducados) return `${caducados} caducado${caducados === 1 ? '' : 's'}${pronto ? ` y ${pronto} por caducar` : ''}`;
    return pronto ? `${pronto} por caducar pronto` : 'Todo en regla';
  });

  constructor() { void this.cargar(); }

  protected async cargar(): Promise<void> {
    this.fallo.set(false);
    try { this.datos.set(await firstValueFrom(this.http.get<Respuesta>('/api/papeles'))); } catch { this.fallo.set(true); }
  }

  protected fecha(iso: string): string { return new Date(`${iso}T00:00:00`).toLocaleDateString('es-ES', { day: 'numeric', month: 'long', year: 'numeric' }); }

  protected cuando(p: Papel): string {
    const d = p.dias;
    if (d < 0) return `Caducó hace ${-d} d`;
    if (d === 0) return 'Caduca hoy';
    if (d === 1) return 'Mañana';
    if (d < 60) return `En ${d} días`;
    if (d < 730) return `En ${Math.round(d / 30)} meses`;
    return `En ${Math.round(d / 365)} años`;
  }

  // ---- diálogo ----
  protected nuevo(): void { this.abrir(null); }
  protected editar(p: Papel): void { this.abrir(p); }

  private abrir(p: Papel | null): void {
    this.editando.set(p);
    this.nombre.set(p?.nombre ?? ''); this.categoria.set(p?.categoria ?? 'Otro'); this.vence.set(p?.vence ?? '');
    this.avisar.set(String(p?.avisarDias ?? 30)); this.notas.set(p?.notas ?? '');
    this.error.set(''); this.confirmarBorrado.set(false);
    this.dialogo().nativeElement.showModal();
  }

  protected cerrar(): void { this.dialogo().nativeElement.close(); }
  protected cerrado(): void { this.editando.set(null); }
  protected fuera(e: MouseEvent): void { if (e.target === this.dialogo().nativeElement) this.cerrar(); }
  protected valor(e: Event): string { return (e.target as HTMLInputElement).value; }

  protected async guardar(ev: Event): Promise<void> {
    ev.preventDefault();
    if (this.guardando()) return;
    const avisarDias = Number(this.avisar());
    if (!this.nombre().trim()) { this.error.set('Ponle un nombre.'); return; }
    if (!this.vence()) { this.error.set('Pon la fecha de caducidad.'); return; }
    if (!Number.isInteger(avisarDias) || avisarDias < 1 || avisarDias > 365) { this.error.set('El aviso va de 1 a 365 días antes.'); return; }
    this.guardando.set(true); this.error.set('');
    const cuerpo = { nombre: this.nombre().trim(), categoria: this.categoria(), vence: this.vence(), avisarDias, notas: this.notas().trim() };
    try {
      const e = this.editando();
      if (e) await firstValueFrom(this.http.put(`/api/papeles/${e.id}`, cuerpo));
      else await firstValueFrom(this.http.post('/api/papeles', cuerpo));
      this.cerrar();
      await this.cargar();
    } catch (x) { this.error.set(this.mensaje(x)); } finally { this.guardando.set(false); }
  }

  protected async borrar(): Promise<void> {
    const e = this.editando(); if (!e) return;
    if (!this.confirmarBorrado()) { this.confirmarBorrado.set(true); return; }
    try { await firstValueFrom(this.http.delete(`/api/papeles/${e.id}`)); this.cerrar(); await this.cargar(); } catch (x) { this.error.set(this.mensaje(x)); }
  }

  private mensaje(x: unknown): string {
    const e = x as HttpErrorResponse;
    const errores = e.error?.errors as Record<string, string[]> | undefined;
    return (errores ? Object.values(errores).flat()[0] : null) ?? 'No se ha podido guardar.';
  }
}
