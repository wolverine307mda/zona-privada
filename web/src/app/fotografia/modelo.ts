// Los datos de «Cuándo salir a hacer fotografía» y las cuentas que no son de dibujar: interpolar la serie, colores del cielo y puntos cardinales.

export interface Pos { alt: number; az: number; }
export interface NubesHora { total: number; baja: number; media: number; alta: number; lluvia: number; vis: number; viento: number; }
export interface Punto { min: number; etapa: string; sol: Pos; luna: Pos; nucleo: Pos; sombra: number | null; nubes: NubesHora | null; nota: number | null; }
export interface Ventana { tipo: string; titulo: string; inicio: string; fin: string; puntuacion: number | null; detalle: string; motivo?: string | null; }
export interface Efemerides {
  amanecer: string | null; atardecer: string | null; azAmanecer: number | null; azAtardecer: number | null;
  civilManana: string | null; civilTarde: string | null; nauticoManana: string | null; nauticoTarde: string | null; astroManana: string | null; astroTarde: string | null;
  mediodia: string; alturaMediodia: number; lunaSale: string | null; lunaPone: string | null; fase: string; iluminacionLuna: number;
}
export interface Dia {
  sinLugar: boolean; lugar?: string; fecha?: string; hoy?: boolean; efemerides?: Efemerides; serie?: Punto[]; ventanas?: Ventana[]; hayPrevision?: boolean;
}
export interface DiaSemana {
  fecha: string; amanecer: string | null; atardecer: string | null; fase: string; iluminacionLuna: number;
  nubes: { total: number; lluvia: number } | null; ventanas: Ventana[]; mejor: Ventana | null;
}
export interface Semana { sinLugar: boolean; lugar?: string; hayPrevision?: boolean; dias?: DiaSemana[]; mejores?: Ventana[]; }
export interface AjustesLuz { activa: boolean; umbral: number; antelacionMin: number; amanecer: boolean; astro: boolean; bortle: number; }

// lo que se pinta en un instante: la serie interpolada entre dos puntos de 10 minutos
export interface Instante {
  min: number; etapa: string; sol: Pos; luna: Pos; nucleo: Pos; sombra: number | null; nubes: NubesHora | null; nota: number | null;
}

const lerp = (a: number, b: number, f: number): number => a + (b - a) * f;
// el camino corto entre dos acimuts (0 y 359 están al lado)
const lerpAz = (a: number, b: number, f: number): number => { const d = ((b - a + 540) % 360) - 180; return (a + d * f + 360) % 360; };
const lerpPos = (a: Pos, b: Pos, f: number): Pos => ({ alt: lerp(a.alt, b.alt, f), az: lerpAz(a.az, b.az, f) });

export function instante(serie: Punto[], min: number): Instante {
  const m = Math.min(Math.max(min, 0), serie[serie.length - 1].min);
  const i = Math.min(Math.floor(m / 10), serie.length - 2);
  const a = serie[i], b = serie[i + 1], f = (m - a.min) / 10;
  return {
    min: m, etapa: f < 0.5 ? a.etapa : b.etapa,
    sol: lerpPos(a.sol, b.sol, f), luna: lerpPos(a.luna, b.luna, f), nucleo: lerpPos(a.nucleo, b.nucleo, f),
    sombra: a.sombra != null && b.sombra != null ? lerp(a.sombra, b.sombra, f) : (f < 0.5 ? a.sombra : b.sombra),
    nubes: a.nubes && b.nubes ? {
      total: lerp(a.nubes.total, b.nubes.total, f), baja: lerp(a.nubes.baja, b.nubes.baja, f), media: lerp(a.nubes.media, b.nubes.media, f),
      alta: lerp(a.nubes.alta, b.nubes.alta, f), lluvia: lerp(a.nubes.lluvia, b.nubes.lluvia, f), vis: lerp(a.nubes.vis, b.nubes.vis, f), viento: lerp(a.nubes.viento, b.nubes.viento, f),
    } : (f < 0.5 ? a.nubes : b.nubes),
    nota: f < 0.5 ? a.nota : b.nota,
  };
}

const PUNTOS = ['N', 'NNE', 'NE', 'ENE', 'E', 'ESE', 'SE', 'SSE', 'S', 'SSO', 'SO', 'OSO', 'O', 'ONO', 'NO', 'NNO'];
export const cardinal = (az: number): string => PUNTOS[Math.round((((az % 360) + 360) % 360) / 22.5) % 16];

export const hhmm = (min: number): string => {
  const m = Math.round(((min % 1440) + 1440) % 1440);
  return `${String(Math.floor(m / 60)).padStart(2, '0')}:${String(m % 60).padStart(2, '0')}`;
};

// ---------- colores ----------

type Rgb = [number, number, number];
const hex = (h: string): Rgb => [parseInt(h.slice(1, 3), 16), parseInt(h.slice(3, 5), 16), parseInt(h.slice(5, 7), 16)];
const aHex = (c: Rgb): string => '#' + c.map((v) => Math.round(Math.min(255, Math.max(0, v))).toString(16).padStart(2, '0')).join('');
export const mezcla = (a: string, b: string, f: number): string => { const x = hex(a), y = hex(b); return aHex([lerp(x[0], y[0], f), lerp(x[1], y[1], f), lerp(x[2], y[2], f)]); };
const clamp01 = (v: number): number => Math.min(1, Math.max(0, v));

// [altura del sol, color arriba, color en el horizonte]
const CIELO: [number, string, string][] = [
  [-20, '#04060f', '#0a1230'], [-12, '#0a1634', '#1a2858'], [-6, '#1a3470', '#6a4f8f'], [-2, '#2b4f92', '#f08a4b'],
  [3, '#3a6fc0', '#f7b765'], [10, '#4585d6', '#a9cdf0'], [30, '#3b82e0', '#9ccbf5'], [90, '#2a74dc', '#8fc3f6'],
];

export function colorCielo(alt: number): { arriba: string; abajo: string } {
  if (alt <= CIELO[0][0]) return { arriba: CIELO[0][1], abajo: CIELO[0][2] };
  for (let i = 1; i < CIELO.length; i++) {
    if (alt <= CIELO[i][0]) {
      const f = (alt - CIELO[i - 1][0]) / (CIELO[i][0] - CIELO[i - 1][0]);
      return { arriba: mezcla(CIELO[i - 1][1], CIELO[i][1], f), abajo: mezcla(CIELO[i - 1][2], CIELO[i][2], f) };
    }
  }
  return { arriba: CIELO[CIELO.length - 1][1], abajo: CIELO[CIELO.length - 1][2] };
}

// el color de las nubes: blancas de día, doradas al amanecer y atardecer, azul oscuro de noche y más grises si el cielo está cubierto
export function colorNube(altSol: number, total: number, capa: 'baja' | 'media' | 'alta'): string {
  const calido = clamp01(1 - Math.abs(altSol - 1) / 9) * (capa === 'baja' ? 0.55 : 1);
  const noche = clamp01((-4 - altSol) / 12);
  let c = mezcla('#ffffff', '#ffb27a', calido);
  c = mezcla(c, '#8d98ad', clamp01(total / 100) * (capa === 'baja' ? 0.45 : 0.2) * (1 - calido));
  return mezcla(c, '#1b2646', noche * 0.92);
}

export const luzDeDia = (altSol: number): number => clamp01((altSol + 8) / 22);

export const colorSol = (alt: number): string => mezcla('#ff9a3c', '#fff6cf', clamp01(alt / 25));

// la nota de 0 a 10 como color: rojo apagado → ámbar → verde
export function colorNota(n: number | null): string {
  if (n == null) return 'var(--soft)';
  return n >= 7 ? '#3fb96a' : n >= 5 ? '#f0b53a' : n >= 3 ? '#e2813a' : '#c9524f';
}

// ---------- la luna ----------

export function creciente(fase: string): boolean { return /creciente/.test(fase) && !/menguante/.test(fase); }

// el trozo iluminado: semicírculo del lado luminoso + terminador elíptico. f = iluminación (0-1). Sin espejo para creciente (luz a la derecha).
export function pathFaseLuna(cx: number, cy: number, r: number, f: number): string {
  const rx = Math.abs(1 - 2 * f) * r;
  const barrido = f < 0.5 ? 0 : 1;
  return `M ${cx} ${cy - r} A ${r} ${r} 0 0 1 ${cx} ${cy + r} A ${rx.toFixed(2)} ${r} 0 0 ${barrido} ${cx} ${cy - r} Z`;
}

// números repetibles para las estrellas y las nubes (no cambian al refrescar)
export function azar(semilla: number): () => number {
  let a = semilla >>> 0;
  return () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}
