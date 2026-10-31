import { Component, ElementRef, effect, inject, signal, untracked, viewChild } from '@angular/core';
import { Errores, GastosService, erroresDe } from './gastos.service';
import { Mantenimiento, diaCorto, hoyIso, numero } from './modelo';

// sugerencias para empezar rápido (se pueden cambiar al crearlas)
const SUGERENCIAS = [
  { nombre: 'Cambio de aceite', cadaKm: 15000, cadaMeses: 12 },
  { nombre: 'ITV', cadaKm: null, cadaMeses: 24 },
  { nombre: 'Revisión', cadaKm: 30000, cadaMeses: 24 },
  { nombre: 'Neumáticos', cadaKm: 40000, cadaMeses: null },
  { nombre: 'Seguro (renovación)', cadaKm: null, cadaMeses: 12 },
];

// Mantenimiento del coche: lo que toca por km (los del último repostaje) o por tiempo, con aviso cuando se acerca
@Component({
  selector: 'app-mantenimiento',
  template: `
    <section class="caja mant">
      <header class="caja__cab">
        <h3>Mantenimiento</h3>
        <span class="sub">@if (km() != null) { {{ numero(km(), 0) }} km según el último repostaje } @else { apunta los km al repostar }</span>
      </header>
      @if (lista().length) {
        <ul class="mant__lista">
          @for (m of lista(); track m.id) {
            <li class="tarea" [attr.data-nivel]="m.nivel">
              <button class="tarea__cuerpo" type="button" (click)="abrir(m)">
                <span class="tarea__anillo" [style.--p]="m.progreso * 100" aria-hidden="true"></span>
                <span class="tarea__texto">
                  <strong>{{ m.nombre }}</strong>
                  <span>{{ estado(m) }}</span>
                </span>
              </button>
              <button class="tarea__hecho" type="button" (click)="hecho(m)" [attr.aria-label]="'Marcar ' + m.nombre + ' como hecho hoy'">Hecho</button>
            </li>
          }
        </ul>
      } @else if (cargado()) {
        <p class="sub">Añade lo que quieres vigilar y te aviso cuando toque:</p>
      }
      <div class="sugerencias">
        @for (s of sugerencias; track s.nombre) {
          @if (!tiene(s.nombre)) { <button type="button" (click)="abrir(undefined, s)">+ {{ s.nombre }}</button> }
        }
        <button type="button" (click)="abrir()">+ Otro</button>
      </div>
    </section>

    <dialog class="dlg" #dlg (click)="fuera($event)" aria-labelledby="dlg-mant-t">
      <form class="dlg__caja" (submit)="guardar($event)" novalidate>
        <header class="dlg__cabeza"><h2 id="dlg-mant-t">{{ editando() ? 'Editar' : 'Vigilar' }} {{ nombre() || 'mantenimiento' }}</h2>
          <button class="dlg__x" type="button" (click)="dlg.close()" aria-label="Cerrar"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6l12 12M18 6 6 18" /></svg></button></header>
        <label class="campo"><span class="campo__etiqueta">Qué</span>
          <input class="campo__input" type="text" maxlength="80" placeholder="Cambio de aceite" [value]="nombre()" (input)="nombre.set(valor($event))"></label>
        <div class="fila2">
          <label class="campo"><span class="campo__etiqueta">Cada (km)</span>
            <input class="campo__input" type="text" inputmode="numeric" placeholder="15000" [value]="cadaKm()" (input)="cadaKm.set(valor($event))"></label>
          <label class="campo"><span class="campo__etiqueta">Cada (meses)</span>
            <input class="campo__input" type="text" inputmode="numeric" placeholder="12" [value]="cadaMeses()" (input)="cadaMeses.set(valor($event))"></label>
        </div>
        @if (errores()['cadaKm'] || errores()['cadaMeses']) { <p class="error">{{ (errores()['cadaKm'] ?? errores()['cadaMeses'])[0] }}</p> }
        <p class="dlg__lead">La última vez que se hizo (si no lo sabes, deja la fecha de hoy y los km actuales):</p>
        <div class="fila2">
          <label class="campo"><span class="campo__etiqueta">Fecha</span>
            <input class="campo__input" type="date" [value]="ultimaFecha()" (input)="ultimaFecha.set(valor($event))"></label>
          <label class="campo"><span class="campo__etiqueta">Km</span>
            <input class="campo__input" type="text" inputmode="numeric" [placeholder]="km() ?? ''" [value]="ultimoKm()" (input)="ultimoKm.set(valor($event))"></label>
        </div>
        <label class="campo"><span class="campo__etiqueta">Notas <em>(opcional)</em></span>
          <input class="campo__input" type="text" maxlength="500" placeholder="Taller, aceite 5W30…" [value]="notas()" (input)="notas.set(valor($event))"></label>
        @if (errores()['nombre'] || errores()['general']) { <p class="error">{{ (errores()['nombre'] ?? errores()['general'])[0] }}</p> }
        <footer class="dlg__pie">
          @if (editando()) { <button class="boton-borrar" type="button" (click)="borrar()">{{ confirmar() ? '¿Seguro? Pulsa otra vez' : 'Eliminar' }}</button> }
          <button class="button button--ghost" type="button" (click)="dlg.close()">Cancelar</button>
          <button class="button" type="submit">Guardar</button>
        </footer>
      </form>
    </dialog>
  `,
  styleUrls: ['./comun.css', './dialogos.css'],
  styles: `
    .mant__lista { display: grid; grid-template-columns: repeat(auto-fill, minmax(260px, 1fr)); gap: 8px; }
    .tarea { --t: var(--ok); display: flex; align-items: center; gap: 8px; padding: 6px 6px 6px 8px; border-radius: 14px; background: var(--tarjeta-2); border: 1px solid transparent; }
    .tarea[data-nivel='pronto'] { --t: #f5b83d; border-color: color-mix(in srgb, #f5b83d 40%, transparent); }
    .tarea[data-nivel='vencido'] { --t: var(--bad); border-color: color-mix(in srgb, var(--bad) 45%, transparent); background: color-mix(in srgb, var(--bad) 8%, var(--tarjeta-2)); }
    .tarea[data-nivel='sin-datos'] { --t: var(--soft); }
    .tarea__cuerpo { flex: 1; min-width: 0; display: flex; align-items: center; gap: 12px; padding: 4px; border: 0; background: none; color: var(--texto); text-align: left; cursor: pointer; font: inherit; }
    .tarea__anillo { --p: 0; flex: none; width: 38px; height: 38px; border-radius: 50%; background: conic-gradient(var(--t) calc(var(--p) * 1%), color-mix(in srgb, var(--t) 18%, transparent) 0); mask: radial-gradient(circle, transparent 0 11px, #000 11.5px); }
    .tarea__texto { display: flex; flex-direction: column; min-width: 0; }
    .tarea__texto strong { font-weight: 500; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .tarea__texto span { font-size: 12px; color: var(--soft); }
    .tarea[data-nivel='vencido'] .tarea__texto span { color: var(--bad); }
    .tarea__hecho { flex: none; min-height: 34px; padding: 0 12px; border-radius: 999px; border: 1px solid var(--linea); background: var(--tarjeta); color: var(--texto); font: 500 13px/1 var(--font-text); cursor: pointer; }
    .sugerencias { display: flex; flex-wrap: wrap; gap: 6px; }
    .sugerencias button { min-height: 32px; padding: 0 12px; border-radius: 999px; border: 1px dashed var(--linea); background: transparent; color: var(--soft); font: 500 13px/1 var(--font-text); cursor: pointer; }
    @media (hover: hover) { .sugerencias button:hover { color: var(--texto); border-color: var(--brand); } }
    .sub { font-size: var(--step-small); color: var(--soft); }
  `,
})
export class MantenimientoCoche {
  private readonly g = inject(GastosService);
  private readonly dlg = viewChild.required<ElementRef<HTMLDialogElement>>('dlg');
  protected readonly numero = numero;
  protected readonly sugerencias = SUGERENCIAS;
  protected readonly lista = signal<Mantenimiento[]>([]);
  protected readonly km = signal<number | null>(null);
  protected readonly cargado = signal(false);
  protected readonly editando = signal<Mantenimiento | null>(null);
  protected readonly nombre = signal('');
  protected readonly cadaKm = signal('');
  protected readonly cadaMeses = signal('');
  protected readonly ultimaFecha = signal('');
  protected readonly ultimoKm = signal('');
  protected readonly notas = signal('');
  protected readonly errores = signal<Errores>({});
  protected readonly confirmar = signal(false);

  constructor() {
    effect(() => {
      this.g.version();
      untracked(async () => {
        try { const r = await this.g.mantenimiento(); this.lista.set(r.lista); this.km.set(r.kmActual); } finally { this.cargado.set(true); }
      });
    });
  }

  protected tiene(n: string): boolean { return this.lista().some((m) => m.nombre.toLowerCase() === n.toLowerCase()); }
  protected valor(e: Event): string { return (e.target as HTMLInputElement).value; }
  private entero(s: string): number | null { const n = parseInt(s.replace(/\D/g, ''), 10); return Number.isFinite(n) ? n : null; }

  protected estado(m: Mantenimiento): string {
    if (m.nivel === 'sin-datos') return 'Falta la última vez que se hizo';
    const partes: string[] = [];
    if (m.faltanKm != null) partes.push(m.faltanKm <= 0 ? `pasado ${numero(-m.faltanKm, 0)} km` : `en ${numero(m.faltanKm, 0)} km`);
    if (m.faltanDias != null) partes.push(m.faltanDias <= 0 ? `venció el ${diaCorto(m.proximaFecha!)}` : m.faltanDias < 60 ? `en ${m.faltanDias} días` : `el ${diaCorto(m.proximaFecha!)} ${m.proximaFecha!.slice(0, 4)}`);
    return (m.nivel === 'vencido' ? '¡Ya toca! ' : '') + partes.join(' · ');
  }

  protected abrir(m?: Mantenimiento, s?: { nombre: string; cadaKm: number | null; cadaMeses: number | null }): void {
    this.editando.set(m ?? null);
    this.nombre.set(m?.nombre ?? s?.nombre ?? '');
    this.cadaKm.set(String(m?.cadaKm ?? s?.cadaKm ?? ''));
    this.cadaMeses.set(String(m?.cadaMeses ?? s?.cadaMeses ?? ''));
    this.ultimaFecha.set(m?.ultimaFecha ?? hoyIso());
    this.ultimoKm.set(String(m?.ultimoKm ?? this.km() ?? ''));
    this.notas.set(m?.notas ?? '');
    this.errores.set({});
    this.confirmar.set(false);
    this.dlg().nativeElement.showModal();
  }

  protected async guardar(e: Event): Promise<void> {
    e.preventDefault();
    try {
      await this.g.guardarMantenimiento({
        nombre: this.nombre(), cadaKm: this.entero(this.cadaKm()), cadaMeses: this.entero(this.cadaMeses()),
        ultimaFecha: this.ultimaFecha() || null, ultimoKm: this.entero(this.ultimoKm()), notas: this.notas() || null,
      }, this.editando()?.id);
      this.dlg().nativeElement.close();
      this.g.recargar();
    } catch (err) { this.errores.set(erroresDe(err)); }
  }

  protected async hecho(m: Mantenimiento): Promise<void> {
    await this.g.mantenimientoHecho(m.id);
    this.g.recargar();
  }

  protected async borrar(): Promise<void> {
    if (!this.confirmar()) { this.confirmar.set(true); return; }
    try {
      await this.g.borrarMantenimiento(this.editando()!.id);
      this.dlg().nativeElement.close();
      this.g.recargar();
    } catch (err) {
      // si no se pudo, se dice y el botón vuelve a pedir confirmación
      this.confirmar.set(false);
      this.errores.set(erroresDe(err));
    }
  }

  protected fuera(e: MouseEvent): void { if (e.target === this.dlg().nativeElement) this.dlg().nativeElement.close(); }
}
