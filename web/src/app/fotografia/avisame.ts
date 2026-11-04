import { Component, computed, input, output } from '@angular/core';
import { AjustesLuz, Semana, colorNota } from './modelo';

const BORTLE = [
  { n: 1, nombre: 'Cielo excelente', color: '#05070d' }, { n: 2, nombre: 'Muy oscuro', color: '#0a0f1e' }, { n: 3, nombre: 'Rural', color: '#101a33' },
  { n: 4, nombre: 'Rural y periferia', color: '#1c2b4a' }, { n: 5, nombre: 'Periferia', color: '#34405a' }, { n: 6, nombre: 'Periferia brillante', color: '#5a5468' },
  { n: 7, nombre: 'Entre periferia y ciudad', color: '#8a6d68' }, { n: 8, nombre: 'Ciudad', color: '#b98a6a' }, { n: 9, nombre: 'Centro de ciudad', color: '#e0b387' },
];

// «Avísame»: cuándo quieres que la app te escriba para salir a fotografiar. La nota mínima se elige en una escala de 10 y enseña qué días de
// esta semana habrían avisado; el cielo de tu zona (Bortle) se elige con muestras de lo oscuro que es.
@Component({
  selector: 'app-avisame',
  template: `
    <section class="caja">
      <header class="caja__cab"><h3>Avísame</h3><span class="sub">te escribo cuando merezca la pena salir</span></header>
      @if (ajustes(); as a) {
        <label class="maestro" [class.maestro--on]="a.activa">
          <span class="maestro__icono" aria-hidden="true"><svg class="icon" viewBox="0 0 24 24"><path d="M6 9a6 6 0 0 1 12 0c0 6 2 7 2 7H4s2-1 2-7zM10 20a2 2 0 0 0 4 0"/></svg></span>
          <span class="maestro__texto"><strong>{{ a.activa ? 'Avisos encendidos' : 'Avisos apagados' }}</strong>
            <small>Por notificación y Telegram, solo si no estás trabajando ni tienes un evento.</small></span>
          <input type="checkbox" role="switch" class="interruptor" aria-label="Avisarme cuando merezca la pena" [checked]="a.activa" (change)="cambiar.emit({ activa: $any($event.target).checked })">
        </label>

        <div class="bloque" [class.bloque--apagado]="!a.activa">
          <div class="bloque__cab"><strong>Nota mínima</strong><span>{{ a.umbral }} de 10</span></div>
          <div class="escala" role="radiogroup" aria-label="Nota mínima para avisar">
            @for (n of notas; track n) {
              <button type="button" role="radio" [attr.aria-checked]="a.umbral === n" [class.dentro]="n >= a.umbral" [style.--c]="color(n)" (click)="cambiar.emit({ umbral: n })">{{ n }}</button>
            }
          </div>
          <p class="ayuda">{{ textoNota(a.umbral) }}</p>
          @if (previa().length) {
            <div class="previa" aria-label="Qué días de esta semana habrían avisado">
              <span class="previa__titulo">Esta semana te habría avisado:</span>
              <ul>
                @for (d of previa(); track d.fecha) {
                  <li [class.previa--si]="d.si"><small>{{ d.dia }}</small><b [style.color]="d.si ? color(d.nota) : null">{{ d.nota ?? '–' }}</b><i aria-hidden="true">{{ d.si ? '✓' : '' }}</i></li>
                }
              </ul>
            </div>
          }
        </div>

        <div class="bloque">
          <div class="bloque__cab"><strong>Cielo de tu zona</strong><span>Bortle {{ a.bortle }} · {{ nombreBortle(a.bortle) }}</span></div>
          <div class="escala escala--cielo" role="radiogroup" aria-label="Oscuridad del cielo de tu zona (Bortle)">
            @for (b of bortle; track b.n) {
              <button type="button" role="radio" [attr.aria-checked]="a.bortle === b.n" [style.--cielo]="b.color" [attr.aria-label]="'Bortle ' + b.n + ', ' + b.nombre" (click)="cambiar.emit({ bortle: b.n })"><span>{{ b.n }}</span></button>
            }
          </div>
          <p class="ayuda">Cuanto más oscuro, más cuenta la Vía Láctea en la nota de las noches.</p>
        </div>

        <label class="fila"><span><strong>Hora dorada de la mañana</strong><small>El amanecer también cuenta para avisarte.</small></span>
          <input type="checkbox" role="switch" class="interruptor" [checked]="a.amanecer" (change)="cambiar.emit({ amanecer: $any($event.target).checked })"></label>
        <label class="fila"><span><strong>Noches de Vía Láctea</strong><small>Te aviso la tarde anterior.</small></span>
          <input type="checkbox" role="switch" class="interruptor" [checked]="a.astro" (change)="cambiar.emit({ astro: $any($event.target).checked })"></label>
        <p class="ayuda">La hora dorada de la tarde se avisa {{ a.antelacionMin }} min antes.</p>
      } @else {
        <p class="ayuda">Cargando…</p>
      }
    </section>
  `,
  styleUrls: ['../gastos/comun.css'],
  styles: `
    :host { display: block; }
    .ayuda { margin: 8px 0 0; color: var(--soft); font-size: .84rem; line-height: 1.4; }
    .icon { width: 22px; height: 22px; fill: none; stroke: currentColor; stroke-width: 1.8; stroke-linecap: round; stroke-linejoin: round; }

    /* interruptor de verdad (un checkbox con aspecto de interruptor, accesible con teclado y lector) */
    .interruptor { flex: none; appearance: none; -webkit-appearance: none; position: relative; width: 52px; height: 30px; margin: 0; border-radius: 999px; border: 1px solid var(--vidrio-borde); background: color-mix(in srgb, var(--texto) 12%, transparent); cursor: pointer; transition: background 220ms ease, border-color 220ms ease; }
    .interruptor::before { content: ''; position: absolute; top: 3px; left: 3px; width: 22px; height: 22px; border-radius: 50%; background: var(--texto); transition: transform 280ms var(--ease-expo, ease), background 220ms ease; }
    .interruptor:checked { background: var(--brand); border-color: var(--brand); }
    .interruptor:checked::before { transform: translateX(22px); background: var(--on-brand); }
    .interruptor:focus-visible { outline: 2px solid var(--acento); outline-offset: 3px; }

    .maestro { display: flex; align-items: center; gap: 14px; padding: 16px; border-radius: 20px; border: 1px solid var(--vidrio-borde); background: color-mix(in srgb, var(--texto) 4%, transparent); cursor: pointer; transition: border-color 300ms, background 300ms; }
    .maestro--on { border-color: color-mix(in srgb, var(--brand) 60%, transparent); background: color-mix(in srgb, var(--brand) 10%, transparent); }
    .maestro__icono { display: grid; place-items: center; flex: none; width: 46px; height: 46px; border-radius: 16px; background: color-mix(in srgb, var(--texto) 8%, transparent); color: var(--soft); transition: background 300ms, color 300ms; }
    .maestro--on .maestro__icono { background: var(--brand); color: var(--on-brand); }
    .maestro__texto { display: grid; gap: 3px; flex: 1; min-width: 0; }
    .maestro__texto small { color: var(--soft); font-size: .8rem; line-height: 1.35; }

    .bloque { margin-top: 18px; transition: opacity 250ms; }
    .bloque--apagado { opacity: .5; }
    .bloque__cab { display: flex; justify-content: space-between; gap: 10px; align-items: baseline; margin-bottom: 10px; }
    .bloque__cab span { color: var(--soft); font-size: .84rem; text-align: right; }

    /* escala de 1 a 10 coloreada como las notas; lo que cumple el mínimo queda iluminado */
    .escala { display: grid; grid-template-columns: repeat(10, minmax(0, 1fr)); gap: 5px; }
    .escala button { min-height: 44px; border-radius: 12px; border: 1px solid var(--vidrio-borde); background: transparent; color: var(--soft); font: 700 .95rem/1 var(--font-code); cursor: pointer; transition: background 200ms, color 200ms, border-color 200ms, transform 200ms var(--ease-out, ease); }
    .escala button.dentro { background: color-mix(in srgb, var(--c) 22%, transparent); border-color: color-mix(in srgb, var(--c) 55%, transparent); color: var(--texto); }
    .escala button[aria-checked='true'] { background: var(--c); border-color: var(--c); color: #0b0f14; transform: translateY(-3px) scale(1.06); }
    .escala--cielo { grid-template-columns: repeat(9, minmax(0, 1fr)); }
    .escala--cielo button { position: relative; overflow: hidden; background: var(--cielo); border-color: color-mix(in srgb, var(--texto) 18%, transparent); color: #fff; text-shadow: 0 1px 3px rgba(0, 0, 0, .6); }
    .escala--cielo button[aria-checked='true'] { background: var(--cielo); border-color: var(--brand); box-shadow: 0 0 0 3px color-mix(in srgb, var(--brand) 55%, transparent); color: #fff; }
    .escala--cielo button span { position: relative; }
    .escala button:focus-visible { outline: 2px solid var(--acento); outline-offset: 2px; }

    /* qué días de esta semana habrían avisado con esa nota */
    .previa { margin-top: 12px; padding: 12px; border-radius: 16px; background: color-mix(in srgb, var(--texto) 4%, transparent); }
    .previa__titulo { font-size: .8rem; color: var(--soft); }
    .previa ul { list-style: none; display: grid; grid-template-columns: repeat(7, minmax(0, 1fr)); gap: 6px; margin: 8px 0 0; padding: 0; }
    .previa li { display: grid; justify-items: center; gap: 2px; padding: 6px 2px; border-radius: 12px; border: 1px solid transparent; opacity: .55; transition: opacity 250ms, border-color 250ms, background 250ms; }
    .previa li.previa--si { opacity: 1; border-color: color-mix(in srgb, var(--brand) 50%, transparent); background: color-mix(in srgb, var(--brand) 10%, transparent); }
    .previa small { color: var(--soft); font-size: .68rem; text-transform: capitalize; }
    .previa b { font: 700 1rem/1 var(--font-code); }
    .previa i { height: 12px; font-style: normal; font-size: .7rem; color: var(--acento); }

    .fila { display: flex; align-items: center; justify-content: space-between; gap: 14px; padding: 14px 2px 0; margin-top: 4px; cursor: pointer; }
    .fila span { display: grid; gap: 2px; min-width: 0; }
    .fila small { color: var(--soft); font-size: .8rem; }

    @media (max-width: 420px) { .escala { gap: 3px; } .escala button { min-height: 40px; border-radius: 10px; } .bloque__cab { flex-direction: column; gap: 2px; } .bloque__cab span { text-align: left; } }
    @media (prefers-reduced-motion: reduce) { .interruptor, .interruptor::before, .escala button, .previa li { transition: none; } }
  `,
})
export class Avisame {
  readonly ajustes = input.required<AjustesLuz | null>();
  readonly semana = input<Semana | null>(null);
  readonly cambiar = output<Partial<AjustesLuz>>();

  protected readonly notas = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
  protected readonly bortle = BORTLE;

  // los días de esta semana con su mejor nota y si habrían avisado con el mínimo elegido
  protected readonly previa = computed(() => {
    const umbral = this.ajustes()?.umbral ?? 6;
    return (this.semana()?.dias ?? []).map((d) => {
      const nota = d.mejor?.puntuacion ?? null;
      return { fecha: d.fecha, dia: new Date(d.fecha + 'T12:00').toLocaleDateString('es-ES', { weekday: 'short' }).replace('.', ''), nota, si: (nota ?? 0) >= umbral };
    });
  });

  protected color(n: number | null): string { return colorNota(n); }
  protected nombreBortle(n: number): string { return BORTLE.find((b) => b.n === n)?.nombre ?? ''; }
  protected textoNota(n: number): string {
    return n <= 3 ? 'Te aviso casi siempre, incluso con cielos regulares.'
      : n <= 5 ? 'Equilibrado: avisos con cielo bastante bueno.'
      : n <= 7 ? 'Solo cuando prometa de verdad.'
      : 'Solo con condiciones casi perfectas: pocos avisos.';
  }
}
