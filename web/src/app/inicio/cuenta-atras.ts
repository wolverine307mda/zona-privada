import { Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { Aparicion, colorEvento } from '../agenda/agenda.service';

export interface Cuenta { clave: string; titulo: string; color: string; modo: 'termina' | 'empieza'; ms: number; }

// 93784000 ms -> { d: 1, h: 2, m: 3, s: 4 }
export function partes(ms: number): { d: number; h: number; m: number; s: number } {
  const t = Math.max(0, Math.floor(ms / 1000));
  return { d: Math.floor(t / 86400), h: Math.floor((t % 86400) / 3600), m: Math.floor((t % 3600) / 60), s: t % 60 };
}

// Cuándo acaba un evento: su fin, o las 24:00 si es de todo el día y no trae fin. Sin hora de fin no hay «termina en».
function finDe(a: Aparicion): number | null {
  if (a.fin) return new Date(a.fin).getTime();
  if (a.todoElDia) { const i = new Date(a.inicio); return new Date(i.getFullYear(), i.getMonth(), i.getDate() + 1).getTime(); }
  return null;
}

// Los eventos que están pasando (cuenta hacia su fin) y el siguiente que empieza (cuenta hacia su inicio)
export function cuentas(eventos: readonly Aparicion[], ahora: number): Cuenta[] {
  const solo = eventos.filter((a) => a.tipo === 'Evento');
  const en: Cuenta[] = [];
  for (const a of solo) {
    const fin = finDe(a);
    if (fin !== null && new Date(a.inicio).getTime() <= ahora && ahora < fin)
      en.push({ clave: a.clave, titulo: a.titulo, color: a.color, modo: 'termina', ms: fin - ahora });
  }
  en.sort((x, y) => x.ms - y.ms);
  const sig = solo.filter((a) => new Date(a.inicio).getTime() > ahora).sort((x, y) => new Date(x.inicio).getTime() - new Date(y.inicio).getTime())[0];
  const lista = en.slice(0, 3);
  if (sig) lista.push({ clave: sig.clave, titulo: sig.titulo, color: sig.color, modo: 'empieza', ms: new Date(sig.inicio).getTime() - ahora });
  return lista;
}

// Cuenta atrás de la agenda: «Termina en 02 h 14 min 09 s» para lo que está pasando y «Empieza en 3 d 04 h …» para lo siguiente
@Component({
  selector: 'app-cuenta-atras',
  template: `
    @if (eventos() === null) {
      <div class="cuenta cuenta--esqueleto" aria-label="Cargando"><i></i></div>
    } @else {
      @for (c of lista(); track c.clave + c.modo) {
        <div class="cuenta" role="timer" [style.--c]="color(c.color)" [attr.aria-label]="(c.modo === 'termina' ? 'Termina' : 'Empieza') + ' ' + c.titulo">
          <span class="cuenta__tipo">{{ c.modo === 'termina' ? 'Termina en' : 'Empieza en' }}</span>
          <strong class="cuenta__titulo">{{ c.titulo }}</strong>
          <span class="cuenta__tiempo">
            @if (p(c).d > 0) { <b>{{ p(c).d }}</b><small>d</small> }
            <b>{{ dos(p(c).h) }}</b><small>h</small><b>{{ dos(p(c).m) }}</b><small>min</small><b>{{ dos(p(c).s) }}</b><small>s</small>
          </span>
        </div>
      }
    }
  `,
  styles: `
    :host { display: grid; gap: 8px; }
    :host:empty { display: none; }
    .cuenta { display: grid; gap: 2px; padding: 10px 12px; border-radius: 16px; background: color-mix(in srgb, var(--c, var(--brand)) 12%, transparent); border-left: 3px solid var(--c, var(--brand)); }
    .cuenta__tipo { font: 600 11px/1 var(--font-text); letter-spacing: .08em; text-transform: uppercase; color: var(--soft); }
    .cuenta__titulo { font: 600 15px/1.25 var(--font-text); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .cuenta__tiempo { display: flex; align-items: baseline; gap: 3px; font-variant-numeric: tabular-nums; }
    .cuenta__tiempo b { font: 600 22px/1.1 var(--font-display); margin-left: 6px; }
    .cuenta__tiempo b:first-child { margin-left: 0; }
    .cuenta__tiempo small { font: 500 12px/1 var(--font-text); color: var(--soft); }
    .cuenta--esqueleto { min-height: 66px; }
    .cuenta--esqueleto i { display: block; height: 100%; min-height: 44px; border-radius: 10px; background: linear-gradient(90deg, transparent, color-mix(in srgb, var(--texto) 10%, transparent), transparent) 0 0 / 200% 100%; animation: barrido 1.4s linear infinite; }
    @keyframes barrido { to { background-position: -200% 0; } }
    @media (prefers-reduced-motion: reduce) { .cuenta--esqueleto i { animation: none; } }
  `,
})
export class CuentaAtras {
  readonly eventos = input<Aparicion[] | null>(null);
  private readonly ahora = signal(Date.now());
  protected readonly lista = computed(() => cuentas(this.eventos() ?? [], this.ahora()));
  protected readonly color = colorEvento;

  constructor() {
    const t = setInterval(() => this.ahora.set(Date.now()), 1000);
    inject(DestroyRef).onDestroy(() => clearInterval(t));
  }

  protected p(c: Cuenta) { return partes(c.ms); }
  protected dos(n: number): string { return String(n).padStart(2, '0'); }
}
