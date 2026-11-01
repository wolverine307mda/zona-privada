import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { Instante, azar, colorCielo, colorNube, colorSol, luzDeDia, mezcla, pathFaseLuna } from './modelo';

const HORIZONTE = 330;
type Capa = 'baja' | 'media' | 'alta';
interface Puff { x: number; y: number; s: number; }

const clamp01 = (v: number): number => Math.min(1, Math.max(0, v));

// lo que se dibuja una vez: estrellas, y la posición de las nubes de cada capa
const ESTRELLAS = (() => {
  const r = azar(7);
  return Array.from({ length: 90 }, (_, i) => ({ x: r() * 800, y: r() * 300, r: 0.5 + r() * 1.3, o: 0.35 + r() * 0.65, d: (r() * 4).toFixed(1), brilla: i % 4 === 0 }));
})();
const PUFFS: Record<Capa, Puff[]> = (() => {
  const mk = (semilla: number, y0: number, y1: number, s0: number, s1: number): Puff[] => {
    const r = azar(semilla);
    return Array.from({ length: 12 }, (_, i) => ({ x: (i + r() * 0.8) * (800 / 12), y: y0 + r() * (y1 - y0), s: s0 + r() * (s1 - s0) }));
  };
  return { baja: mk(11, 250, 310, 1.2, 1.8), media: mk(23, 150, 240, 0.9, 1.4), alta: mk(37, 55, 140, 0.8, 1.3) };
})();
const GOTAS = (() => { const r = azar(5); return Array.from({ length: 46 }, () => ({ x: r() * 840 - 20, d: (r() * 0.7).toFixed(2), l: 10 + r() * 14 })); })();
const CAPAS: { id: Capa; n: number; opacidad: number; duracion: number }[] = [
  { id: 'alta', n: 12, opacidad: 0.55, duracion: 190 }, { id: 'media', n: 12, opacidad: 0.78, duracion: 120 }, { id: 'baja', n: 12, opacidad: 0.92, duracion: 70 },
];

// La escena: cielo, sol, luna, estrellas, Vía Láctea, tres capas de nubes, lluvia y una silueta con su sombra.
// Todo sale de los datos reales del instante (altura y acimut, cobertura de cada capa, probabilidad de lluvia), nada es decorativo.
@Component({
  selector: 'app-cielo',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <svg class="cielo" viewBox="0 0 800 450" preserveAspectRatio="xMidYMid slice" role="img" [attr.aria-label]="etiqueta()">
      <defs>
        <linearGradient id="gCielo" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0" [attr.stop-color]="color().arriba"/><stop offset="1" [attr.stop-color]="color().abajo"/>
        </linearGradient>
        <radialGradient id="gSol"><stop offset="0" [attr.stop-color]="colorDelSol()" stop-opacity="1"/><stop offset="0.35" [attr.stop-color]="colorDelSol()" stop-opacity=".55"/><stop offset="1" [attr.stop-color]="colorDelSol()" stop-opacity="0"/></radialGradient>
        <radialGradient id="gLuna"><stop offset="0" stop-color="#cfd9ff" stop-opacity=".5"/><stop offset="1" stop-color="#cfd9ff" stop-opacity="0"/></radialGradient>
        <radialGradient id="gAurora"><stop offset="0" stop-color="#ff8f45" stop-opacity=".75"/><stop offset="1" stop-color="#ff8f45" stop-opacity="0"/></radialGradient>
        <linearGradient id="gVia" x1="0" y1="0" x2="1" y2="0">
          <stop offset="0" stop-color="#9fb4ff" stop-opacity="0"/><stop offset="0.5" stop-color="#d7defa" stop-opacity=".9"/><stop offset="1" stop-color="#9fb4ff" stop-opacity="0"/>
        </linearGradient>
        @for (c of capas; track c.id) {
          <radialGradient [attr.id]="'gN-' + c.id"><stop offset="0" [attr.stop-color]="tinte()[c.id]" stop-opacity="1"/><stop offset="0.55" [attr.stop-color]="tinte()[c.id]" stop-opacity=".7"/><stop offset="1" [attr.stop-color]="tinte()[c.id]" stop-opacity="0"/></radialGradient>
        }
        <linearGradient id="gSuelo" x1="0" y1="0" x2="0" y2="1"><stop offset="0" [attr.stop-color]="suelo().arriba"/><stop offset="1" [attr.stop-color]="suelo().abajo"/></linearGradient>
      </defs>

      <rect width="800" height="450" fill="url(#gCielo)"/>

      <!-- resplandor del horizonte hacia donde está el sol -->
      <ellipse [attr.cx]="sol().x" [attr.cy]="HORIZONTE - 6" rx="360" ry="120" fill="url(#gAurora)" [attr.opacity]="resplandor()"/>

      <!-- estrellas y Vía Láctea -->
      <g [attr.opacity]="opacidadEstrellas()">
        <g [attr.transform]="'translate(' + nucleo().x + ' ' + nucleo().y + ') rotate(-58)'" [attr.opacity]="opacidadVia()">
          <ellipse rx="330" ry="46" fill="url(#gVia)"/><ellipse rx="190" ry="22" fill="url(#gVia)" opacity=".7"/>
        </g>
        @for (e of estrellas; track $index) {
          <circle [attr.cx]="e.x" [attr.cy]="e.y" [attr.r]="e.r" fill="#fff" [attr.opacity]="e.o" [class.brilla]="e.brilla" [style.animation-delay]="e.d + 's'"/>
        }
      </g>

      <!-- luna -->
      <g [attr.transform]="'translate(' + luna().x + ' ' + luna().y + ')'" [attr.opacity]="luna().o">
        <circle r="46" fill="url(#gLuna)"/>
        <circle r="17" fill="#1d2542"/>
        <path [attr.d]="fasePath()" [attr.transform]="creciente() ? '' : 'scale(-1 1)'" fill="#f4f1e4"/>
      </g>

      <!-- sol -->
      <g [attr.transform]="'translate(' + sol().x + ' ' + sol().y + ')'" [attr.opacity]="sol().o">
        <circle [attr.r]="sol().halo" fill="url(#gSol)"/>
        <circle [attr.r]="sol().r" [attr.fill]="colorDelSol()"/>
      </g>

      <!-- nubes: tres capas con deriva distinta (la lejana va más despacio) -->
      @for (c of capas; track c.id) {
        <g [attr.opacity]="c.opacidad * cobertura()[c.id].o">
          <g class="deriva" [style.animation-duration]="c.duracion + 's'">
            @for (copia of [0, 800]; track copia) {
              @for (p of puffs[c.id]; track $index; let k = $index) {
                @if (k < cobertura()[c.id].n) {
                  <g [attr.transform]="'translate(' + (p.x + copia) + ' ' + p.y + ') scale(' + p.s + ')'">
                    <ellipse cx="0" cy="0" rx="52" [attr.ry]="c.id === 'alta' ? 11 : 17" [attr.fill]="'url(#gN-' + c.id + ')'"/>
                    <ellipse cx="-30" cy="5" rx="34" [attr.ry]="c.id === 'alta' ? 8 : 13" [attr.fill]="'url(#gN-' + c.id + ')'"/>
                    <ellipse cx="30" cy="6" rx="38" [attr.ry]="c.id === 'alta' ? 8 : 14" [attr.fill]="'url(#gN-' + c.id + ')'"/>
                    @if (c.id !== 'alta') { <ellipse cx="2" cy="-11" rx="30" ry="15" [attr.fill]="'url(#gN-' + c.id + ')'"/> }
                  </g>
                }
              }
            }
          </g>
        </g>
      }

      <!-- lluvia -->
      @if (lluvia() > 0) {
        <g [attr.opacity]="lluvia()" stroke="#bcd0ee" stroke-width="1.2" stroke-linecap="round">
          @for (g of gotas; track $index) { <line class="gota" [attr.x1]="g.x" y1="-30" [attr.x2]="g.x - 5" [attr.y2]="-30 + g.l" [style.animation-delay]="g.d + 's'"/> }
        </g>
      }

      <!-- el suelo, una silueta y su sombra -->
      <path [attr.d]="relieve" fill="url(#gSuelo)"/>
      @if (sombra(); as s) {
        <path [attr.d]="s.path" fill="#000" [attr.opacity]="s.o"/>
      }
      <g transform="translate(400 392)" [attr.fill]="figura()">
        <circle cx="0" cy="-58" r="7"/><rect x="-7" y="-50" width="14" height="30" rx="5"/><rect x="-7" y="-22" width="5.5" height="22" rx="2.5"/><rect x="1.5" y="-22" width="5.5" height="22" rx="2.5"/>
      </g>
    </svg>
  `,
  styles: `
    :host { display: block; position: absolute; inset: 0; } /* llena su contenedor (que es position: relative) */
    .cielo { display: block; width: 100%; height: 100%; }
    .deriva { animation: deriva linear infinite; }
    @keyframes deriva { to { transform: translateX(-800px); } }
    .brilla { animation: brilla 3.6s ease-in-out infinite; }
    @keyframes brilla { 50% { opacity: .25; } }
    .gota { animation: cae .8s linear infinite; }
    @keyframes cae { to { transform: translate(-30px, 520px); } }
    @media (prefers-reduced-motion: reduce) { .deriva, .brilla, .gota { animation: none; } }
  `,
})
export class Cielo {
  readonly i = input.required<Instante>();
  readonly fase = input.required<{ iluminacion: number; creciente: boolean }>();
  readonly etiqueta = input('');

  protected readonly HORIZONTE = HORIZONTE;
  protected readonly relieve = `M0 ${HORIZONTE} C 90 ${HORIZONTE - 22}, 170 ${HORIZONTE - 4}, 260 ${HORIZONTE - 14} S 420 ${HORIZONTE - 28}, 520 ${HORIZONTE - 10} S 700 ${HORIZONTE - 24}, 800 ${HORIZONTE - 8} L800 450 L0 450 Z`;
  protected readonly estrellas = ESTRELLAS;
  protected readonly puffs = PUFFS;
  protected readonly gotas = GOTAS;
  protected readonly capas = CAPAS;

  protected readonly color = computed(() => colorCielo(this.i().sol.alt));
  protected readonly colorDelSol = computed(() => colorSol(this.i().sol.alt));
  protected readonly creciente = computed(() => this.fase().creciente);
  protected readonly fasePath = computed(() => pathFaseLuna(0, 0, 17, this.fase().iluminacion));

  // el cielo mira al sur: el este queda a la izquierda y el oeste a la derecha
  private static x(az: number): number { return 400 + ((az - 180) / 135) * 270; }
  private static borde(az: number): number { return clamp01(1 - (Math.abs(az - 180) - 118) / 30); }

  protected readonly sol = computed(() => {
    const { alt, az } = this.i().sol;
    return { x: Cielo.x(az), y: HORIZONTE - alt * 3.7, r: alt < 12 ? 15 + (12 - Math.max(alt, -2)) * 0.5 : 15, halo: 70 + Math.max(0, 14 - alt) * 4, o: Cielo.borde(az) * (alt > -8 ? 1 : 0) };
  });
  protected readonly luna = computed(() => {
    const { alt, az } = this.i().luna;
    return { x: Cielo.x(az), y: HORIZONTE - alt * 3.7, o: Cielo.borde(az) * (alt > -6 ? 1 : 0) * (1 - clamp01((this.i().sol.alt + 2) / 14) * 0.85) };
  });
  protected readonly nucleo = computed(() => ({ x: Cielo.x(this.i().nucleo.az), y: HORIZONTE - this.i().nucleo.alt * 3.7 }));

  private readonly total = computed(() => this.i().nubes?.total ?? 0);
  protected readonly opacidadEstrellas = computed(() => clamp01((-5 - this.i().sol.alt) / 11) * (1 - this.total() / 100 * 0.92) * (1 - this.fase().iluminacion * clamp01(this.i().luna.alt / 8) * 0.55));
  protected readonly opacidadVia = computed(() => clamp01((-16 - this.i().sol.alt) / 4) * clamp01(this.i().nucleo.alt / 8) * (1 - this.fase().iluminacion * clamp01(this.i().luna.alt / 8)));
  protected readonly resplandor = computed(() => clamp01(1 - Math.abs(this.i().sol.alt - 0) / 12) * (1 - this.total() / 100 * 0.35));

  // cuántas nubes de cada capa y con qué fuerza (la cobertura real de esa capa)
  protected readonly cobertura = computed<Record<Capa, { n: number; o: number }>>(() => {
    const n = this.i().nubes;
    const de = (v: number | undefined) => ({ n: Math.round(((v ?? 0) / 100) * 12), o: clamp01(0.35 + (v ?? 0) / 100) });
    return { baja: de(n?.baja), media: de(n?.media), alta: de(n?.alta) };
  });
  protected readonly tinte = computed<Record<Capa, string>>(() => {
    const s = this.i().sol.alt, t = this.total();
    return { baja: colorNube(s, t, 'baja'), media: colorNube(s, t, 'media'), alta: colorNube(s, t, 'alta') };
  });
  protected readonly lluvia = computed(() => { const n = this.i().nubes; return n && n.lluvia >= 45 && n.total >= 55 ? clamp01((n.lluvia - 40) / 50) : 0; });

  protected readonly suelo = computed(() => {
    const d = luzDeDia(this.i().sol.alt);
    return { arriba: mezcla('#05070c', '#26372a', d), abajo: mezcla('#020306', '#101a14', d) };
  });
  protected readonly figura = computed(() => mezcla('#02030a', '#0c120e', luzDeDia(this.i().sol.alt)));

  // la sombra de la silueta (mide 70 px): opuesta al sol y tan larga como dice 1/tan(altura); se apaga con las nubes
  protected readonly sombra = computed(() => {
    const { sombra, nubes } = this.i();
    if (sombra == null) return null;
    const az = this.i().sol.az;
    const largo = Math.min(sombra * 70, 330);
    const dx = -Math.sin(((az - 180) * Math.PI) / 180) * largo;
    const dy = Math.cos(((az - 180) * Math.PI) / 180) * largo * 0.16 + 4;
    const x0 = 400, y0 = 392;
    const path = `M ${x0 - 6} ${y0} L ${x0 + 6} ${y0} L ${x0 + dx + 5} ${y0 + dy} L ${x0 + dx - 5} ${y0 + dy} Z`;
    return { path, o: 0.5 * (1 - (nubes?.total ?? 0) / 100 * 0.8) * clamp01(this.i().sol.alt / 4) };
  });
}
