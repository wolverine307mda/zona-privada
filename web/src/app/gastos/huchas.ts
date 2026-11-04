import { Component, ElementRef, computed, effect, inject, signal, untracked, viewChild } from '@angular/core';
import { Errores, GastosService, erroresDe } from './gastos.service';
import { Hucha, diaCorto, euros, eurosCorto } from './modelo';

const EMOJIS = ['🐷', '✈️', '🏖️', '🚗', '🏠', '💻', '🎮', '🎁', '🛟', '💍', '🎓', '📱'];
const COLORES = ['#ff7a1a', '#3ddc84', '#6c8cff', '#ff5fa2', '#c77dff', '#2ec4b6', '#ffc93c', '#e62429'];

// Huchas: dinero apartado para algo. No cambia el saldo (sigue siendo tuyo), pero dice cuánto ya tiene destino.
// Con fecha objetivo calcula cuánto meter al mes para llegar.
@Component({
  selector: 'app-huchas',
  template: `
    <section class="cab-huchas">
      <article class="caja total" data-inclina>
        <span class="etq">Apartado en huchas</span>
        <strong class="grande">{{ euros(total().ahorrado) }}</strong>
        <span class="sub">de {{ eurosCorto(total().objetivo) }} entre todas · {{ total().pct }} %</span>
        <span class="barra"><span [style.width.%]="total().pct"></span></span>
      </article>
      <button class="caja nueva" type="button" (click)="abrir()">
        <span class="nueva__mas" aria-hidden="true">+</span>
        <strong>Nueva hucha</strong>
        <span>Un viaje, el colchón para imprevistos, el próximo móvil…</span>
      </button>
    </section>

    <section class="huchas">
      @for (h of lista(); track h.id) {
        <article class="hucha" data-inclina [style.--c]="h.color || '#ff7a1a'" [class.hucha--llena]="h.porcentaje >= 100">
          <button class="hucha__editar" type="button" (click)="abrir(h)" [attr.aria-label]="'Editar ' + h.nombre">
            <svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M4 20h4L19 9l-4-4L4 16zM14 6l4 4" /></svg>
          </button>
          <div class="hucha__tarro" aria-hidden="true">
            <span class="hucha__agua" [style.--p]="h.porcentaje"></span>
            <span class="hucha__emoji">{{ h.emoji }}</span>
          </div>
          <h3>{{ h.nombre }}</h3>
          <p class="hucha__cifra">{{ euros(h.ahorrado) }} <em>de {{ eurosCorto(h.objetivo) }}</em></p>
          <p class="hucha__sub">
            @if (h.porcentaje >= 100) { 🎉 ¡Conseguido! }
            @else if (h.alMes != null) { Mete <b>{{ euros(h.alMes) }}</b> al mes para llegar el {{ diaCorto(h.fecha!) }} }
            @else { Faltan {{ euros(h.objetivo - h.ahorrado) }} }
          </p>
          <div class="hucha__botones">
            @for (v of [10, 50]; track v) { <button type="button" (click)="aportar(h, v)">+{{ v }} €</button> }
            <button type="button" (click)="abrirMovimiento(h, 1)">Meter…</button>
            <button type="button" (click)="abrirMovimiento(h, -1)" [disabled]="h.ahorrado <= 0">Sacar…</button>
          </div>
        </article>
      } @empty {
        @if (cargado()) {
          <div class="caja vacio vacio--ancho">
            <strong>Aún no tienes huchas</strong>
            Apartar dinero con un nombre ayuda a no gastarlo: empieza por un colchón de 3 meses de gastos.
          </div>
        }
      }
    </section>

    <!-- crear / editar -->
    <dialog class="dlg" #dlg (click)="fuera($event)" aria-labelledby="dlg-hucha-t">
      <form class="dlg__caja" (submit)="guardar($event)" novalidate>
        <header class="dlg__cabeza"><h2 id="dlg-hucha-t">{{ editando() ? 'Editar hucha' : 'Nueva hucha' }}</h2>
          <button class="dlg__x" type="button" (click)="dlg.close()" aria-label="Cerrar"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6l12 12M18 6 6 18" /></svg></button></header>
        <div class="emojis" role="group" aria-label="Icono">
          @for (e of emojis; track e) { <button type="button" [attr.aria-pressed]="emoji() === e" (click)="emoji.set(e)">{{ e }}</button> }
        </div>
        <label class="campo"><span class="campo__etiqueta">Nombre</span>
          <input class="campo__input" type="text" maxlength="80" placeholder="Viaje a Japón" [value]="nombre()" (input)="nombre.set(valor($event))"></label>
        @if (errores()['nombre']) { <p class="error">{{ errores()['nombre'][0] }}</p> }
        <div class="fila2">
          <label class="campo"><span class="campo__etiqueta">Objetivo (€)</span>
            <input class="campo__input" type="text" inputmode="decimal" placeholder="1500" [value]="objetivo()" (input)="objetivo.set(valor($event))"></label>
          <label class="campo"><span class="campo__etiqueta">Para cuándo <em>(opcional)</em></span>
            <input class="campo__input" type="date" [value]="fecha()" (input)="fecha.set(valor($event))"></label>
        </div>
        @if (errores()['objetivo']) { <p class="error">{{ errores()['objetivo'][0] }}</p> }
        <div class="colores" role="group" aria-label="Color">
          @for (c of colores; track c) { <button type="button" [style.--m]="c" [attr.aria-pressed]="color() === c" (click)="color.set(c)" [attr.aria-label]="'Color ' + c"></button> }
        </div>
        @if (errores()['general']) { <p class="error">{{ errores()['general'][0] }}</p> }
        <footer class="dlg__pie">
          @if (editando()) { <button class="boton-borrar" type="button" (click)="borrar()">{{ confirmar() ? '¿Seguro? Pulsa otra vez' : 'Eliminar' }}</button> }
          <button class="button button--ghost" type="button" (click)="dlg.close()">Cancelar</button>
          <button class="button" type="submit">Guardar</button>
        </footer>
      </form>
    </dialog>

    <!-- meter / sacar una cantidad -->
    <dialog class="dlg" #dlgMov (click)="fuera($event)" aria-labelledby="dlg-mov-h">
      @if (moviendo(); as m) {
        <form class="dlg__caja" (submit)="confirmarMovimiento($event)" novalidate>
          <header class="dlg__cabeza"><h2 id="dlg-mov-h">{{ m.signo > 0 ? 'Meter en' : 'Sacar de' }} {{ m.hucha.nombre }}</h2>
            <button class="dlg__x" type="button" (click)="dlgMov.close()" aria-label="Cerrar"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6l12 12M18 6 6 18" /></svg></button></header>
          <label class="importe"><span class="sr">Cantidad</span>
            <input type="text" inputmode="decimal" placeholder="0,00" autocomplete="off" [value]="cantidad()" (input)="cantidad.set(valor($event))">
            <span class="importe__simbolo">€</span></label>
          <p class="dlg__lead">Ahora hay <strong>{{ euros(m.hucha.ahorrado) }}</strong>. Mover dinero a una hucha no cambia tu saldo: solo lo aparta.</p>
          @if (errores()['importe']) { <p class="error">{{ errores()['importe'][0] }}</p> }
          <footer class="dlg__pie">
            <button class="button button--ghost" type="button" (click)="dlgMov.close()">Cancelar</button>
            <button class="button" type="submit">{{ m.signo > 0 ? 'Meter' : 'Sacar' }}</button>
          </footer>
        </form>
      }
    </dialog>
  `,
  styleUrls: ['./comun.css', './dialogos.css'],
  styles: `
    .cab-huchas { display: grid; grid-template-columns: minmax(0, 1.6fr) minmax(0, 1fr); gap: 20px; }
    .total { gap: 6px; }
    .total .sub { font-size: var(--step-small); color: var(--soft); }
    .barra { height: 10px; border-radius: 999px; background: var(--tarjeta-2); overflow: hidden; margin-top: 6px; }
    .barra span { display: block; height: 100%; border-radius: inherit; background: linear-gradient(90deg, var(--brand), var(--ok)); transition: width 900ms var(--ease-expo); }
    .nueva { align-items: flex-start; justify-content: center; gap: 4px; border-style: dashed; cursor: pointer; text-align: left; color: var(--texto); font: inherit; }
    .nueva span:last-child { font-size: var(--step-small); color: var(--soft); }
    .nueva__mas { display: grid; place-items: center; width: 40px; height: 40px; border-radius: 12px; background: var(--brand); color: var(--on-brand); font: var(--peso-ligero) 24px/1 var(--font-text); margin-bottom: 6px; }
    @media (hover: hover) { .nueva:hover { border-color: var(--brand); } }
    .huchas { display: grid; grid-template-columns: repeat(auto-fill, minmax(230px, 1fr)); gap: 16px; }
    .vacio--ancho { grid-column: 1 / -1; }
    .hucha {
      position: relative; display: flex; flex-direction: column; align-items: center; gap: 6px; padding: 22px 16px 16px; text-align: center; min-width: 0;
      border-radius: calc(var(--radio) + 4px); border: 1px solid var(--vidrio-borde); background: var(--brillo), var(--vidrio); backdrop-filter: var(--desenfoque); box-shadow: var(--sombra);
      transition: transform 500ms var(--ease-out), border-color 240ms ease;
    }
    @media (hover: hover) { .hucha:hover { border-color: color-mix(in srgb, var(--c) 55%, var(--linea)); } }
    .hucha--llena { box-shadow: 0 20px 50px -24px var(--c), var(--sombra); }
    .hucha__editar { position: absolute; top: 10px; right: 10px; display: grid; place-items: center; width: 34px; height: 34px; border-radius: 10px; border: 0; background: transparent; color: var(--soft); cursor: pointer; }
    .hucha__editar .icon { width: 16px; height: 16px; }
    @media (hover: hover) { .hucha__editar:hover { background: var(--tarjeta-2); color: var(--texto); } }
    /* el tarro se llena con el porcentaje, con una ola que se mueve */
    .hucha__tarro { position: relative; width: 96px; height: 96px; border-radius: 50%; overflow: hidden; background: color-mix(in srgb, var(--c) 12%, var(--tarjeta-2)); box-shadow: inset 0 0 0 3px color-mix(in srgb, var(--c) 40%, transparent); display: grid; place-items: center; }
    .hucha__agua { --p: 0; position: absolute; left: -50%; width: 200%; height: 200%; top: calc(100% - var(--p) * 1%); border-radius: 42%; background: color-mix(in srgb, var(--c) 70%, transparent); animation: ola 7s linear infinite; transition: top 1.2s var(--ease-expo); }
    @keyframes ola { to { rotate: 360deg; } }
    .hucha__emoji { position: relative; font-size: 40px; line-height: 1; filter: drop-shadow(0 4px 8px rgba(0, 0, 0, 0.25)); }
    .hucha h3 { font: 500 var(--step-body)/1.15 var(--font-text); letter-spacing: -0.02em; margin-top: 6px; max-width: 100%; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .hucha__cifra { font: 500 var(--step-lead)/1.1 var(--font-text); letter-spacing: -0.03em; }
    .hucha__cifra em { display: block; margin-top: 4px; font: 400 var(--step-small)/1 var(--font-text); color: var(--soft); letter-spacing: 0; font-style: normal; }
    .hucha__sub { font-size: 12.5px; color: var(--soft); min-height: 2.6em; }
    .hucha__sub b { color: var(--texto); }
    .hucha__botones { display: flex; flex-wrap: wrap; justify-content: center; gap: 6px; margin-top: 4px; }
    .hucha__botones button { min-height: 34px; padding: 0 12px; border-radius: 999px; border: 1px solid var(--linea); background: var(--tarjeta-2); color: var(--texto); font: 500 13px/1 var(--font-text); cursor: pointer; }
    .hucha__botones button:first-child { background: color-mix(in srgb, var(--c) 18%, var(--tarjeta-2)); border-color: color-mix(in srgb, var(--c) 40%, transparent); }
    .hucha__botones button:disabled { opacity: 0.4; }
    .emojis { display: flex; flex-wrap: wrap; gap: 6px; }
    .emojis button { width: 42px; height: 42px; border-radius: 12px; border: 1.5px solid var(--linea); background: var(--tarjeta-2); font-size: 22px; cursor: pointer; }
    .emojis button[aria-pressed='true'] { border-color: var(--brand); background: color-mix(in srgb, var(--brand) 14%, var(--tarjeta-2)); }
    .colores { display: flex; flex-wrap: wrap; gap: 8px; }
    .colores button { width: 30px; height: 30px; border-radius: 50%; border: 2px solid transparent; background: var(--m); cursor: pointer; }
    .colores button[aria-pressed='true'] { border-color: var(--texto); transform: scale(1.12); }
    @media (max-width: 700px) { .cab-huchas { grid-template-columns: minmax(0, 1fr); } .huchas { grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 10px; } .hucha { padding: 18px 10px 12px; } .hucha__tarro { width: 76px; height: 76px; } .hucha__emoji { font-size: 32px; } }
    @media (prefers-reduced-motion: reduce) { .hucha__agua { animation: none; } }
  `,
})
export class Huchas {
  protected readonly g = inject(GastosService);
  private readonly dlg = viewChild.required<ElementRef<HTMLDialogElement>>('dlg');
  private readonly dlgMov = viewChild.required<ElementRef<HTMLDialogElement>>('dlgMov');
  protected readonly euros = euros;
  protected readonly eurosCorto = eurosCorto;
  protected readonly diaCorto = diaCorto;
  protected readonly emojis = EMOJIS;
  protected readonly colores = COLORES;

  protected readonly lista = signal<Hucha[]>([]);
  protected readonly cargado = signal(false);
  protected readonly editando = signal<Hucha | null>(null);
  protected readonly nombre = signal('');
  protected readonly objetivo = signal('');
  protected readonly fecha = signal('');
  protected readonly emoji = signal('🐷');
  protected readonly color = signal(COLORES[0]);
  protected readonly errores = signal<Errores>({});
  protected readonly confirmar = signal(false);
  protected readonly moviendo = signal<{ hucha: Hucha; signo: 1 | -1 } | null>(null);
  protected readonly cantidad = signal('');

  protected readonly total = computed(() => {
    const ah = this.lista().reduce((t, h) => t + h.ahorrado, 0), ob = this.lista().reduce((t, h) => t + h.objetivo, 0);
    return { ahorrado: ah, objetivo: ob, pct: ob > 0 ? Math.min(100, Math.round((ah / ob) * 100)) : 0 };
  });

  constructor() {
    effect(() => {
      this.g.version();
      untracked(async () => { try { this.lista.set(await this.g.huchas()); } finally { this.cargado.set(true); } });
    });
  }

  protected valor(e: Event): string { return (e.target as HTMLInputElement).value; }
  private num(s: string): number | null { const n = Number(s.replace(/\s/g, '').replace(',', '.')); return s.trim() && Number.isFinite(n) ? n : null; }

  protected abrir(h?: Hucha): void {
    this.editando.set(h ?? null);
    this.nombre.set(h?.nombre ?? '');
    this.objetivo.set(h ? String(h.objetivo).replace('.', ',') : '');
    this.fecha.set(h?.fecha ?? '');
    this.emoji.set(h?.emoji ?? '🐷');
    this.color.set(h?.color || COLORES[this.lista().length % COLORES.length]);
    this.errores.set({});
    this.confirmar.set(false);
    this.dlg().nativeElement.showModal();
  }

  protected async guardar(e: Event): Promise<void> {
    e.preventDefault();
    try {
      await this.g.guardarHucha({ nombre: this.nombre(), objetivo: this.num(this.objetivo()) ?? 0, color: this.color(), emoji: this.emoji(), fecha: this.fecha() || null }, this.editando()?.id);
      this.dlg().nativeElement.close();
      this.g.recargar();
    } catch (err) { this.errores.set(erroresDe(err)); }
  }

  protected async borrar(): Promise<void> {
    if (!this.confirmar()) { this.confirmar.set(true); return; }
    try {
      await this.g.borrarHucha(this.editando()!.id);
      this.dlg().nativeElement.close();
      this.g.recargar();
    } catch (err) {
      // si no se pudo, se dice y el botón vuelve a pedir confirmación
      this.confirmar.set(false);
      this.errores.set(erroresDe(err));
    }
  }

  protected async aportar(h: Hucha, v: number): Promise<void> {
    this.lista.update((l) => l.map((x) => (x.id === h.id ? { ...x, ahorrado: x.ahorrado + v, porcentaje: Math.min(100, ((x.ahorrado + v) / x.objetivo) * 100) } : x)));
    try { await this.g.aportar(h.id, v); } finally { this.g.recargar(); }
  }

  protected abrirMovimiento(h: Hucha, signo: 1 | -1): void {
    this.moviendo.set({ hucha: h, signo });
    this.cantidad.set('');
    this.errores.set({});
    this.dlgMov().nativeElement.showModal();
  }

  protected async confirmarMovimiento(e: Event): Promise<void> {
    e.preventDefault();
    const m = this.moviendo(), v = this.num(this.cantidad());
    if (!m || !v || v <= 0) { this.errores.set({ importe: ['Pon una cantidad mayor que 0.'] }); return; }
    try {
      await this.g.aportar(m.hucha.id, v * m.signo);
      this.dlgMov().nativeElement.close();
      this.g.recargar();
    } catch (err) { this.errores.set(erroresDe(err)); }
  }

  protected fuera(e: MouseEvent): void { const d = e.target as HTMLElement; if (d.tagName === 'DIALOG') (d as HTMLDialogElement).close(); }
}
