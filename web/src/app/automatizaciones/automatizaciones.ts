import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { EstadoError, EstadoVacio, Esqueleto } from '../ui';

interface Disparador { clave: string; etiqueta: string; unidad: string; mayor: boolean; ayuda: string; }
interface Regla {
  id: number; nombre: string; activa: boolean; disparador: string; etiqueta: string; unidad: string; mayor: boolean; umbral: number;
  accion: 'avisar' | 'tarea'; texto: string; esperaHoras: number; ultima: string | null; ultimoValor: number | null; ahora: number | null; cumple: boolean;
}
interface Respuesta { disparadores: Disparador[]; reglas: Regla[]; }

// «Si pasa X, haz Y»: cada 5 minutos se mira el dato de cada regla (disco, gasto del mes, tareas vencidas…) y, si se cumple, te avisa por la app y Telegram
// o apunta una tarea. Entre una vez y la siguiente espera las horas que digas. «Probar» hace la acción ya, sin esperar a que se cumpla.
@Component({
  selector: 'app-automatizaciones',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [EstadoError, EstadoVacio, Esqueleto],
  styleUrls: ['../gastos/comun.css', '../gastos/dialogos.css'],
  template: `
    <header class="cab">
      <p class="cab__sub" aria-live="polite">{{ resumen() }}</p>
      <button class="button" type="button" (click)="nueva()">Nueva regla</button>
    </header>
    @if (aviso()) { <p class="ok" role="status">{{ aviso() }}</p> }

    @if (fallo()) { <app-estado-error titulo="No pude cargar las automatizaciones" (reintentar)="cargar()" /> }
    @else if (datos() === null) { <section class="caja"><app-esqueleto forma="bloque" alto="72px" /><app-esqueleto forma="bloque" alto="72px" /></section> }
    @else if (datos()!.reglas.length === 0) {
      <app-estado-vacio icono="M13 2 4 14h7l-1 8 9-12h-7z" titulo="Aún no hay reglas" texto="Por ejemplo: «si el disco pasa del 90 %, avísame» o «si gasto más de 800 € este mes, apunta una tarea».">
        <button class="button" type="button" (click)="nueva()">Crear la primera</button>
      </app-estado-vacio>
    } @else {
      <ul class="lista" aria-label="Automatizaciones">
        @for (r of datos()!.reglas; track r.id) {
          <li class="regla" [class.regla--off]="!r.activa" [class.regla--salta]="r.activa && r.cumple">
            <label class="interruptor" [title]="r.activa ? 'Activa' : 'Apagada'">
              <input type="checkbox" [checked]="r.activa" (change)="alternar(r, $any($event.target).checked)" [attr.aria-label]="'Activar ' + r.nombre">
              <span class="interruptor__pista" aria-hidden="true"></span>
            </label>
            <button type="button" class="regla__texto" (click)="editar(r)" [attr.aria-label]="'Editar ' + r.nombre">
              <strong>{{ r.nombre }}</strong>
              <small>Si {{ r.etiqueta.toLowerCase() }} {{ r.mayor ? 'llega a' : 'baja de' }} {{ r.umbral }} {{ r.unidad }} → {{ r.accion === 'tarea' ? 'apunta una tarea' : 'avisa' }}</small>
              <small>Ahora: {{ r.ahora === null ? 'sin datos' : (redondo(r.ahora) + ' ' + r.unidad) }}@if (r.ultima) { · última vez {{ hora(r.ultima) }} }</small>
            </button>
            <button type="button" class="button button--ghost button--sm" [disabled]="probando() === r.id" (click)="probar(r)">Probar</button>
          </li>
        }
      </ul>
    }

    <dialog class="dlg" #dialogo (click)="fuera($event)" (close)="cerrado()" aria-labelledby="dlg-regla">
      <form class="dlg__caja" (submit)="guardar($event)" novalidate>
        <header class="dlg__cabeza">
          <h2 id="dlg-regla">{{ editando() ? 'Editar regla' : 'Nueva regla' }}</h2>
          <button class="dlg__x" type="button" (click)="cerrar()" aria-label="Cerrar"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6l12 12M18 6 6 18" /></svg></button>
        </header>
        <label class="campo"><span class="campo__etiqueta">Nombre</span>
          <input class="campo__input" type="text" maxlength="60" placeholder="Disco casi lleno" [value]="nombre()" (input)="nombre.set(valor($event))"></label>
        <label class="campo"><span class="campo__etiqueta">Qué vigilar</span>
          <select class="campo__input" [value]="disparador()" (change)="disparador.set(valor($event))">
            @for (d of datos()?.disparadores ?? []; track d.clave) { <option [value]="d.clave">{{ d.etiqueta }}</option> }
          </select></label>
        <div class="fila2">
          <label class="campo"><span class="campo__etiqueta">Cuando {{ actual()?.mayor === false ? 'baje de' : 'llegue a' }} <em>({{ actual()?.unidad }})</em></span>
            <input class="campo__input" type="number" inputmode="decimal" min="0" step="any" [value]="umbral()" (input)="umbral.set(valor($event))"></label>
          <label class="campo"><span class="campo__etiqueta">Esperar entre avisos <em>(horas)</em></span>
            <input class="campo__input" type="number" inputmode="numeric" min="1" max="720" [value]="espera()" (input)="espera.set(valor($event))"></label>
        </div>
        <label class="campo"><span class="campo__etiqueta">Qué hacer</span>
          <select class="campo__input" [value]="accion()" (change)="accion.set(valor($event))">
            <option value="avisar">Avisarme (app y Telegram)</option>
            <option value="tarea">Apuntar una tarea para hoy</option>
          </select></label>
        <label class="campo"><span class="campo__etiqueta">Texto <em>(opcional: el aviso o el título de la tarea)</em></span>
          <input class="campo__input" type="text" maxlength="200" [value]="texto()" (input)="texto.set(valor($event))"></label>
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
    .ok { margin: 0 0 var(--esp-3); padding: 10px 14px; border-radius: var(--radio-m, 12px); background: color-mix(in srgb, var(--ok) 18%, transparent); }
    .lista { list-style: none; margin: 0; padding: 0; display: grid; gap: 8px; }
    .regla { display: grid; grid-template-columns: auto minmax(0, 1fr) auto; align-items: center; gap: 12px; padding: 12px 16px; border-radius: var(--radio-l); border: 1px solid var(--vidrio-borde); background: var(--vidrio); }
    .regla--off { opacity: .6; }
    .regla--salta { border-color: color-mix(in srgb, var(--acento) 60%, transparent); }
    .regla__texto { display: grid; gap: 2px; min-width: 0; padding: 0; border: 0; background: none; color: inherit; font: inherit; text-align: left; cursor: pointer; }
    .regla__texto strong { font-weight: 500; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .regla__texto small { color: var(--soft); font-size: .78rem; }
  `,
})
export class Automatizaciones {
  private readonly http = inject(HttpClient);
  private readonly dialogo = viewChild.required<ElementRef<HTMLDialogElement>>('dialogo');

  protected readonly datos = signal<Respuesta | null>(null);
  protected readonly fallo = signal(false);
  protected readonly aviso = signal('');
  protected readonly probando = signal<number | null>(null);

  protected readonly editando = signal<Regla | null>(null);
  protected readonly nombre = signal('');
  protected readonly disparador = signal('disco');
  protected readonly umbral = signal('90');
  protected readonly espera = signal('24');
  protected readonly accion = signal<string>('avisar');
  protected readonly texto = signal('');
  protected readonly error = signal('');
  protected readonly guardando = signal(false);
  protected readonly confirmarBorrado = signal(false);

  protected readonly actual = computed(() => this.datos()?.disparadores.find((d) => d.clave === this.disparador()));
  protected readonly resumen = computed(() => {
    const l = this.datos()?.reglas;
    if (!l) return 'Si pasa esto, haz aquello';
    if (l.length === 0) return 'Sin reglas todavía';
    const salta = l.filter((r) => r.activa && r.cumple).length;
    return `${l.filter((r) => r.activa).length} activa${l.filter((r) => r.activa).length === 1 ? '' : 's'}${salta ? ` · ${salta} cumpliéndose ahora` : ''}`;
  });

  constructor() { void this.cargar(); }

  protected async cargar(): Promise<void> {
    this.fallo.set(false);
    try { this.datos.set(await firstValueFrom(this.http.get<Respuesta>('/api/automatizaciones'))); } catch { this.fallo.set(true); }
  }

  protected redondo(n: number): string { return n.toLocaleString('es-ES', { maximumFractionDigits: 1 }); }
  protected hora(iso: string): string { return new Date(iso).toLocaleString('es-ES', { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' }); }

  protected async alternar(r: Regla, activa: boolean): Promise<void> {
    try { await firstValueFrom(this.http.put(`/api/automatizaciones/${r.id}`, { activa })); await this.cargar(); } catch { void this.cargar(); }
  }

  protected async probar(r: Regla): Promise<void> {
    if (this.probando() !== null) return;
    this.probando.set(r.id); this.aviso.set('');
    try {
      const x = await firstValueFrom(this.http.post<{ texto: string }>(`/api/automatizaciones/${r.id}/probar`, {}));
      this.aviso.set(r.accion === 'tarea' ? `Tarea apuntada: ${x.texto}` : `Aviso enviado: ${x.texto}`);
    } catch { this.aviso.set('No se pudo probar.'); } finally { this.probando.set(null); }
  }

  // ---- diálogo ----
  protected nueva(): void { this.abrir(null); }
  protected editar(r: Regla): void { this.abrir(r); }

  private abrir(r: Regla | null): void {
    this.editando.set(r);
    this.nombre.set(r?.nombre ?? ''); this.disparador.set(r?.disparador ?? 'disco'); this.umbral.set(String(r?.umbral ?? 90));
    this.espera.set(String(r?.esperaHoras ?? 24)); this.accion.set(r?.accion ?? 'avisar'); this.texto.set(r?.texto ?? '');
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
    const umbral = Number(this.umbral()); const esperaHoras = Number(this.espera());
    if (!this.nombre().trim()) { this.error.set('Ponle un nombre.'); return; }
    if (this.umbral().trim() === '' || !Number.isFinite(umbral) || umbral < 0) { this.error.set('Pon un valor válido (0 o más).'); return; }
    if (!Number.isInteger(esperaHoras) || esperaHoras < 1 || esperaHoras > 720) { this.error.set('La espera va de 1 a 720 horas.'); return; }
    this.guardando.set(true); this.error.set('');
    const cuerpo = { nombre: this.nombre().trim(), disparador: this.disparador(), umbral, esperaHoras, accion: this.accion(), texto: this.texto().trim() };
    try {
      const e = this.editando();
      if (e) await firstValueFrom(this.http.put(`/api/automatizaciones/${e.id}`, cuerpo));
      else await firstValueFrom(this.http.post('/api/automatizaciones', cuerpo));
      this.cerrar();
      await this.cargar();
    } catch (x) { this.error.set(this.mensaje(x)); } finally { this.guardando.set(false); }
  }

  protected async borrar(): Promise<void> {
    const e = this.editando(); if (!e) return;
    if (!this.confirmarBorrado()) { this.confirmarBorrado.set(true); return; }
    try { await firstValueFrom(this.http.delete(`/api/automatizaciones/${e.id}`)); this.cerrar(); await this.cargar(); } catch (x) { this.error.set(this.mensaje(x)); }
  }

  private mensaje(x: unknown): string {
    const e = x as HttpErrorResponse;
    const errores = e.error?.errors as Record<string, string[]> | undefined;
    return (errores ? Object.values(errores).flat()[0] : null) ?? 'No se ha podido guardar.';
  }
}
