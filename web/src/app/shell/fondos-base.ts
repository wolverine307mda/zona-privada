// Lo común a todos los fondos animados: tipos, color (con «fuerza»), resolución nítida y algunas piezas de dibujo que se repiten.

export interface Paleta {
  r: number; g: number; b: number; oscuro: boolean;
  /** fuerza del color, 0.5..2 (1 = tal cual): más saturación y más opacidad. Se cambia en vivo desde Ajustes */
  fuerza?: number;
}
/** intensidad 0..1 (cuánto se ve), velocidad 0.25..2 (cuánto se mueve) y `ligero`: en el móvil cada escena dibuja menos cosas y más grandes */
export interface Opciones { intensidad: number; velocidad: number; ligero?: boolean; }
export interface Escena {
  /** el lienzo cambia de tamaño (px de CSS); cada escena decide su resolución */
  cambiar(w: number, h: number): void;
  dibujar(t: number, dt: number): void;
  puntero(x: number, y: number): void;
  pixelado?: boolean;
}
export type Rgb = [number, number, number];

export const FRIO: Rgb = [58, 91, 255];
export const TAU = Math.PI * 2;
export const lerp = (a: number, b: number, t: number) => a + (b - a) * t;
export const suave = (a: number, b: number, x: number) => { const t = Math.min(1, Math.max(0, (x - a) / (b - a))); return t * t * (3 - 2 * t); };
export const azar = (a: number, b: number) => a + Math.random() * (b - a);
const tope = (x: number) => Math.max(0, Math.min(255, x));

/** Un color suelto (rojo, verde, azul) con la «fuerza» de la paleta aplicada: más saturado si la fuerza es mayor que 1, más gris si es menor */
export function fuerte(p: Paleta, r: number, g: number, b: number): Rgb {
  const f = p.fuerza ?? 1;
  if (f === 1) return [r, g, b];
  const gris = 0.3 * r + 0.59 * g + 0.11 * b, s = 1 + (f - 1) * 0.85;
  return [tope(gris + (r - gris) * s), tope(gris + (g - gris) * s), tope(gris + (b - gris) * s)];
}
/** Cuánto se multiplica la opacidad por la fuerza (1 = nada) */
export const alfaFuerza = (p: Paleta): number => { const f = p.fuerza ?? 1; return f >= 1 ? 1 + (f - 1) * 0.9 : 0.35 + 0.65 * f; };

/** El color de la paleta con una transparencia, mezclado hacia otro color si hace falta y con la «fuerza» aplicada (más saturado y más opaco) */
export function col(p: Paleta, a: number, k = 0, hacia: Rgb = [255, 255, 255]): string {
  const [r, g, b] = fuerte(p, lerp(p.r, hacia[0], k), lerp(p.g, hacia[1], k), lerp(p.b, hacia[2], k));
  return `rgba(${Math.round(r)},${Math.round(g)},${Math.round(b)},${Math.max(0, Math.min(1, a * alfaFuerza(p))).toFixed(3)})`;
}

/** Una paleta de colores fijos (los fondos con tema propio) que sigue leyendo en vivo el modo y la fuerza de la paleta del tema */
export function fija(p: Paleta, r: number, g: number, b: number): Paleta {
  const q = Object.create(p) as Paleta;
  q.r = r; q.g = g; q.b = b;
  return q;
}

/** Pone el lienzo a la resolución de la pantalla (hasta 2× en ordenador y 1,5× en el móvil, para que no se vea borroso) y deja el dibujo en píxeles de CSS */
export function ajustar(canvas: HTMLCanvasElement, ctx: CanvasRenderingContext2D, w: number, h: number, op: Opciones): void {
  const dpr = typeof devicePixelRatio === 'number' && devicePixelRatio > 0 ? devicePixelRatio : 1;
  const k = Math.max(1, Math.min(op.ligero ? 1.5 : 2, dpr));
  canvas.width = Math.max(1, Math.round(w * k)); canvas.height = Math.max(1, Math.round(h * k));
  ctx.setTransform(k, 0, 0, k, 0, 0);
}

/** En modo oscuro la luz se suma (brilla); en claro se pinta encima de lo que hay */
export const aditivo = (ctx: CanvasRenderingContext2D, p: Paleta) => { ctx.globalCompositeOperation = p.oscuro ? 'lighter' : 'source-over'; };
export const normal = (ctx: CanvasRenderingContext2D) => { ctx.globalCompositeOperation = 'source-over'; };

/** Un resplandor redondo: del color del centro al color (transparente) del borde */
export function halo(ctx: CanvasRenderingContext2D, x: number, y: number, r: number, centro: string, borde: string): void {
  const g = ctx.createRadialGradient(x, y, 0, x, y, r);
  g.addColorStop(0, centro); g.addColorStop(1, borde);
  ctx.fillStyle = g; ctx.beginPath(); ctx.arc(x, y, r, 0, TAU); ctx.fill();
}

/** Ruido suave y barato (suma de senos) entre -1 y 1: sirve para caminos y corrientes que no se repiten a la vista */
export function ruido(x: number, y: number, t: number): number {
  return (Math.sin(x * 1.7 + t) + Math.sin(y * 2.3 - t * 0.7) + Math.sin((x + y) * 1.1 + t * 0.5) + Math.sin((x - y) * 0.7 - t * 0.3)) / 4;
}

/** Una escena que se calcula en baja resolución (un color por celda) y se estira: el estirado la suaviza. `color` da el rojo, verde, azul y opacidad (0..1) de cada punto */
export function plasma(canvas: HTMLCanvasElement, op: Opciones, celda: number, color: (v: number, borde: number, x: number, y: number) => [number, number, number, number], onda: (x: number, y: number, t: number) => number): Escena {
  const ctx = canvas.getContext('2d')!;
  let W = 0, H = 0, img: ImageData;
  return {
    cambiar(w, h) {
      const d = op.ligero ? celda * 1.9 : celda;
      W = Math.max(30, Math.round(w / d)); H = Math.max(24, Math.round(h / d));
      canvas.width = W; canvas.height = H; img = ctx.createImageData(W, H);
    },
    puntero() { /* sin ratón */ },
    dibujar(t) {
      const d = img.data, ar = W / H;
      for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) {
        const nx = (x / W) * ar * 3, ny = (y / H) * 3;
        const v = onda(nx, ny, t), [r, g, b, a] = color(v, Math.abs(v - 0.5) * 2, x / W, y / H), o = (y * W + x) * 4;
        d[o] = r; d[o + 1] = g; d[o + 2] = b; d[o + 3] = Math.max(0, Math.min(255, a * 255));
      }
      ctx.putImageData(img, 0, 0);
    },
  };
}

/** «#ff7a1f» (o «rgb(…)») a números; si no se entiende, el naranja de siempre */
export function paletaDe(brand: string, oscuro: boolean): Paleta {
  const h = /^#?([0-9a-f]{6})$/i.exec(brand.trim());
  if (h) { const n = parseInt(h[1], 16); return { r: (n >> 16) & 255, g: (n >> 8) & 255, b: n & 255, oscuro }; }
  const m = /rgba?\(\s*(\d+)[ ,]+(\d+)[ ,]+(\d+)/.exec(brand);
  return m ? { r: +m[1], g: +m[2], b: +m[3], oscuro } : { r: 255, g: 122, b: 31, oscuro };
}
