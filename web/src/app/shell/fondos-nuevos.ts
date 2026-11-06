// Fondos nuevos (se descargan solo si se eligen). Todos toman el color de acento del tema y respetan «cuánto se ve», «velocidad» y «fuerza del color».

import type { FondoId } from '../core/tema.service';
import { type Escena, type Opciones, type Paleta, FRIO, TAU, aditivo, ajustar, alfaFuerza, azar, col, fuerte, halo, lerp, normal, plasma, ruido, suave } from './fondos-base';

const CALIDO: [number, number, number] = [255, 214, 120];
const SIN_RATON = () => { /* este fondo no sigue al ratón */ };
/** los fondos nuevos parten con algo más de color que los clásicos */
const FUERZA_BASE = 1.4;

// ---- Nebulosa: nubes de luz enormes que derivan despacio y polvo de estrellas ----
function nebulosa(canvas: HTMLCanvasElement, p: Paleta, op: Opciones): Escena {
  const ctx = canvas.getContext('2d')!;
  let W = 0, H = 0;
  const nubes = Array.from({ length: 9 }, (_, i) => ({ fx: 0.05 + 0.021 * i, fy: 0.04 + 0.017 * ((i * 3) % 7), px: i * 1.7, py: i * 2.3, r: 0.3 + 0.07 * ((i * 5) % 4), k: (i % 4) / 4, a: 0.1 + 0.035 * (i % 3) }));
  let polvo: { x: number; y: number; r: number; f: number }[] = [];
  return {
    cambiar(w, h) { W = w; H = h; ajustar(canvas, ctx, w, h, op); polvo = Array.from({ length: op.ligero ? 60 : 180 }, () => ({ x: Math.random(), y: Math.random(), r: 0.4 + Math.random() * 1.2, f: Math.random() * 6.28 })); },
    puntero: SIN_RATON,
    dibujar(t) {
      ctx.clearRect(0, 0, W, H);
      aditivo(ctx, p);
      const I = op.intensidad * FUERZA_BASE, m = Math.max(W, H);
      for (const n of nubes) {
        const x = (0.5 + 0.42 * Math.sin(t * n.fx + n.px)) * W, y = (0.5 + 0.4 * Math.cos(t * n.fy + n.py)) * H;
        halo(ctx, x, y, n.r * m, col(p, n.a * I * 1.5, n.k, FRIO), col(p, 0, n.k, FRIO));
      }
      for (const s of polvo) {
        const a = (0.25 + 0.5 * (0.5 + 0.5 * Math.sin(t * 1.3 + s.f))) * (0.3 + 0.7 * I);
        ctx.fillStyle = p.oscuro ? `rgba(235,240,255,${(a * 0.85).toFixed(3)})` : col(p, a * 0.8); ctx.beginPath(); ctx.arc(s.x * W, s.y * H, s.r, 0, TAU); ctx.fill();
      }
      normal(ctx);
    },
  };
}

// ---- Plasma: marmol de luz líquida que fluye (se calcula pequeño y se estira) ----
function plasmaEsc(canvas: HTMLCanvasElement, p: Paleta, op: Opciones): Escena {
  return plasma(canvas, op, 7,
    (v) => {
      const u = suave(0.2, 0.92, v), claro = suave(0.78, 1, v) * 0.55, base = fuerte(p, lerp(p.r, FRIO[0], (1 - v) * 0.55), lerp(p.g, FRIO[1], (1 - v) * 0.55), lerp(p.b, FRIO[2], (1 - v) * 0.55));
      return [lerp(base[0], 255, claro), lerp(base[1], 255, claro), lerp(base[2], 255, claro), Math.min(1, (0.04 + 0.46 * u) * op.intensidad * FUERZA_BASE * alfaFuerza(p))];
    },
    (x, y, t) => { const a = Math.sin(x * 2.1 + t * 0.5), b = Math.sin(y * 2.7 - t * 0.4 + a), c = Math.sin((x + y) * 1.5 + t * 0.3 + b * 1.2); return 0.5 + (0.5 * (a + b + c)) / 3; });
}

// ---- Seda: hilos de luz que se cruzan y ondulan, como telas ----
function seda(canvas: HTMLCanvasElement, p: Paleta, op: Opciones): Escena {
  const ctx = canvas.getContext('2d')!;
  let W = 0, H = 0;
  const hilos = Array.from({ length: 18 }, (_, i) => ({ y: 0.12 + (0.76 * i) / 17, a1: 0.05 + 0.04 * ((i * 7) % 5) / 4, a2: 0.02 + 0.02 * ((i * 3) % 4) / 3, f1: 0.0034 + 0.0012 * ((i * 5) % 4), f2: 0.0085 + 0.002 * ((i * 3) % 3), v1: 0.25 + 0.12 * (i % 5), v2: 0.18 + 0.1 * ((i * 2) % 4), fase: i * 0.8, k: (i % 5) / 5 }));
  return {
    cambiar(w, h) { W = w; H = h; ajustar(canvas, ctx, w, h, op); },
    puntero: SIN_RATON,
    dibujar(t) {
      ctx.clearRect(0, 0, W, H);
      aditivo(ctx, p);
      const I = op.intensidad * FUERZA_BASE, paso = op.ligero ? 22 : 10;
      ctx.lineCap = 'round'; ctx.lineJoin = 'round';
      for (const c of hilos) {
        ctx.beginPath();
        for (let x = -paso; x <= W + paso; x += paso) {
          const y = c.y * H + Math.sin(x * c.f1 + t * c.v1 * 0.5 + c.fase) * H * c.a1 + Math.sin(x * c.f2 - t * c.v2 * 0.5) * H * c.a2;
          if (x > -paso) ctx.lineTo(x, y); else ctx.moveTo(x, y);
        }
        ctx.strokeStyle = col(p, 0.05 * I, c.k, FRIO); ctx.lineWidth = 9; ctx.stroke(); // el halo del hilo
        ctx.strokeStyle = col(p, 0.42 * I, c.k * 0.8 + 0.1, FRIO); ctx.lineWidth = 1.3; ctx.stroke();
      }
      normal(ctx);
    },
  };
}

// ---- Lluvia digital: columnas de luz que caen, con su cabeza brillante ----
function lluvia(canvas: HTMLCanvasElement, p: Paleta, op: Opciones): Escena {
  const ctx = canvas.getContext('2d')!;
  let W = 0, H = 0, cols: { x: number; y: number; v: number; largo: number; k: number }[] = [];
  const paso = 15;
  return {
    cambiar(w, h) {
      W = w; H = h; ajustar(canvas, ctx, w, h, op);
      const sep = op.ligero ? 30 : 20;
      cols = Array.from({ length: Math.ceil(w / sep) }, (_, i) => ({ x: i * sep + sep / 2, y: -Math.random() * h, v: 70 + Math.random() * 190, largo: 8 + Math.floor(Math.random() * 14), k: Math.random() * 0.4 }));
    },
    puntero: SIN_RATON,
    dibujar(_t, dt) {
      ctx.clearRect(0, 0, W, H);
      aditivo(ctx, p);
      const I = op.intensidad * FUERZA_BASE;
      for (const c of cols) {
        c.y += c.v * dt;
        if (c.y - c.largo * paso > H) { c.y = -Math.random() * H * 0.4; c.v = 70 + Math.random() * 190; c.largo = 8 + Math.floor(Math.random() * 14); }
        for (let j = 0; j < c.largo; j++) {
          const y = Math.floor((c.y - j * paso) / paso) * paso; // cada trazo en su casilla
          if (y < -paso || y > H) continue;
          const a = Math.pow(1 - j / c.largo, 1.6);
          ctx.fillStyle = col(p, (j === 0 ? 0.85 : 0.34) * a * I, j === 0 ? 0.75 : c.k);
          ctx.fillRect(c.x - 1.5, y, 3, paso * 0.62);
        }
        halo(ctx, c.x, c.y, 16, col(p, 0.2 * I, 0.5), col(p, 0, 0.5));
      }
      normal(ctx);
    },
  };
}

// ---- Luciérnagas: luces cálidas que flotan y parpadean ----
function luciernagas(canvas: HTMLCanvasElement, p: Paleta, op: Opciones): Escena {
  const ctx = canvas.getContext('2d')!;
  let W = 0, H = 0, bichos: { x: number; y: number; ax: number; ay: number; fx: number; fy: number; px: number; py: number; f: number; b: number; r: number }[] = [];
  return {
    cambiar(w, h) {
      W = w; H = h; ajustar(canvas, ctx, w, h, op);
      bichos = Array.from({ length: op.ligero ? 16 : 40 }, () => ({ x: Math.random(), y: Math.random(), ax: azar(0.03, 0.1), ay: azar(0.03, 0.09), fx: azar(0.1, 0.35), fy: azar(0.1, 0.3), px: Math.random() * 6.28, py: Math.random() * 6.28, f: Math.random() * 6.28, b: azar(0.5, 1.4), r: azar(1.6, 3.2) }));
    },
    puntero: SIN_RATON,
    dibujar(t) {
      ctx.clearRect(0, 0, W, H);
      aditivo(ctx, p);
      const I = op.intensidad * FUERZA_BASE;
      for (const b of bichos) {
        const x = (b.x + b.ax * Math.sin(t * b.fx + b.px) + 0.04 * Math.sin(t * 0.07 + b.f)) * W, y = (b.y + b.ay * Math.sin(t * b.fy + b.py)) * H;
        const lat = Math.pow(0.5 + 0.5 * Math.sin(t * b.b + b.f), 3); // parpadean de golpe y se apagan
        halo(ctx, x, y, b.r * 9, col(p, 0.38 * lat * I, 0.55, CALIDO), col(p, 0, 0.55, CALIDO));
        ctx.fillStyle = col(p, 0.95 * lat * I, 0.8, CALIDO); ctx.beginPath(); ctx.arc(x, y, b.r * 0.6, 0, TAU); ctx.fill();
      }
      normal(ctx);
    },
  };
}

// ---- Hexágonos: un panal cuyas celdas se encienden con ondas de luz ----
function hexagonos(canvas: HTMLCanvasElement, p: Paleta, op: Opciones): Escena {
  const ctx = canvas.getContext('2d')!;
  let W = 0, H = 0;
  const R = () => (op.ligero ? 46 : 30);
  const contorno = (cx: number, cy: number, r: number) => { ctx.beginPath(); for (let i = 0; i < 6; i++) { const a = (i * Math.PI) / 3; const x = cx + r * Math.cos(a), y = cy + r * Math.sin(a); if (i) ctx.lineTo(x, y); else ctx.moveTo(x, y); } ctx.closePath(); };
  return {
    cambiar(w, h) { W = w; H = h; ajustar(canvas, ctx, w, h, op); },
    puntero: SIN_RATON,
    dibujar(t) {
      ctx.clearRect(0, 0, W, H);
      aditivo(ctx, p);
      const I = op.intensidad * FUERZA_BASE, r = R(), dx = r * 1.5, dy = Math.sqrt(3) * r;
      const c1x = W * (0.5 + 0.35 * Math.sin(t * 0.13)), c1y = H * (0.5 + 0.3 * Math.cos(t * 0.11)), c2x = W * (0.5 + 0.4 * Math.cos(t * 0.09 + 2)), c2y = H * (0.5 + 0.35 * Math.sin(t * 0.14 + 1));
      ctx.lineWidth = 1;
      for (let i = -1; i * dx < W + dx; i++) for (let j = -1; j * dy < H + dy; j++) {
        const cx = i * dx, cy = j * dy + (i % 2 ? dy / 2 : 0);
        const v1 = Math.max(0, Math.sin(Math.hypot(cx - c1x, cy - c1y) * 0.011 - t * 1.3)), v2 = Math.max(0, Math.sin(Math.hypot(cx - c2x, cy - c2y) * 0.009 - t * 1.0 + 1));
        const luz = Math.min(1, Math.pow(v1, 6) + Math.pow(v2, 6));
        if (luz > 0.04) { contorno(cx, cy, r - 2); ctx.fillStyle = col(p, 0.3 * luz * I, 0.2); ctx.fill(); }
        contorno(cx, cy, r - 1); ctx.strokeStyle = col(p, (0.07 + 0.6 * luz) * I, 0.15 + luz * 0.4); ctx.stroke();
      }
      normal(ctx);
    },
  };
}

// ---- Circuitos: pistas de una placa con pulsos de luz que las recorren ----
interface Pista { pts: { x: number; y: number; d: number }[]; total: number; vel: number; fase: number; k: number }
function circuitos(canvas: HTMLCanvasElement, p: Paleta, op: Opciones): Escena {
  const ctx = canvas.getContext('2d')!;
  let W = 0, H = 0, pistas: Pista[] = [];
  const pos = (pi: Pista, d: number): { x: number; y: number } => {
    let i = 1; while (i < pi.pts.length - 1 && pi.pts[i].d < d) i++;
    const a = pi.pts[i - 1], b = pi.pts[i], s = b.d > a.d ? Math.min(1, Math.max(0, (d - a.d) / (b.d - a.d))) : 0;
    return { x: lerp(a.x, b.x, s), y: lerp(a.y, b.y, s) };
  };
  return {
    cambiar(w, h) {
      W = w; H = h; ajustar(canvas, ctx, w, h, op);
      const g = op.ligero ? 54 : 38, dirs = [[1, 0], [1, 1], [0, 1], [-1, 1], [-1, 0], [-1, -1], [0, -1], [1, -1]];
      pistas = Array.from({ length: op.ligero ? 14 : 32 }, () => {
        let x = Math.round(Math.random() * (w / g)) * g, y = Math.round(Math.random() * (h / g)) * g, dir = Math.floor(Math.random() * 8), d = 0;
        const pts = [{ x, y, d }];
        for (let s = 0; s < 9; s++) {
          dir = (dir + (Math.random() < 0.5 ? 1 : 7) + (Math.random() < 0.25 ? 1 : 0)) % 8; // gira 45° (a veces 90°)
          const n = 1 + Math.floor(Math.random() * 3), [vx, vy] = dirs[dir];
          x += vx * n * g; y += vy * n * g; d += Math.hypot(vx, vy) * n * g;
          pts.push({ x, y, d });
        }
        return { pts, total: d, vel: azar(70, 160), fase: Math.random(), k: Math.random() * 0.5 };
      });
    },
    puntero: SIN_RATON,
    dibujar(t) {
      ctx.clearRect(0, 0, W, H);
      aditivo(ctx, p);
      const I = op.intensidad * FUERZA_BASE;
      ctx.lineWidth = 1.2; ctx.lineJoin = 'round';
      for (const pi of pistas) {
        ctx.strokeStyle = col(p, 0.14 * I, pi.k); ctx.beginPath();
        pi.pts.forEach((q, i) => (i ? ctx.lineTo(q.x, q.y) : ctx.moveTo(q.x, q.y))); ctx.stroke();
        for (const q of [pi.pts[0], pi.pts[pi.pts.length - 1]]) { ctx.strokeStyle = col(p, 0.3 * I, pi.k); ctx.beginPath(); ctx.arc(q.x, q.y, 3.2, 0, TAU); ctx.stroke(); }
        const d = (((t * pi.vel) / pi.total + pi.fase) % 1) * pi.total, cabeza = pos(pi, d);
        ctx.lineWidth = 2; ctx.beginPath();
        for (let s = 14; s >= 0; s--) { const q = pos(pi, Math.max(0, d - s * 5)); if (s === 14) ctx.moveTo(q.x, q.y); else ctx.lineTo(q.x, q.y); }
        ctx.strokeStyle = col(p, 0.6 * I, 0.4 + pi.k); ctx.stroke(); ctx.lineWidth = 1.2;
        halo(ctx, cabeza.x, cabeza.y, 12, col(p, 0.5 * I, 0.6), col(p, 0, 0.6));
      }
      normal(ctx);
    },
  };
}

// ---- Caleidoscopio: pétalos de luz que giran y se reflejan ----
function caleidoscopio(canvas: HTMLCanvasElement, p: Paleta, op: Opciones): Escena {
  const ctx = canvas.getContext('2d')!;
  let W = 0, H = 0;
  return {
    cambiar(w, h) { W = w; H = h; ajustar(canvas, ctx, w, h, op); },
    puntero: SIN_RATON,
    dibujar(t) {
      ctx.clearRect(0, 0, W, H);
      aditivo(ctx, p);
      const I = op.intensidad * FUERZA_BASE, R = Math.hypot(W, H) * 0.42, n = op.ligero ? 8 : 12, petalos = op.ligero ? 3 : 5;
      ctx.lineWidth = 1.2; ctx.lineCap = 'round';
      for (let k = 0; k < n; k++) {
        ctx.save(); ctx.translate(W / 2, H / 2); ctx.rotate((k * TAU) / n + t * 0.045); if (k % 2) ctx.scale(1, -1);
        for (let j = 0; j < petalos; j++) {
          const e = R * (0.35 + 0.13 * j), a = Math.sin(t * 0.5 + j), b = Math.cos(t * 0.4 + j * 1.3);
          ctx.strokeStyle = col(p, (0.34 - 0.04 * j) * I, (j % 2) * 0.45, FRIO);
          ctx.beginPath(); ctx.moveTo(0, 0); ctx.bezierCurveTo(R * 0.3, R * 0.14 * a, R * 0.55, -R * 0.22 * b, e, R * 0.12 * Math.sin(t * 0.3 + j)); ctx.stroke();
          halo(ctx, e, R * 0.12 * Math.sin(t * 0.3 + j), 9, col(p, 0.35 * I, 0.5), col(p, 0, 0.5));
        }
        ctx.restore();
      }
      halo(ctx, W / 2, H / 2, R * 0.14, col(p, 0.28 * I, 0.4), col(p, 0, 0.4));
      normal(ctx);
    },
  };
}

// ---- Sonar: ondas que se expanden desde varios puntos y se cruzan ----
function sonar(canvas: HTMLCanvasElement, p: Paleta, op: Opciones): Escena {
  const ctx = canvas.getContext('2d')!;
  let W = 0, H = 0;
  const focos = [{ fx: 0.07, fy: 0.05, px: 0, py: 1, k: 0 }, { fx: 0.05, fy: 0.08, px: 2, py: 3, k: 0.5 }, { fx: 0.09, fy: 0.06, px: 4, py: 5, k: 0.2 }, { fx: 0.06, fy: 0.07, px: 1.5, py: 0.5, k: 0.7 }];
  return {
    cambiar(w, h) { W = w; H = h; ajustar(canvas, ctx, w, h, op); },
    puntero: SIN_RATON,
    dibujar(t) {
      ctx.clearRect(0, 0, W, H);
      aditivo(ctx, p);
      const I = op.intensidad * FUERZA_BASE, R = Math.max(W, H) * 0.55, anillos = op.ligero ? 3 : 5;
      for (const f of focos) {
        const x = (0.5 + 0.38 * Math.sin(t * f.fx + f.px)) * W, y = (0.5 + 0.36 * Math.cos(t * f.fy + f.py)) * H;
        for (let i = 0; i < anillos; i++) {
          const vida = (t / 7 + i / anillos + f.px) % 1, a = Math.pow(1 - vida, 2);
          ctx.strokeStyle = col(p, 0.55 * a * I, f.k, FRIO); ctx.lineWidth = 1 + 3 * (1 - vida);
          ctx.beginPath(); ctx.arc(x, y, vida * R, 0, TAU); ctx.stroke();
        }
        halo(ctx, x, y, 14, col(p, 0.4 * I, f.k, FRIO), col(p, 0, f.k, FRIO));
      }
      normal(ctx);
    },
  };
}

// ---- Flujo: partículas que dejan estelas siguiendo una corriente invisible ----
function flujo(canvas: HTMLCanvasElement, p: Paleta, op: Opciones): Escena {
  const ctx = canvas.getContext('2d')!;
  let W = 0, H = 0, ps: { x: number; y: number; vida: number; k: number; v: number }[] = [];
  const nueva = () => ({ x: Math.random() * W, y: Math.random() * H, vida: azar(2, 7), k: Math.random() * 0.6, v: azar(40, 110) });
  return {
    cambiar(w, h) { W = w; H = h; ajustar(canvas, ctx, w, h, op); ps = Array.from({ length: op.ligero ? 220 : 620 }, nueva); },
    puntero: SIN_RATON,
    dibujar(t, dt) {
      ctx.globalCompositeOperation = 'destination-out'; ctx.fillStyle = 'rgba(0,0,0,0.09)'; ctx.fillRect(0, 0, W, H); // las estelas se apagan poco a poco
      aditivo(ctx, p);
      const I = op.intensidad * FUERZA_BASE;
      ctx.lineWidth = 1.5; ctx.lineCap = 'round';
      for (const q of ps) {
        const a = ruido(q.x * 0.0026, q.y * 0.0026, t * 0.12) * TAU * 1.2, nx = q.x + Math.cos(a) * q.v * Math.max(dt, 0.016) * 1.6, ny = q.y + Math.sin(a) * q.v * Math.max(dt, 0.016) * 1.6;
        ctx.strokeStyle = col(p, 0.6 * I, q.k, FRIO); ctx.beginPath(); ctx.moveTo(q.x, q.y); ctx.lineTo(nx, ny); ctx.stroke();
        q.x = nx; q.y = ny; q.vida -= dt;
        if (q.vida <= 0 || nx < -10 || nx > W + 10 || ny < -10 || ny > H + 10) Object.assign(q, nueva());
      }
      normal(ctx);
    },
  };
}

// ---- Cristales: una malla de triángulos que brillan y se mueven, como un diamante tallado ----
function cristales(canvas: HTMLCanvasElement, p: Paleta, op: Opciones): Escena {
  const ctx = canvas.getContext('2d')!;
  let W = 0, H = 0, cols = 0, filas = 0, base: { x: number; y: number }[] = [];
  return {
    cambiar(w, h) {
      W = w; H = h; ajustar(canvas, ctx, w, h, op);
      cols = op.ligero ? 6 : 11; filas = Math.max(4, Math.round((cols * h) / w) + 1);
      base = Array.from({ length: (cols + 1) * (filas + 1) }, (_, i) => { const c = i % (cols + 1), f = Math.floor(i / (cols + 1)); return { x: (c / cols) * w + azar(-0.3, 0.3) * (w / cols), y: (f / filas) * h + azar(-0.3, 0.3) * (h / filas) }; });
    },
    puntero: SIN_RATON,
    dibujar(t) {
      ctx.clearRect(0, 0, W, H);
      aditivo(ctx, p);
      const I = op.intensidad * FUERZA_BASE, pt = (c: number, f: number) => { const q = base[f * (cols + 1) + c]; return [q.x + Math.sin(t * 0.21 + c * 1.7 + f) * 14, q.y + Math.cos(t * 0.17 + f * 1.3 + c) * 14]; };
      for (let f = 0; f < filas; f++) for (let c = 0; c < cols; c++) {
        const a = pt(c, f), b = pt(c + 1, f), d = pt(c, f + 1), e = pt(c + 1, f + 1);
        [[a, b, d], [b, e, d]].forEach((tri, k) => {
          const brillo = 0.5 + 0.5 * Math.sin(t * 0.55 + c * 1.31 + f * 0.93 + k * 2.1);
          ctx.fillStyle = col(p, (0.03 + 0.16 * brillo * brillo) * I, ((c + f + k) % 3) * 0.3, FRIO);
          ctx.beginPath(); ctx.moveTo(tri[0][0], tri[0][1]); ctx.lineTo(tri[1][0], tri[1][1]); ctx.lineTo(tri[2][0], tri[2][1]); ctx.closePath(); ctx.fill();
          ctx.strokeStyle = col(p, (0.05 + 0.14 * brillo) * I, 0.4); ctx.lineWidth = 1; ctx.stroke();
        });
      }
      normal(ctx);
    },
  };
}

// ---- Nevada: copos en tres capas de profundidad ----
function nevada(canvas: HTMLCanvasElement, p: Paleta, op: Opciones): Escena {
  const ctx = canvas.getContext('2d')!;
  let W = 0, H = 0, copos: { x: number; y: number; r: number; v: number; s: number; f: number; z: number }[] = [];
  return {
    cambiar(w, h) {
      W = w; H = h; ajustar(canvas, ctx, w, h, op);
      const n = op.ligero ? 80 : 200;
      copos = Array.from({ length: n }, () => { const z = Math.random(); return { x: Math.random() * w, y: Math.random() * h, r: 0.8 + z * 3.2, v: 14 + z * 56, s: azar(10, 40) * (0.4 + z), f: Math.random() * 6.28, z }; });
    },
    puntero: SIN_RATON,
    dibujar(t, dt) {
      ctx.clearRect(0, 0, W, H);
      const I = op.intensidad * FUERZA_BASE;
      for (const c of copos) {
        c.y += c.v * dt; if (c.y > H + 6) { c.y = -6; c.x = Math.random() * W; }
        const x = c.x + Math.sin(t * 0.5 + c.f) * c.s * 0.5, a = (0.3 + 0.5 * c.z) * (0.3 + 0.7 * I);
        if (c.z > 0.7) halo(ctx, x, c.y, c.r * 3.2, p.oscuro ? `rgba(225,236,255,${(a * 0.7).toFixed(3)})` : col(p, a * 0.5, 0.5), p.oscuro ? 'rgba(225,236,255,0)' : col(p, 0, 0.5));
        ctx.fillStyle = p.oscuro ? `rgba(240,246,255,${a.toFixed(3)})` : col(p, a * 0.9, 0.3); ctx.beginPath(); ctx.arc(x, c.y, c.r, 0, TAU); ctx.fill();
      }
    },
  };
}

// ---- Cometas: bólidos con larga cola cruzando un cielo de polvo ----
function cometas(canvas: HTMLCanvasElement, p: Paleta, op: Opciones): Escena {
  const ctx = canvas.getContext('2d')!;
  let W = 0, H = 0, polvo: { x: number; y: number; r: number; f: number }[] = [], bolidos: { x: number; y: number; vx: number; vy: number; largo: number }[] = [], espera = 0.5;
  return {
    cambiar(w, h) { W = w; H = h; ajustar(canvas, ctx, w, h, op); polvo = Array.from({ length: op.ligero ? 50 : 130 }, () => ({ x: Math.random() * w, y: Math.random() * h, r: 0.3 + Math.random() * 1.1, f: Math.random() * 6.28 })); bolidos = [{ x: w * 0.8, y: h * 0.15, vx: -380, vy: 240, largo: Math.min(380, w * 0.4) }]; espera = 1.5; },
    puntero: SIN_RATON,
    dibujar(t, dt) {
      ctx.clearRect(0, 0, W, H);
      aditivo(ctx, p);
      const I = op.intensidad * FUERZA_BASE;
      for (const s of polvo) { ctx.fillStyle = p.oscuro ? `rgba(235,240,255,${((0.15 + 0.3 * (0.5 + 0.5 * Math.sin(t + s.f))) * (0.3 + 0.7 * I)).toFixed(3)})` : col(p, 0.2 * I); ctx.beginPath(); ctx.arc(s.x, s.y, s.r, 0, TAU); ctx.fill(); }
      espera -= dt;
      if (bolidos.length < (op.ligero ? 2 : 4) && espera <= 0) { const v = azar(320, 560); bolidos.push({ x: azar(W * 0.35, W * 1.1), y: azar(-60, H * 0.35), vx: -v, vy: v * azar(0.45, 0.7), largo: azar(230, 420) }); espera = azar(1.2, 3.5); }
      bolidos = bolidos.filter((b) => b.x > -b.largo && b.y < H + b.largo);
      for (const b of bolidos) {
        b.x += b.vx * dt; b.y += b.vy * dt;
        const m = Math.hypot(b.vx, b.vy), ux = -b.vx / m, uy = -b.vy / m, seg = 26;
        for (let i = 0; i < seg; i++) {
          const f = i / seg, f2 = (i + 1) / seg;
          ctx.strokeStyle = col(p, (1 - f) * 0.7 * I, 0.25 + f * 0.2, FRIO); ctx.lineWidth = 3.2 * (1 - f) + 0.4;
          ctx.beginPath(); ctx.moveTo(b.x + ux * b.largo * f, b.y + uy * b.largo * f); ctx.lineTo(b.x + ux * b.largo * f2, b.y + uy * b.largo * f2); ctx.stroke();
        }
        halo(ctx, b.x, b.y, 22, col(p, 0.6 * I, 0.6), col(p, 0, 0.6));
        ctx.fillStyle = col(p, 0.95 * I, 0.85); ctx.beginPath(); ctx.arc(b.x, b.y, 2.4, 0, TAU); ctx.fill();
      }
      normal(ctx);
    },
  };
}

// ---- Haces: focos de escenario que barren el aire ----
function haces(canvas: HTMLCanvasElement, p: Paleta, op: Opciones): Escena {
  const ctx = canvas.getContext('2d')!;
  let W = 0, H = 0;
  const focos = Array.from({ length: 7 }, (_, i) => ({ x: (i + 0.5) / 7, arriba: i % 2 === 0, f: 0.12 + 0.05 * ((i * 3) % 5), ph: i * 1.3, k: (i % 4) / 4, ancho: 0.05 + 0.03 * (i % 3) }));
  return {
    cambiar(w, h) { W = w; H = h; ajustar(canvas, ctx, w, h, op); },
    puntero: SIN_RATON,
    dibujar(t) {
      ctx.clearRect(0, 0, W, H);
      aditivo(ctx, p);
      const I = op.intensidad * FUERZA_BASE, largo = Math.hypot(W, H) * 1.1;
      for (const f of focos) {
        const ang = (f.arriba ? Math.PI / 2 : -Math.PI / 2) + Math.sin(t * f.f + f.ph) * 0.75;
        ctx.save(); ctx.translate(f.x * W, f.arriba ? -12 : H + 12); ctx.rotate(ang);
        const g = ctx.createLinearGradient(0, 0, largo, 0);
        g.addColorStop(0, col(p, 0.36 * I, f.k, FRIO)); g.addColorStop(0.55, col(p, 0.1 * I, f.k, FRIO)); g.addColorStop(1, col(p, 0, f.k, FRIO));
        ctx.fillStyle = g; ctx.beginPath(); ctx.moveTo(0, 0); ctx.lineTo(largo, -largo * f.ancho); ctx.lineTo(largo, largo * f.ancho); ctx.closePath(); ctx.fill();
        halo(ctx, 0, 0, 40, col(p, 0.5 * I, 0.5, FRIO), col(p, 0, 0.5, FRIO));
        ctx.restore();
      }
      normal(ctx);
    },
  };
}

export function crearNuevo(id: FondoId, canvas: HTMLCanvasElement, p: Paleta, op: Opciones): Escena | null {
  switch (id) {
    case 'nebulosa': return nebulosa(canvas, p, op);
    case 'plasma': return plasmaEsc(canvas, p, op);
    case 'seda': return seda(canvas, p, op);
    case 'lluvia': return lluvia(canvas, p, op);
    case 'luciernagas': return luciernagas(canvas, p, op);
    case 'hexagonos': return hexagonos(canvas, p, op);
    case 'circuitos': return circuitos(canvas, p, op);
    case 'caleidoscopio': return caleidoscopio(canvas, p, op);
    case 'sonar': return sonar(canvas, p, op);
    case 'flujo': return flujo(canvas, p, op);
    case 'cristales': return cristales(canvas, p, op);
    case 'nevada': return nevada(canvas, p, op);
    case 'cometas': return cometas(canvas, p, op);
    case 'haces': return haces(canvas, p, op);
    default: return null;
  }
}
