import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { EstadoError, EstadoVacio, Esqueleto } from '../ui';

interface Habito {
  id: number; nombre: string; icono: string; dias: number; objetivo: number; unidad: string; horaAviso: string | null; archivado: boolean;
  tocaHoy: boolean; hoy: number; hecho: boolean; actual: number; mejor: number; cumplimiento30: number; mapa: string;
}

const DIAS = ['L', 'M', 'X', 'J', 'V', 'S', 'D'];
const NOMBRE_DIAS = ['lunes', 'martes', 'miércoles', 'jueves', 'viernes', 'sábado', 'domingo'];

// Hábitos y rachas: lo que quieres hacer cada día (o ciertos días de la semana), con un toque para apuntarlo, la racha y un mapa de las últimas 13 semanas.
// Solo cuentan los días que tocaban: un hábito de lunes a viernes no se rompe el fin de semana. Si le pones hora, te avisa si a esa hora no está hecho.
@Component({
  selector: 'app-habitos',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [EstadoError, EstadoVacio, Esqueleto],
  styleUrls: ['../gastos/comun.css', '../gastos/dialogos.css'],
  template: `
    <header class="cab">
      <p class="cab__sub" aria-live="polite">{{ resumen() }}</p>
      <button class="button" type="button" (click)="nuevo()">Nuevo hábito</button>
    </header>

    @if (fallo()) { <app-estado-error titulo="No pude cargar los hábitos" (reintentar)="cargar()" /> }
    @else if (lista() === null) { <section class="caja"><app-esqueleto forma="bloque" alto="72px" /><app-esqueleto forma="bloque" alto="72px" /></section> }
    @else if (lista()!.length === 0) {
      <app-estado-vacio icono="M5 12.5l4.2 4.2L19 7" titulo="Aún no tienes hábitos" texto="Crea el primero: meditar, leer, beber agua, salir a caminar…">
        <button class="button" type="button" (click)="nuevo()">Crear un hábito</button>
      </app-estado-vacio>
    } @else {
      <ul class="lista" aria-label="Hábitos">
        @for (h of lista()!; track h.id) {
          <li class="habito" [class.habito--hecho]="h.hecho" [class.habito--libre]="!h.tocaHoy && !h.hecho">
            @if (h.objetivo === 1) {
              <button type="button" class="check" [class.check--hecho]="h.hecho" [attr.aria-pressed]="h.hecho" [attr.aria-label]="(h.hecho ? 'Quitar ' : 'Apuntar ') + h.nombre" [disabled]="ocupado() === h.id" (click)="alternar(h)">
                <svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M5 12.5l4.2 4.2L19 7" /></svg>
              </button>
            } @else {
              <div class="contador" role="group" [attr.aria-label]="h.nombre">
                <button type="button" (click)="poner(h, h.hoy - 1)" [disabled]="h.hoy <= 0 || ocupado() === h.id" aria-label="Uno menos">−</button>
                <strong [class.contador--hecho]="h.hecho" aria-live="polite">{{ h.hoy }}<small>/{{ h.objetivo }}</small></strong>
                <button type="button" (click)="poner(h, h.hoy + 1)" [disabled]="ocupado() === h.id" aria-label="Uno más">+</button>
              </div>
            }
            <button type="button" class="habito__texto" (click)="editar(h)" [attr.aria-label]="'Editar ' + h.nombre">
              <strong>{{ h.nombre }}</strong>
              <small>{{ detalle(h) }}</small>
            </button>
            <div class="racha" [attr.aria-label]="'Racha de ' + h.actual + ' días'">
              <b>{{ h.actual }}</b><small>{{ h.actual === 1 ? 'día' : 'días' }}</small>
            </div>
            <div class="mapa" role="img" [attr.aria-label]="'Últimas 13 semanas: cumplimiento del ' + h.cumplimiento30 + ' % en los últimos 30 días'">
              @for (c of celdas(h); track $index) { <i [attr.data-e]="c"></i> }
            </div>
          </li>
        }
      </ul>
    }

    <dialog class="dlg" #dialogo (click)="fuera($event)" (close)="cerrado()" aria-labelledby="dlg-habito">
      <form class="dlg__caja" (submit)="guardar($event)" novalidate>
        <header class="dlg__cabeza">
          <h2 id="dlg-habito">{{ editando() ? 'Editar hábito' : 'Nuevo hábito' }}</h2>
          <button class="dlg__x" type="button" (click)="cerrar()" aria-label="Cerrar"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6l12 12M18 6 6 18" /></svg></button>
        </header>
        <label class="campo"><span class="campo__etiqueta">Nombre</span>
          <input class="campo__input" type="text" maxlength="40" placeholder="Meditar, leer, beber agua…" [value]="nombre()" (input)="nombre.set(valor($event))"></label>
        <fieldset class="cats">
          <legend class="campo__etiqueta">Qué días toca</legend>
          <div class="dias">
            @for (d of dias; track d; let i = $index) {
              <button type="button" [attr.aria-pressed]="tocaDia(i)" [attr.aria-label]="nombreDia(i)" (click)="alternarDia(i)">{{ d }}</button>
            }
          </div>
        </fieldset>
        <div class="fila2">
          <label class="campo"><span class="campo__etiqueta">Veces al día <em>(1 = sí o no)</em></span>
            <input class="campo__input" type="number" inputmode="numeric" min="1" max="99" [value]="objetivo()" (input)="objetivo.set(valor($event))"></label>
          <label class="campo"><span class="campo__etiqueta">Unidad <em>(opcional)</em></span>
            <input class="campo__input" type="text" maxlength="12" placeholder="vasos, páginas…" [value]="unidad()" (input)="unidad.set(valor($event))"></label>
        </div>
        <label class="campo"><span class="campo__etiqueta">Avisarme a las <em>(si aún no está hecho)</em></span>
          <input class="campo__input" type="time" [value]="hora()" (input)="hora.set(valor($event))"></label>
        @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
        <footer class="dlg__pie">
          @if (editando()) {
            <button type="button" class="boton-borrar" (click)="archivar()">Archivar</button>
            <button type="button" class="boton-borrar" (click)="borrar()">{{ confirmarBorrado() ? 'Seguro: borrar todo' : 'Borrar' }}</button>
          }
          <button class="button" type="submit" [attr.aria-busy]="guardando() || null" [disabled]="guardando()">Guardar</button>
        </footer>
      </form>
    </dialog>
  `,
  styles: `
    .cab__sub { margin: 0; font: var(--peso-ligero) 1.3rem/1.2 var(--font-text); }
    .cab { display: flex; align-items: center; justify-content: space-between; gap: 12px; flex-wrap: wrap; margin-bottom: var(--esp-4); }
    .lista { list-style: none; margin: 0; padding: 0; display: grid; gap: 10px; }
    .habito { display: grid; grid-template-columns: auto minmax(0, 1fr) auto; grid-template-areas: 'c t r' 'm m m'; gap: 10px 14px; align-items: center; padding: 14px 16px; border-radius: var(--radio-l); border: 1px solid var(--vidrio-borde); background: var(--vidrio); }
    .habito--hecho { border-color: color-mix(in srgb, var(--ok) 45%, transparent); }
    .habito--libre { opacity: .7; }
    .check, .contador { grid-area: c; }
    .check { display: grid; place-items: center; width: 52px; height: 52px; border-radius: 50%; border: 2px solid var(--linea); background: transparent; color: transparent; cursor: pointer; transition: background var(--dur-2) var(--ease-expo), border-color var(--dur-2), transform var(--dur-1); }
    .check .icon { width: 26px; height: 26px; stroke-width: 2.4; }
    .check:active { transform: scale(.92); }
    .check--hecho { background: var(--ok); border-color: var(--ok); color: var(--on-negra, #05210f); }
    .contador { display: flex; align-items: center; gap: 4px; }
    .contador strong { min-width: 3.2ch; text-align: center; font: var(--peso-ligero) 1.35rem/1 var(--font-text); font-variant-numeric: tabular-nums; }
    .contador strong small { font-size: .75rem; color: var(--soft); }
    .contador--hecho { color: var(--ok); }
    .contador button { width: 40px; height: 40px; border: 0; border-radius: 50%; background: color-mix(in srgb, var(--texto) 8%, transparent); color: var(--texto); font-size: 1.2rem; line-height: 1; cursor: pointer; }
    .contador button:disabled { opacity: .35; }
    .habito__texto { grid-area: t; display: grid; gap: 2px; min-width: 0; padding: 4px 0; border: 0; background: none; color: inherit; font: inherit; text-align: left; cursor: pointer; }
    .habito__texto strong { font-weight: 500; font-size: 1.02rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .habito__texto small { color: var(--soft); font-size: .78rem; }
    .racha { grid-area: r; display: grid; justify-items: center; line-height: 1; min-width: 44px; }
    .racha b { font: var(--peso-ligero) 1.6rem/1 var(--font-text); font-variant-numeric: tabular-nums; color: var(--acento); }
    .racha small { color: var(--soft); font-size: .7rem; }
    /* 13 semanas × 7 días, por columnas (cada columna es una semana de lunes a domingo) */
    .mapa { grid-area: m; display: grid; grid-auto-flow: column; grid-template-rows: repeat(7, 11px); grid-auto-columns: 11px; gap: 3px; justify-content: start; }
    .mapa i { width: 11px; height: 11px; border-radius: 3px; background: color-mix(in srgb, var(--texto) 7%, transparent); }
    .mapa i[data-e='h'] { background: var(--brand); }
    .mapa i[data-e='p'] { background: color-mix(in srgb, var(--brand) 40%, transparent); }
    .mapa i[data-e='f'] { background: color-mix(in srgb, var(--bad) 35%, transparent); }
    .mapa i[data-e='o'] { outline: 1.5px solid var(--brand); outline-offset: -1.5px; background: transparent; }
    .mapa i[data-e='l'] { background: color-mix(in srgb, var(--texto) 3%, transparent); }
    .mapa i[data-e='x'], .mapa i[data-e='-'] { background: transparent; }
    .dias { display: grid; grid-template-columns: repeat(7, minmax(0, 1fr)); gap: 6px; margin-top: 6px; }
    .dias button { min-height: 44px; border-radius: 50%; border: 1px solid var(--linea); background: transparent; color: var(--texto); font-weight: 600; cursor: pointer; }
    .dias button[aria-pressed='true'] { background: var(--brand); border-color: var(--brand); color: var(--on-brand); }
    @media (min-width: 720px) { .habito { grid-template-columns: auto minmax(0, 1fr) auto auto; grid-template-areas: 'c t r m'; } .mapa { justify-content: end; } }
    @media (prefers-reduced-motion: reduce) { .check { transition: none; } }
  `,
})
export class Habitos {
  private readonly http = inject(HttpClient);
  private readonly dialogo = viewChild.required<ElementRef<HTMLDialogElement>>('dialogo');

  protected readonly dias = DIAS;
  protected readonly lista = signal<Habito[] | null>(null);
  protected readonly fallo = signal(false);
  protected readonly ocupado = signal<number | null>(null);

  protected readonly editando = signal<Habito | null>(null);
  protected readonly nombre = signal('');
  protected readonly mascara = signal(127);
  protected readonly objetivo = signal('1');
  protected readonly unidad = signal('');
  protected readonly hora = signal('');
  protected readonly error = signal('');
  protected readonly guardando = signal(false);
  protected readonly confirmarBorrado = signal(false);

  protected readonly resumen = computed(() => {
    const l = this.lista();
    if (!l) return 'Rachas y rutinas de cada día';
    const hoy = l.filter((h) => h.tocaHoy);
    if (hoy.length === 0) return l.length ? 'Hoy no toca ninguno' : 'Rachas y rutinas de cada día';
    const hechos = hoy.filter((h) => h.hecho).length;
    return hechos === hoy.length ? `Hoy, todo hecho (${hechos} de ${hoy.length})` : `Hoy llevas ${hechos} de ${hoy.length}`;
  });

  constructor() { void this.cargar(); }

  protected async cargar(): Promise<void> {
    this.fallo.set(false);
    try { this.lista.set(await firstValueFrom(this.http.get<Habito[]>('/api/habitos'))); } catch { this.fallo.set(true); }
  }

  protected detalle(h: Habito): string {
    const dias = h.dias === 127 ? 'cada día' : DIAS.filter((_, i) => h.dias & (1 << i)).join(' ');
    const meta = h.objetivo > 1 ? `${h.objetivo}${h.unidad ? ' ' + h.unidad : ''} al día · ` : '';
    return `${meta}${dias} · mejor racha ${h.mejor} · ${h.cumplimiento30} % en 30 días`;
  }

  // el mapa por columnas de semana: se rellena por delante con huecos hasta que el primer día caiga en su día de la semana
  protected celdas(h: Habito): string[] {
    const primero = new Date(); primero.setDate(primero.getDate() - (h.mapa.length - 1));
    const hueco = (primero.getDay() + 6) % 7;
    return [...Array<string>(hueco).fill('x'), ...h.mapa];
  }

  protected async alternar(h: Habito): Promise<void> { await this.poner(h, h.hecho ? 0 : h.objetivo); }

  protected async poner(h: Habito, cantidad: number): Promise<void> {
    if (this.ocupado() !== null) return;
    this.ocupado.set(h.id);
    try {
      const nuevo = await firstValueFrom(this.http.put<Habito>(`/api/habitos/${h.id}/marcar`, { cantidad: Math.max(0, cantidad) }));
      this.lista.update((l) => l?.map((x) => (x.id === h.id ? nuevo : x)) ?? null);
    } catch { void this.cargar(); } finally { this.ocupado.set(null); }
  }

  // ---- diálogo ----
  protected nuevo(): void { this.abrir(null); }
  protected editar(h: Habito): void { this.abrir(h); }

  private abrir(h: Habito | null): void {
    this.editando.set(h);
    this.nombre.set(h?.nombre ?? '');
    this.mascara.set(h?.dias ?? 127);
    this.objetivo.set(String(h?.objetivo ?? 1));
    this.unidad.set(h?.unidad ?? '');
    this.hora.set(h?.horaAviso ?? '');
    this.error.set(''); this.confirmarBorrado.set(false);
    this.dialogo().nativeElement.showModal();
  }

  protected cerrar(): void { this.dialogo().nativeElement.close(); }
  protected cerrado(): void { this.editando.set(null); }
  protected fuera(e: MouseEvent): void { if (e.target === this.dialogo().nativeElement) this.cerrar(); }
  protected valor(e: Event): string { return (e.target as HTMLInputElement).value; }
  protected tocaDia(i: number): boolean { return (this.mascara() & (1 << i)) !== 0; }
  protected nombreDia(i: number): string { return NOMBRE_DIAS[i]; }
  protected alternarDia(i: number): void { this.mascara.update((m) => m ^ (1 << i)); }

  protected async guardar(ev: Event): Promise<void> {
    ev.preventDefault();
    if (this.guardando()) return;
    const objetivo = Number(this.objetivo());
    if (!this.nombre().trim()) { this.error.set('Ponle un nombre.'); return; }
    if (!this.mascara()) { this.error.set('Elige al menos un día.'); return; }
    if (!Number.isInteger(objetivo) || objetivo < 1 || objetivo > 99) { this.error.set('Las veces al día van de 1 a 99.'); return; }
    this.guardando.set(true); this.error.set('');
    const cuerpo = { nombre: this.nombre().trim(), dias: this.mascara(), objetivo, unidad: this.unidad().trim(), horaAviso: this.hora() };
    try {
      const e = this.editando();
      if (e) await firstValueFrom(this.http.put(`/api/habitos/${e.id}`, cuerpo));
      else await firstValueFrom(this.http.post('/api/habitos', cuerpo));
      this.cerrar();
      await this.cargar();
    } catch (x) { this.error.set(this.mensaje(x)); } finally { this.guardando.set(false); }
  }

  protected async archivar(): Promise<void> {
    const e = this.editando(); if (!e) return;
    try { await firstValueFrom(this.http.put(`/api/habitos/${e.id}`, { archivado: true })); this.cerrar(); await this.cargar(); } catch (x) { this.error.set(this.mensaje(x)); }
  }

  protected async borrar(): Promise<void> {
    const e = this.editando(); if (!e) return;
    if (!this.confirmarBorrado()) { this.confirmarBorrado.set(true); return; }
    try { await firstValueFrom(this.http.delete(`/api/habitos/${e.id}`)); this.cerrar(); await this.cargar(); } catch (x) { this.error.set(this.mensaje(x)); }
  }

  private mensaje(x: unknown): string {
    const e = x as HttpErrorResponse;
    const errores = e.error?.errors as Record<string, string[]> | undefined;
    return (errores ? Object.values(errores).flat()[0] : null) ?? 'No se ha podido guardar.';
  }
}
