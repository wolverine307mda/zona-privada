// Los fondos animados de la app. Cada uno es un dibujo en un canvas que se mueve despacio y toma el color de acento del tema.
// Este archivo solo se descarga cuando se elige uno distinto de «Aura» y «Nada».

import type { FondoId } from '../core/tema.service';

export interface Paleta { r: number; g: number; b: number; oscuro: boolean; }
/** intensidad 0..1 (cuánto se ve) y velocidad 0.25..2 (cuánto se mueve) */
export interface Opciones { intensidad: number; velocidad: number; }
export interface Escena {
  /** el lienzo cambia de tamaño (px de CSS); cada escena decide su resolución */
  cambiar(w: number, h: number): void;
  dibujar(t: number, dt: number): void;
  puntero(x: number, y: number): void;
  pixelado?: boolean;
}

const FRIO: [number, number, number] = [58, 91, 255];
const suave = (a: number, b: number, x: number) => { const t = Math.min(1, Math.max(0, (x - a) / (b - a))); return t * t * (3 - 2 * t); };
const lerp = (a: number, b: number, t: number) => a + (b - a) * t;

// el color de acento con una transparencia, mezclado hacia otro color si hace falta
function col(p: Paleta, a: number, k = 0, hacia: [number, number, number] = [255, 255, 255]): string {
  return `rgba(${Math.round(lerp(p.r, hacia[0], k))},${Math.round(lerp(p.g, hacia[1], k))},${Math.round(lerp(p.b, hacia[2], k))},${Math.max(0, Math.min(1, a)).toFixed(3)})`;
}

// ---- Líquido: metaballs que se funden, calculadas en baja resolución y estiradas (el estirado las suaviza) ----
function liquido(canvas: HTMLCanvasElement, p: Paleta, op: Opciones): Escena {
  const ctx = canvas.getContext('2d')!;
  let W = 0, H = 0, img: ImageData;
  const bolas = Array.from({ length: 8 }, (_, i) => ({ fx: 0.11 + 0.047 * i, fy: 0.09 + 0.041 * ((i * 3) % 8), px: i * 1.9, py: i * 2.7, r: 0.085 + (0.045 * ((i * 5) % 4)) / 3, frio: i % 2 }));
  return {
    cambiar(w, h) { W = Math.max(40, Math.round(w / 9)); H = Math.max(24, Math.round(h / 9)); canvas.width = W; canvas.height = H; img = ctx.createImageData(W, H); },
    puntero() { /* el líquido no sigue al ratón */ },
    dibujar(t) {
      const ar = W / H, d = img.data;
      const pos = bolas.map((b) => [(0.5 + 0.42 * Math.sin(t * b.fx + b.px)) * ar, 0.5 + 0.42 * Math.cos(t * b.fy + b.py)]);
      const amax = 0.5 * op.intensidad + 0.05;
      for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) {
        const nx = x / H, ny = y / H;
        let f = 0, m = 0;
        for (let i = 0; i < pos.length; i++) {
          const dx = nx - pos[i][0], dy = ny - pos[i][1];
          const k = (bolas[i].r * bolas[i].r) / (dx * dx + dy * dy + 1e-4);
          f += k; if (bolas[i].frio) m += k;
        }
        const o = (y * W + x) * 4, a = suave(0.8, 1.35, f);
        if (a <= 0) { d[o + 3] = 0; continue; }
        const mezcla = f > 0 ? (m / f) * 0.55 : 0, borde = 1 - suave(1.35, 2.3, f); // más claro hacia el borde
        d[o] = lerp(p.r, FRIO[0], mezcla) + 60 * borde; d[o + 1] = lerp(p.g, FRIO[1], mezcla) + 60 * borde; d[o + 2] = lerp(p.b, FRIO[2], mezcla) + 60 * borde;
        d[o + 3] = 255 * a * amax * (0.7 + 0.3 * borde);
      }
      ctx.putImageData(img, 0, 0);
    },
  };
}

// ---- Isométrico: un suelo de cubos que suben y bajan en ondas ----
function isometrico(canvas: HTMLCanvasElement, p: Paleta, op: Opciones): Escena {
  const ctx = canvas.getContext('2d')!;
  let W = 0, H = 0;
  return {
    cambiar(w, h) { W = w; H = h; canvas.width = w; canvas.height = h; },
    puntero() { /* sin ratón */ },
    dibujar(t) {
      ctx.clearRect(0, 0, W, H);
      const tw = Math.max(56, Math.min(88, W / 16)), th = tw / 2, n = Math.ceil(W / tw + H / th) + 2, I = op.intensidad;
      const x0 = W / 2, y0 = -th * 3;
      for (let s = 0; s < n * 2; s++) {
        for (let i = Math.max(0, s - n + 1); i <= Math.min(s, n - 1); i++) {
          const j = s - i;
          const cx = x0 + (i - j) * (tw / 2), cy = y0 + (i + j) * (th / 2);
          if (cx < -tw || cx > W + tw || cy < -tw || cy > H + tw) continue;
          const alto = (tw * 0.9 * (Math.sin(i * 0.45 + t * 0.9) + Math.sin(j * 0.5 - t * 0.7) + 2)) / 4, y = cy - alto;
          const claro = alto / (tw * 0.9);
          ctx.fillStyle = col(p, (0.04 + 0.16 * claro) * I, 0.25); // cara de arriba
          ctx.beginPath(); ctx.moveTo(cx, y - th / 2); ctx.lineTo(cx + tw / 2, y); ctx.lineTo(cx, y + th / 2); ctx.lineTo(cx - tw / 2, y); ctx.closePath(); ctx.fill();
          ctx.fillStyle = col(p, 0.1 * I, 0, p.oscuro ? [0, 0, 0] : [60, 60, 60]); // lado izquierdo
          ctx.beginPath(); ctx.moveTo(cx - tw / 2, y); ctx.lineTo(cx, y + th / 2); ctx.lineTo(cx, cy + th / 2); ctx.lineTo(cx - tw / 2, cy); ctx.closePath(); ctx.fill();
          ctx.fillStyle = col(p, 0.06 * I, 0.5); // lado derecho
          ctx.beginPath(); ctx.moveTo(cx + tw / 2, y); ctx.lineTo(cx, y + th / 2); ctx.lineTo(cx, cy + th / 2); ctx.lineTo(cx + tw / 2, cy); ctx.closePath(); ctx.fill();
          ctx.strokeStyle = col(p, 0.14 * I, 0.3); ctx.lineWidth = 1;
          ctx.beginPath(); ctx.moveTo(cx - tw / 2, y); ctx.lineTo(cx, y - th / 2); ctx.lineTo(cx + tw / 2, y); ctx.stroke();
        }
      }
    },
  };
}

// ---- Cómic: trama de puntos de imprenta (benday) que crece y se encoge en ondas ----
function comic(canvas: HTMLCanvasElement, p: Paleta, op: Opciones): Escena {
  const ctx = canvas.getContext('2d')!;
  let W = 0, H = 0;
  return {
    cambiar(w, h) { W = w; H = h; canvas.width = w; canvas.height = h; },
    puntero() { /* sin ratón */ },
    dibujar(t) {
      ctx.clearRect(0, 0, W, H);
      const s = 26, I = op.intensidad;
      const capa = (paso: number, desfase: number, color: string, escala: number) => {
        ctx.fillStyle = color;
        for (let gy = -1, fila = 0; gy * paso < H + paso; gy++, fila++) {
          for (let gx = -1; gx * paso < W + paso; gx++) {
            const x = gx * paso + (fila % 2 ? paso / 2 : 0) + desfase, y = gy * paso * 0.866 + desfase;
            const onda = 0.5 + 0.5 * Math.sin(x * 0.011 + y * 0.008 + t * 0.55);
            const caida = 1 - y / (H * 1.1); // más grandes arriba, como en el cómic impreso
            const r = paso * 0.5 * escala * onda * Math.max(0.12, caida);
            if (r < 0.6) continue;
            ctx.beginPath(); ctx.arc(x, y, r, 0, Math.PI * 2); ctx.fill();
          }
        }
      };
      capa(s, 0, col(p, 0.24 * I), 1);
      capa(s * 1.9, s * 0.5, col({ ...p, r: FRIO[0], g: FRIO[1], b: FRIO[2] }, 0.14 * I), 1.1);
    },
  };
}

// ---- Píxel art: estrellas que centellean a saltos, nieve de píxeles y montañas a escalones ----
function pixel(canvas: HTMLCanvasElement, p: Paleta, op: Opciones): Escena {
  const ctx = canvas.getContext('2d')!;
  const B = 5;
  let W = 0, H = 0;
  let astros: { x: number; y: number; v: number; f: number; c: number }[] = [];
  return {
    pixelado: true,
    cambiar(w, h) {
      W = Math.ceil(w / B); H = Math.ceil(h / B); canvas.width = W; canvas.height = H;
      astros = Array.from({ length: Math.round((W * H) / 150) }, () => ({ x: Math.random() * W, y: Math.random() * H, v: 1 + Math.random() * 5, f: Math.random() * 6.28, c: Math.random() < 0.35 ? 1 : 0 }));
    },
    puntero() { /* sin ratón */ },
    dibujar(t, dt) {
      ctx.clearRect(0, 0, W, H);
      const I = op.intensidad;
      ctx.fillStyle = col(p, 0.14 * I, 0.1); // montañas
      for (let x = 0; x < W; x++) {
        const h = Math.round(H * 0.1 + H * 0.07 * (Math.sin(x * 0.07 + 1) + Math.sin(x * 0.17 + t * 0.05) * 0.5 + 1));
        ctx.fillRect(x, H - h, 1, h);
      }
      for (const a of astros) {
        a.y += a.v * dt; if (a.y > H) { a.y = 0; a.x = Math.random() * W; }
        const brillo = Math.floor((Math.sin(t * 2 + a.f) + 1) * 1.5); // 0, 1 o 2: centelleo a saltos
        const alfa = [0.14, 0.34, 0.62][brillo] * (0.4 + 0.6 * I);
        ctx.fillStyle = a.c ? col(p, alfa, 0.1) : p.oscuro ? `rgba(235,240,255,${(alfa * 0.8).toFixed(3)})` : `rgba(40,50,70,${(alfa * 0.6).toFixed(3)})`;
        ctx.fillRect(Math.floor(a.x), Math.floor(a.y), 1, 1);
      }
    },
  };
}

// ---- Partículas: una red de puntos que se unen con líneas y se apartan del puntero ----
function particulas(canvas: HTMLCanvasElement, p: Paleta, op: Opciones): Escena {
  const ctx = canvas.getContext('2d')!;
  let W = 0, H = 0, mx = -999, my = -999;
  let ps: { x: number; y: number; vx: number; vy: number }[] = [];
  return {
    cambiar(w, h) {
      W = w; H = h; canvas.width = w; canvas.height = h;
      ps = Array.from({ length: Math.min(90, Math.round((w * h) / 17000)) }, () => ({ x: Math.random() * w, y: Math.random() * h, vx: (Math.random() - 0.5) * 24, vy: (Math.random() - 0.5) * 24 }));
    },
    puntero(x, y) { mx = x; my = y; },
    dibujar(_t, dt) {
      ctx.clearRect(0, 0, W, H);
      const I = op.intensidad, d = 140;
      for (const a of ps) {
        a.x += a.vx * dt; a.y += a.vy * dt;
        if (a.x < 0 || a.x > W) a.vx *= -1;
        if (a.y < 0 || a.y > H) a.vy *= -1;
        const dx = a.x - mx, dy = a.y - my, dd = dx * dx + dy * dy;
        if (dd < 12000 && dd > 1) { const f = (1 - dd / 12000) * 60 * dt, l = Math.sqrt(dd); a.x += (dx / l) * f; a.y += (dy / l) * f; }
      }
      for (let i = 0; i < ps.length; i++) {
        for (let j = i + 1; j < ps.length; j++) {
          const dx = ps[i].x - ps[j].x, dy = ps[i].y - ps[j].y, q = dx * dx + dy * dy;
          if (q > d * d) continue;
          ctx.strokeStyle = col(p, (1 - Math.sqrt(q) / d) * 0.4 * I, 0.2); ctx.lineWidth = 1;
          ctx.beginPath(); ctx.moveTo(ps[i].x, ps[i].y); ctx.lineTo(ps[j].x, ps[j].y); ctx.stroke();
        }
        ctx.fillStyle = col(p, 0.55 * I, 0.3);
        ctx.beginPath(); ctx.arc(ps[i].x, ps[i].y, 1.8, 0, Math.PI * 2); ctx.fill();
      }
    },
  };
}

export function crearEscena(id: FondoId, canvas: HTMLCanvasElement, p: Paleta, op: Opciones): Escena | null {
  switch (id) {
    case 'liquido': return liquido(canvas, p, op);
    case 'isometrico': return isometrico(canvas, p, op);
    case 'comic': return comic(canvas, p, op);
    case 'pixel': return pixel(canvas, p, op);
    case 'particulas': return particulas(canvas, p, op);
    default: return null;
  }
}

/** «#ff7a1f» (o «rgb(…)») a números; si no se entiende, el naranja de siempre */
export function paletaDe(brand: string, oscuro: boolean): Paleta {
  const h = /^#?([0-9a-f]{6})$/i.exec(brand.trim());
  if (h) { const n = parseInt(h[1], 16); return { r: (n >> 16) & 255, g: (n >> 8) & 255, b: n & 255, oscuro }; }
  const m = /rgba?\(\s*(\d+)[ ,]+(\d+)[ ,]+(\d+)/.exec(brand);
  return m ? { r: +m[1], g: +m[2], b: +m[3], oscuro } : { r: 255, g: 122, b: 31, oscuro };
}
