// Los fondos animados de la app. Cada uno es un dibujo en un canvas que se mueve despacio y toma el color de acento del tema.
// Este archivo solo se descarga cuando se elige uno distinto de «Aura» y «Nada».

import type { FondoId } from '../core/tema.service';

export interface Paleta { r: number; g: number; b: number; oscuro: boolean; }
/** intensidad 0..1 (cuánto se ve), velocidad 0.25..2 (cuánto se mueve) y `ligero`: en el móvil cada escena dibuja menos cosas y más grandes */
export interface Opciones { intensidad: number; velocidad: number; ligero?: boolean; }
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
    cambiar(w, h) { const d = op.ligero ? 16 : 9; W = Math.max(30, Math.round(w / d)); H = Math.max(24, Math.round(h / d)); canvas.width = W; canvas.height = H; img = ctx.createImageData(W, H); },
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
      const tw = op.ligero ? Math.max(84, W / 5) : Math.max(56, Math.min(88, W / 16)), th = tw / 2, n = Math.ceil(W / tw + H / th) + 2, I = op.intensidad;
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
      const s = op.ligero ? 36 : 26, I = op.intensidad;
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
  const B = op.ligero ? 7 : 5;
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
      ps = Array.from({ length: Math.min(op.ligero ? 34 : 90, Math.round((w * h) / 17000)) }, () => ({ x: Math.random() * w, y: Math.random() * h, vx: (Math.random() - 0.5) * 24, vy: (Math.random() - 0.5) * 24 }));
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

// ---- Boreal: cintas de luz que ondulan, como una aurora (se dibuja pequeño y se estira: el estirado las difumina) ----
function boreal(canvas: HTMLCanvasElement, p: Paleta, op: Opciones): Escena {
  const ctx = canvas.getContext('2d')!;
  let W = 0, H = 0;
  const cintas = [{ y: 0.3, a: 0.2, f: 0.9, k: 0.15, fase: 0 }, { y: 0.44, a: 0.15, f: 0.7, k: 0.5, fase: 2 }, { y: 0.22, a: 0.12, f: 1.15, k: 0.35, fase: 4 }];
  return {
    cambiar(w, h) { W = Math.max(48, Math.round(w / 8)); H = Math.max(80, Math.round(h / 8)); canvas.width = W; canvas.height = H; },
    puntero() { /* sin ratón */ },
    dibujar(t) {
      ctx.clearRect(0, 0, W, H);
      ctx.globalCompositeOperation = p.oscuro ? 'lighter' : 'source-over';
      const I = op.intensidad, n = 26;
      for (const c of cintas) {
        const g = ctx.createLinearGradient(0, H * (c.y - 0.12), 0, H * (c.y + 0.3));
        g.addColorStop(0, col(p, 0, c.k)); g.addColorStop(0.45, col(p, c.a * I * 1.5, c.k)); g.addColorStop(1, col(p, 0, c.k));
        ctx.fillStyle = g; ctx.beginPath();
        for (let i = 0; i <= n; i++) {
          const x = (W * i) / n, y = H * c.y + H * 0.09 * Math.sin(i * 0.5 * c.f + t * 0.35 * c.f + c.fase) + H * 0.04 * Math.sin(i * 0.9 + t * 0.5);
          if (i) ctx.lineTo(x, y); else ctx.moveTo(x, y);
        }
        for (let i = n; i >= 0; i--) ctx.lineTo((W * i) / n, H * c.y + H * 0.24 + H * 0.07 * Math.sin(i * 0.4 + t * 0.3 * c.f + c.fase + 1));
        ctx.closePath(); ctx.fill();
      }
      ctx.globalCompositeOperation = 'source-over';
    },
  };
}

// ---- Bokeh: luces desenfocadas que suben despacio, como las de una foto de noche ----
function bokeh(canvas: HTMLCanvasElement, p: Paleta, op: Opciones): Escena {
  const ctx = canvas.getContext('2d')!;
  let W = 0, H = 0;
  let luces: { x: number; y: number; r: number; v: number; f: number; k: number; a: number }[] = [];
  return {
    cambiar(w, h) {
      W = w; H = h; canvas.width = w; canvas.height = h;
      const m = Math.min(w, h);
      luces = Array.from({ length: op.ligero ? 9 : 20 }, () => ({ x: Math.random(), y: Math.random(), r: m * (0.05 + Math.random() * 0.1), v: 0.004 + Math.random() * 0.01, f: Math.random() * 6.28, k: Math.random() * 0.7, a: 0.12 + Math.random() * 0.2 }));
    },
    puntero() { /* sin ratón */ },
    dibujar(t, dt) {
      ctx.clearRect(0, 0, W, H);
      const I = op.intensidad;
      for (const l of luces) {
        l.y -= l.v * dt; if (l.y < -0.2) { l.y = 1.2; l.x = Math.random(); }
        const x = (l.x + 0.03 * Math.sin(t * 0.2 + l.f)) * W, y = l.y * H, a = l.a * I * (0.75 + 0.25 * Math.sin(t * 0.6 + l.f));
        const g = ctx.createRadialGradient(x, y, 0, x, y, l.r);
        g.addColorStop(0, col(p, a * 0.5, l.k)); g.addColorStop(0.8, col(p, a * 0.7, l.k)); g.addColorStop(1, col(p, 0, l.k));
        ctx.fillStyle = g; ctx.beginPath(); ctx.arc(x, y, l.r, 0, Math.PI * 2); ctx.fill();
      }
    },
  };
}

// ---- Olas: capas de ondas que se mecen una sobre otra ----
function olas(canvas: HTMLCanvasElement, p: Paleta, op: Opciones): Escena {
  const ctx = canvas.getContext('2d')!;
  let W = 0, H = 0;
  const capas = [{ b: 0.6, am: 0.03, fr: 0.006, v: 0.5, a: 0.07, k: 0.4 }, { b: 0.7, am: 0.035, fr: 0.008, v: -0.4, a: 0.09, k: 0.2 }, { b: 0.8, am: 0.03, fr: 0.01, v: 0.6, a: 0.11, k: 0.1 }, { b: 0.9, am: 0.025, fr: 0.013, v: -0.5, a: 0.14, k: 0 }];
  return {
    cambiar(w, h) { W = w; H = h; canvas.width = w; canvas.height = h; },
    puntero() { /* sin ratón */ },
    dibujar(t) {
      ctx.clearRect(0, 0, W, H);
      const I = op.intensidad, paso = op.ligero ? 16 : 8;
      for (const c of capas) {
        ctx.fillStyle = col(p, c.a * I, c.k); ctx.beginPath(); ctx.moveTo(0, H);
        for (let x = 0; x <= W + paso; x += paso) ctx.lineTo(x, H * c.b + H * c.am * Math.sin(x * c.fr + t * c.v) + H * c.am * 0.5 * Math.sin(x * c.fr * 2.1 + t * c.v * 1.4));
        ctx.lineTo(W + paso, H); ctx.closePath(); ctx.fill();
      }
    },
  };
}

// ---- Estrellas: un cielo de noche que gira despacio, con alguna estrella fugaz ----
function estrellas(canvas: HTMLCanvasElement, p: Paleta, op: Opciones): Escena {
  const ctx = canvas.getContext('2d')!;
  let W = 0, H = 0;
  let astros: { x: number; y: number; r: number; f: number; v: number; c: number }[] = [];
  let fugaz: { x: number; y: number; vx: number; vy: number; vida: number } | null = null, espera = 4;
  return {
    cambiar(w, h) {
      W = w; H = h; canvas.width = w; canvas.height = h;
      astros = Array.from({ length: Math.min(op.ligero ? 70 : 160, Math.round((w * h) / 9000)) }, () => ({ x: Math.random() * w, y: Math.random() * h, r: 0.5 + Math.random() * 1.4, f: Math.random() * 6.28, v: 1 + Math.random() * 5, c: Math.random() < 0.3 ? 1 : 0 }));
    },
    puntero() { /* sin ratón */ },
    dibujar(t, dt) {
      ctx.clearRect(0, 0, W, H);
      const I = op.intensidad;
      for (const a of astros) {
        a.x += a.v * dt; if (a.x > W + 2) { a.x = -2; a.y = Math.random() * H; }
        const alfa = (0.25 + 0.55 * (0.5 + 0.5 * Math.sin(t * 1.4 + a.f))) * (0.35 + 0.65 * I);
        ctx.fillStyle = a.c ? col(p, alfa, 0.2) : p.oscuro ? `rgba(235,240,255,${(alfa * 0.9).toFixed(3)})` : `rgba(50,60,90,${(alfa * 0.55).toFixed(3)})`;
        ctx.beginPath(); ctx.arc(a.x, a.y, a.r, 0, Math.PI * 2); ctx.fill();
      }
      espera -= dt;
      if (!fugaz && espera <= 0) { fugaz = { x: Math.random() * W * 0.8, y: Math.random() * H * 0.4, vx: 380 + Math.random() * 200, vy: 170 + Math.random() * 100, vida: 1 }; espera = 5 + Math.random() * 7; }
      if (fugaz) {
        fugaz.x += fugaz.vx * dt; fugaz.y += fugaz.vy * dt; fugaz.vida -= dt * 1.4;
        if (fugaz.vida <= 0) fugaz = null;
        else {
          const g = ctx.createLinearGradient(fugaz.x, fugaz.y, fugaz.x - fugaz.vx * 0.18, fugaz.y - fugaz.vy * 0.18);
          g.addColorStop(0, col(p, 0.8 * fugaz.vida * I, 0.5)); g.addColorStop(1, col(p, 0, 0.5));
          ctx.strokeStyle = g; ctx.lineWidth = 1.6; ctx.beginPath(); ctx.moveTo(fugaz.x, fugaz.y); ctx.lineTo(fugaz.x - fugaz.vx * 0.18, fugaz.y - fugaz.vy * 0.18); ctx.stroke();
        }
      }
    },
  };
}

// ---- Rejilla: un suelo de líneas en perspectiva que avanza hacia ti ----
function rejilla(canvas: HTMLCanvasElement, p: Paleta, op: Opciones): Escena {
  const ctx = canvas.getContext('2d')!;
  let W = 0, H = 0;
  return {
    cambiar(w, h) { W = w; H = h; canvas.width = w; canvas.height = h; },
    puntero() { /* sin ratón */ },
    dibujar(t) {
      ctx.clearRect(0, 0, W, H);
      const I = op.intensidad, hor = H * 0.5, cx = W / 2, n = 16, columnas = op.ligero ? 10 : 16;
      ctx.lineWidth = 1;
      for (let i = 0; i < n; i++) {
        const z = (i + ((t * 0.18) % 1)) / n, y = hor + (H - hor) * z * z;
        ctx.strokeStyle = col(p, 0.38 * I * z, 0.2); ctx.beginPath(); ctx.moveTo(0, y); ctx.lineTo(W, y); ctx.stroke();
      }
      for (let i = -columnas; i <= columnas; i++) {
        const g = ctx.createLinearGradient(0, hor, 0, H);
        g.addColorStop(0, col(p, 0, 0.2)); g.addColorStop(1, col(p, 0.3 * I, 0.2));
        ctx.strokeStyle = g; ctx.beginPath(); ctx.moveTo(cx + i * W * 0.012, hor); ctx.lineTo(cx + i * W * (op.ligero ? 0.22 : 0.14), H); ctx.stroke();
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
    case 'boreal': return boreal(canvas, p, op);
    case 'bokeh': return bokeh(canvas, p, op);
    case 'olas': return olas(canvas, p, op);
    case 'estrellas': return estrellas(canvas, p, op);
    case 'rejilla': return rejilla(canvas, p, op);
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
