// Los fondos animados de la app. Cada uno es un dibujo en un canvas que se mueve despacio y toma el color de acento del tema.
// Este archivo solo se descarga cuando se elige uno distinto de «Aura» y «Nada». Los nuevos y los inspirados en Marvel viven en sus propios archivos
// y se descargan solo si se eligen.

import type { FondoId } from '../core/tema.service';
import { type Escena, type Opciones, type Paleta, FRIO, TAU, ajustar, aditivo, alfaFuerza, col, fuerte, halo, lerp, normal, suave } from './fondos-base';

export { paletaDe, col } from './fondos-base';
export type { Escena, Opciones, Paleta } from './fondos-base';

// ---- Líquido: metaballs que se funden, calculadas en baja resolución y estiradas (el estirado las suaviza) ----
function liquido(canvas: HTMLCanvasElement, p: Paleta, op: Opciones): Escena {
  const ctx = canvas.getContext('2d')!;
  let W = 0, H = 0, img: ImageData;
  const bolas = Array.from({ length: 9 }, (_, i) => ({ fx: 0.11 + 0.047 * i, fy: 0.09 + 0.041 * ((i * 3) % 8), px: i * 1.9, py: i * 2.7, r: 0.085 + (0.045 * ((i * 5) % 4)) / 3, frio: i % 2 }));
  return {
    cambiar(w, h) { const d = op.ligero ? 12 : 6; W = Math.max(30, Math.round(w / d)); H = Math.max(24, Math.round(h / d)); canvas.width = W; canvas.height = H; img = ctx.createImageData(W, H); },
    puntero() { /* el líquido no sigue al ratón */ },
    dibujar(t) {
      const ar = W / H, d = img.data;
      const pos = bolas.map((b) => [(0.5 + 0.42 * Math.sin(t * b.fx + b.px)) * ar, 0.5 + 0.42 * Math.cos(t * b.fy + b.py)]);
      const amax = Math.min(1, (0.5 * op.intensidad + 0.05) * alfaFuerza(p));
      const base = fuerte(p, p.r, p.g, p.b), frio = fuerte(p, FRIO[0], FRIO[1], FRIO[2]);
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
        const mezcla = f > 0 ? (m / f) * 0.55 : 0, borde = 1 - suave(1.35, 2.3, f), brillo = suave(2.4, 4.2, f) * 40; // más claro hacia el borde y con un punto de luz en el centro
        d[o] = lerp(base[0], frio[0], mezcla) + 60 * borde + brillo; d[o + 1] = lerp(base[1], frio[1], mezcla) + 60 * borde + brillo; d[o + 2] = lerp(base[2], frio[2], mezcla) + 60 * borde + brillo;
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
    cambiar(w, h) { W = w; H = h; ajustar(canvas, ctx, w, h, op); },
    puntero() { /* sin ratón */ },
    dibujar(t) {
      ctx.clearRect(0, 0, W, H);
      const tw = op.ligero ? Math.max(84, W / 5) : Math.max(52, Math.min(80, W / 18)), th = tw / 2, n = Math.ceil(W / tw + H / th) + 2, I = op.intensidad;
      const x0 = W / 2, y0 = -th * 3;
      for (let s = 0; s < n * 2; s++) {
        for (let i = Math.max(0, s - n + 1); i <= Math.min(s, n - 1); i++) {
          const j = s - i;
          const cx = x0 + (i - j) * (tw / 2), cy = y0 + (i + j) * (th / 2);
          if (cx < -tw || cx > W + tw || cy < -tw || cy > H + tw) continue;
          const alto = (tw * 0.9 * (Math.sin(i * 0.45 + t * 0.9) + Math.sin(j * 0.5 - t * 0.7) + 2)) / 4, y = cy - alto;
          const claro = alto / (tw * 0.9);
          ctx.fillStyle = col(p, (0.05 + 0.2 * claro) * I, 0.25); // cara de arriba
          ctx.beginPath(); ctx.moveTo(cx, y - th / 2); ctx.lineTo(cx + tw / 2, y); ctx.lineTo(cx, y + th / 2); ctx.lineTo(cx - tw / 2, y); ctx.closePath(); ctx.fill();
          ctx.fillStyle = col(p, 0.12 * I, 0, p.oscuro ? [0, 0, 0] : [60, 60, 60]); // lado izquierdo
          ctx.beginPath(); ctx.moveTo(cx - tw / 2, y); ctx.lineTo(cx, y + th / 2); ctx.lineTo(cx, cy + th / 2); ctx.lineTo(cx - tw / 2, cy); ctx.closePath(); ctx.fill();
          ctx.fillStyle = col(p, 0.07 * I, 0.5); // lado derecho
          ctx.beginPath(); ctx.moveTo(cx + tw / 2, y); ctx.lineTo(cx, y + th / 2); ctx.lineTo(cx, cy + th / 2); ctx.lineTo(cx + tw / 2, cy); ctx.closePath(); ctx.fill();
          if (claro > 0.6) { // el filo de los cubos más altos se enciende
            ctx.strokeStyle = col(p, (claro - 0.6) * 0.9 * I, 0.5); ctx.lineWidth = 1;
            ctx.beginPath(); ctx.moveTo(cx - tw / 2, y); ctx.lineTo(cx, y - th / 2); ctx.lineTo(cx + tw / 2, y); ctx.stroke();
          }
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
    cambiar(w, h) { W = w; H = h; ajustar(canvas, ctx, w, h, op); },
    puntero() { /* sin ratón */ },
    dibujar(t) {
      ctx.clearRect(0, 0, W, H);
      const s = op.ligero ? 34 : 24, I = op.intensidad;
      const capa = (paso: number, desfase: number, color: string, escala: number, inclinada: number) => {
        ctx.fillStyle = color;
        for (let gy = -1, fila = 0; gy * paso < H + paso; gy++, fila++) {
          for (let gx = -1; gx * paso < W + paso; gx++) {
            const x = gx * paso + (fila % 2 ? paso / 2 : 0) + desfase, y = gy * paso * 0.866 + desfase;
            const onda = 0.5 + 0.5 * Math.sin(x * 0.011 + y * 0.008 + t * 0.55 + inclinada);
            const caida = 1 - y / (H * 1.1); // más grandes arriba, como en el cómic impreso
            const r = paso * 0.5 * escala * onda * Math.max(0.12, caida);
            if (r < 0.6) continue;
            ctx.beginPath(); ctx.arc(x, y, r, 0, TAU); ctx.fill();
          }
        }
      };
      capa(s, 0, col(p, 0.28 * I), 1, 0);
      capa(s * 1.9, s * 0.5, col({ ...p, r: FRIO[0], g: FRIO[1], b: FRIO[2] }, 0.17 * I), 1.1, 1.7);
      capa(s * 3.4, s, col(p, 0.1 * I, 0.6), 1.2, 3.1);
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
      astros = Array.from({ length: Math.max(10, Math.round((W * H) / 130)) }, () => ({ x: Math.random() * W, y: Math.random() * H, v: 1 + Math.random() * 5, f: Math.random() * 6.28, c: Math.random() < 0.35 ? 1 : 0 }));
    },
    puntero() { /* sin ratón */ },
    dibujar(t, dt) {
      ctx.clearRect(0, 0, W, H);
      const I = op.intensidad;
      for (const [k, alfa, vel] of [[0.12, 0.1, 0.03], [0.07, 0.16, 0.05]] as const) { // dos filas de montañas, la de atrás más baja
        ctx.fillStyle = col(p, alfa * I, 0.1);
        for (let x = 0; x < W; x++) {
          const h = Math.round(H * k + H * 0.07 * (Math.sin(x * 0.07 + 1 + k * 9) + Math.sin(x * 0.17 + t * vel) * 0.5 + 1));
          ctx.fillRect(x, H - h, 1, h);
        }
      }
      for (const a of astros) {
        a.y += a.v * dt; if (a.y > H) { a.y = 0; a.x = Math.random() * W; }
        const brillo = Math.floor((Math.sin(t * 2 + a.f) + 1) * 1.5); // 0, 1 o 2: centelleo a saltos
        const alfa = [0.14, 0.34, 0.66][brillo] * (0.4 + 0.6 * I);
        ctx.fillStyle = a.c ? col(p, alfa, 0.1) : p.oscuro ? `rgba(235,240,255,${(alfa * 0.8).toFixed(3)})` : `rgba(40,50,70,${(alfa * 0.6).toFixed(3)})`;
        ctx.fillRect(Math.floor(a.x), Math.floor(a.y), 1, 1);
        if (brillo === 2 && a.c) { ctx.fillRect(Math.floor(a.x) - 1, Math.floor(a.y), 3, 1); ctx.fillRect(Math.floor(a.x), Math.floor(a.y) - 1, 1, 3); } // la más brillante, en cruz
      }
    },
  };
}

// ---- Partículas: una red de puntos que se unen con líneas y se apartan del puntero ----
function particulas(canvas: HTMLCanvasElement, p: Paleta, op: Opciones): Escena {
  const ctx = canvas.getContext('2d')!;
  let W = 0, H = 0, mx = -999, my = -999;
  let ps: { x: number; y: number; vx: number; vy: number; r: number }[] = [];
  return {
    cambiar(w, h) {
      W = w; H = h; ajustar(canvas, ctx, w, h, op);
      ps = Array.from({ length: Math.max(14, Math.min(op.ligero ? 38 : 110, Math.round((w * h) / 14000))) }, () => ({ x: Math.random() * w, y: Math.random() * h, vx: (Math.random() - 0.5) * 24, vy: (Math.random() - 0.5) * 24, r: 1.4 + Math.random() * 1.8 }));
    },
    puntero(x, y) { mx = x; my = y; },
    dibujar(_t, dt) {
      ctx.clearRect(0, 0, W, H);
      const I = op.intensidad, d = op.ligero ? 120 : 150;
      for (const a of ps) {
        a.x += a.vx * dt; a.y += a.vy * dt;
        if (a.x < 0 || a.x > W) a.vx *= -1;
        if (a.y < 0 || a.y > H) a.vy *= -1;
        const dx = a.x - mx, dy = a.y - my, dd = dx * dx + dy * dy;
        if (dd < 14000 && dd > 1) { const f = (1 - dd / 14000) * 70 * dt, l = Math.sqrt(dd); a.x += (dx / l) * f; a.y += (dy / l) * f; }
      }
      aditivo(ctx, p);
      for (let i = 0; i < ps.length; i++) {
        for (let j = i + 1; j < ps.length; j++) {
          const dx = ps[i].x - ps[j].x, dy = ps[i].y - ps[j].y, q = dx * dx + dy * dy;
          if (q > d * d) continue;
          ctx.strokeStyle = col(p, (1 - Math.sqrt(q) / d) * 0.5 * I, 0.2); ctx.lineWidth = 1.1;
          ctx.beginPath(); ctx.moveTo(ps[i].x, ps[i].y); ctx.lineTo(ps[j].x, ps[j].y); ctx.stroke();
        }
      }
      for (const a of ps) { // cada punto con su resplandor
        halo(ctx, a.x, a.y, a.r * 5, col(p, 0.3 * I, 0.3), col(p, 0, 0.3));
        ctx.fillStyle = col(p, 0.8 * I, 0.45); ctx.beginPath(); ctx.arc(a.x, a.y, a.r, 0, TAU); ctx.fill();
      }
      normal(ctx);
    },
  };
}

// ---- Boreal: cintas de luz que ondulan, como una aurora (se dibuja pequeño y se estira: el estirado las difumina) ----
function boreal(canvas: HTMLCanvasElement, p: Paleta, op: Opciones): Escena {
  const ctx = canvas.getContext('2d')!;
  let W = 0, H = 0;
  const cintas = [
    { y: 0.3, a: 0.2, f: 0.9, k: 0.15, fase: 0 }, { y: 0.44, a: 0.16, f: 0.7, k: 0.5, fase: 2 }, { y: 0.22, a: 0.13, f: 1.15, k: 0.35, fase: 4 },
    { y: 0.36, a: 0.1, f: 0.55, k: 0.7, fase: 1 }, { y: 0.5, a: 0.08, f: 1.3, k: 0.25, fase: 5 },
  ];
  return {
    cambiar(w, h) { const d = op.ligero ? 9 : 5; W = Math.max(60, Math.round(w / d)); H = Math.max(90, Math.round(h / d)); canvas.width = W; canvas.height = H; },
    puntero() { /* sin ratón */ },
    dibujar(t) {
      ctx.clearRect(0, 0, W, H);
      aditivo(ctx, p);
      const I = op.intensidad, n = 40;
      for (const c of cintas) {
        const g = ctx.createLinearGradient(0, H * (c.y - 0.14), 0, H * (c.y + 0.34));
        g.addColorStop(0, col(p, 0, c.k)); g.addColorStop(0.4, col(p, c.a * I * 1.6, c.k)); g.addColorStop(1, col(p, 0, c.k));
        ctx.fillStyle = g; ctx.beginPath();
        for (let i = 0; i <= n; i++) {
          const x = (W * i) / n, y = H * c.y + H * 0.09 * Math.sin(i * 0.4 * c.f + t * 0.35 * c.f + c.fase) + H * 0.04 * Math.sin(i * 0.9 + t * 0.5);
          if (i) ctx.lineTo(x, y); else ctx.moveTo(x, y);
        }
        for (let i = n; i >= 0; i--) ctx.lineTo((W * i) / n, H * c.y + H * 0.27 + H * 0.08 * Math.sin(i * 0.35 + t * 0.3 * c.f + c.fase + 1));
        ctx.closePath(); ctx.fill();
        // rayos que cuelgan de la cinta, como las cortinas de una aurora de verdad
        for (let i = 0; i <= n; i += 2) {
          const x = (W * i) / n, y = H * c.y + H * 0.09 * Math.sin(i * 0.4 * c.f + t * 0.35 * c.f + c.fase) + H * 0.04 * Math.sin(i * 0.9 + t * 0.5);
          const gr = ctx.createLinearGradient(0, y, 0, y + H * 0.22);
          gr.addColorStop(0, col(p, c.a * I * 0.7 * (0.5 + 0.5 * Math.sin(i + t * 0.8)), c.k)); gr.addColorStop(1, col(p, 0, c.k));
          ctx.fillStyle = gr; ctx.fillRect(x - 0.7, y, 1.4, H * 0.22);
        }
      }
      normal(ctx);
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
      W = w; H = h; ajustar(canvas, ctx, w, h, op);
      const m = Math.min(w, h);
      luces = Array.from({ length: op.ligero ? 11 : 26 }, () => ({ x: Math.random(), y: Math.random(), r: m * (0.04 + Math.random() * 0.11), v: 0.004 + Math.random() * 0.01, f: Math.random() * 6.28, k: Math.random() * 0.7, a: 0.1 + Math.random() * 0.16 }));
    },
    puntero() { /* sin ratón */ },
    dibujar(t, dt) {
      ctx.clearRect(0, 0, W, H);
      aditivo(ctx, p);
      const I = op.intensidad;
      for (const l of luces) {
        l.y -= l.v * dt; if (l.y < -0.2) { l.y = 1.2; l.x = Math.random(); }
        const x = (l.x + 0.03 * Math.sin(t * 0.2 + l.f)) * W, y = l.y * H, a = l.a * I * (0.75 + 0.25 * Math.sin(t * 0.6 + l.f));
        halo(ctx, x, y, l.r, col(p, a * 0.5, l.k), col(p, 0, l.k));
        ctx.strokeStyle = col(p, a * 1.1, l.k + 0.1); ctx.lineWidth = 1.4; // el borde más brillante de una luz desenfocada de verdad
        ctx.beginPath(); ctx.arc(x, y, l.r * 0.86, 0, TAU); ctx.stroke();
        ctx.fillStyle = col(p, a * 0.9, 0.7); ctx.beginPath(); ctx.arc(x + l.r * 0.25, y - l.r * 0.3, l.r * 0.07, 0, TAU); ctx.fill();
      }
      normal(ctx);
    },
  };
}

// ---- Olas: capas de ondas que se mecen una sobre otra ----
function olas(canvas: HTMLCanvasElement, p: Paleta, op: Opciones): Escena {
  const ctx = canvas.getContext('2d')!;
  let W = 0, H = 0;
  const capas = [
    { b: 0.55, am: 0.03, fr: 0.006, v: 0.5, a: 0.08, k: 0.4 }, { b: 0.65, am: 0.035, fr: 0.008, v: -0.4, a: 0.1, k: 0.2 }, { b: 0.75, am: 0.03, fr: 0.01, v: 0.6, a: 0.12, k: 0.1 },
    { b: 0.85, am: 0.022, fr: 0.013, v: -0.7, a: 0.14, k: 0 }, { b: 0.94, am: 0.016, fr: 0.017, v: 0.9, a: 0.17, k: 0 },
  ];
  return {
    cambiar(w, h) { W = w; H = h; ajustar(canvas, ctx, w, h, op); },
    puntero() { /* sin ratón */ },
    dibujar(t) {
      ctx.clearRect(0, 0, W, H);
      const I = op.intensidad, paso = op.ligero ? 14 : 6;
      for (const c of capas) {
        const alto = (x: number) => H * c.b + H * c.am * Math.sin(x * c.fr + t * c.v) + H * c.am * 0.5 * Math.sin(x * c.fr * 2.1 + t * c.v * 1.4);
        const g = ctx.createLinearGradient(0, H * (c.b - c.am * 2), 0, H);
        g.addColorStop(0, col(p, c.a * I * 1.3, c.k)); g.addColorStop(1, col(p, c.a * I * 0.3, c.k));
        ctx.fillStyle = g; ctx.beginPath(); ctx.moveTo(0, H);
        for (let x = 0; x <= W + paso; x += paso) ctx.lineTo(x, alto(x));
        ctx.lineTo(W + paso, H); ctx.closePath(); ctx.fill();
        ctx.strokeStyle = col(p, c.a * I * 1.8, c.k + 0.3); ctx.lineWidth = 1.2; ctx.beginPath(); // la cresta, más clara
        for (let x = 0; x <= W + paso; x += paso) { if (x) ctx.lineTo(x, alto(x)); else ctx.moveTo(x, alto(x)); }
        ctx.stroke();
      }
    },
  };
}

// ---- Estrellas: un cielo de noche con tres capas de profundidad, una banda de luz y estrellas fugaces ----
function estrellas(canvas: HTMLCanvasElement, p: Paleta, op: Opciones): Escena {
  const ctx = canvas.getContext('2d')!;
  let W = 0, H = 0;
  let astros: { x: number; y: number; r: number; f: number; v: number; c: number; z: number }[] = [];
  let fugaces: { x: number; y: number; vx: number; vy: number; vida: number }[] = [], espera = 2;
  return {
    cambiar(w, h) {
      W = w; H = h; ajustar(canvas, ctx, w, h, op);
      astros = Array.from({ length: Math.max(30, Math.min(op.ligero ? 90 : 240, Math.round((w * h) / 6500))) }, () => { const z = Math.random(); return { x: Math.random() * w, y: Math.random() * h, r: 0.4 + z * 1.5, f: Math.random() * 6.28, v: 1 + z * 5, c: Math.random() < 0.3 ? 1 : 0, z }; });
    },
    puntero() { /* sin ratón */ },
    dibujar(t, dt) {
      ctx.clearRect(0, 0, W, H);
      const I = op.intensidad;
      aditivo(ctx, p);
      // una banda difusa de luz cruzando el cielo, como la Vía Láctea
      ctx.save(); ctx.translate(W * 0.5, H * 0.5); ctx.rotate(-0.5);
      const banda = ctx.createLinearGradient(0, -H * 0.3, 0, H * 0.3);
      banda.addColorStop(0, col(p, 0, 0.3)); banda.addColorStop(0.5, col(p, 0.1 * I, 0.3)); banda.addColorStop(1, col(p, 0, 0.3));
      ctx.fillStyle = banda; ctx.fillRect(-W, -H * 0.3, W * 2, H * 0.6); ctx.restore();
      for (const a of astros) {
        a.x += a.v * dt; if (a.x > W + 4) { a.x = -4; a.y = Math.random() * H; }
        const brillo = 0.5 + 0.5 * Math.sin(t * (1 + a.z) + a.f), alfa = (0.25 + 0.6 * brillo) * (0.35 + 0.65 * I) * (0.5 + 0.5 * a.z);
        const color = a.c ? col(p, alfa, 0.25) : p.oscuro ? `rgba(235,240,255,${(alfa * 0.95).toFixed(3)})` : `rgba(50,60,90,${(alfa * 0.55).toFixed(3)})`;
        ctx.fillStyle = color; ctx.beginPath(); ctx.arc(a.x, a.y, a.r, 0, TAU); ctx.fill();
        if (a.z > 0.82 && brillo > 0.7) { // las más cercanas destellan en cruz
          ctx.strokeStyle = color; ctx.lineWidth = 0.7; const l = a.r * 5 * brillo;
          ctx.beginPath(); ctx.moveTo(a.x - l, a.y); ctx.lineTo(a.x + l, a.y); ctx.moveTo(a.x, a.y - l); ctx.lineTo(a.x, a.y + l); ctx.stroke();
        }
      }
      espera -= dt;
      if (fugaces.length < 2 && espera <= 0) { fugaces.push({ x: Math.random() * W * 0.8, y: Math.random() * H * 0.4, vx: 420 + Math.random() * 240, vy: 180 + Math.random() * 120, vida: 1 }); espera = 3 + Math.random() * 6; }
      fugaces = fugaces.filter((f) => f.vida > 0);
      for (const f of fugaces) {
        f.x += f.vx * dt; f.y += f.vy * dt; f.vida -= dt * 1.3;
        const g = ctx.createLinearGradient(f.x, f.y, f.x - f.vx * 0.2, f.y - f.vy * 0.2);
        g.addColorStop(0, col(p, 0.9 * f.vida * I, 0.5)); g.addColorStop(1, col(p, 0, 0.5));
        ctx.strokeStyle = g; ctx.lineWidth = 1.8; ctx.beginPath(); ctx.moveTo(f.x, f.y); ctx.lineTo(f.x - f.vx * 0.2, f.y - f.vy * 0.2); ctx.stroke();
      }
      normal(ctx);
    },
  };
}

// ---- Rejilla: un suelo de líneas en perspectiva que avanza hacia ti, con su sol en el horizonte ----
function rejilla(canvas: HTMLCanvasElement, p: Paleta, op: Opciones): Escena {
  const ctx = canvas.getContext('2d')!;
  let W = 0, H = 0;
  return {
    cambiar(w, h) { W = w; H = h; ajustar(canvas, ctx, w, h, op); },
    puntero() { /* sin ratón */ },
    dibujar(t) {
      ctx.clearRect(0, 0, W, H);
      const I = op.intensidad, hor = H * 0.5, cx = W / 2, n = 18, columnas = op.ligero ? 10 : 18;
      aditivo(ctx, p);
      // el sol y el resplandor del horizonte
      halo(ctx, cx, hor, Math.min(W, H) * 0.5, col(p, 0.22 * I, 0.3), col(p, 0, 0.3));
      const r = Math.min(W, H) * 0.16;
      ctx.save(); ctx.beginPath(); ctx.rect(0, 0, W, hor); ctx.clip();
      const sol = ctx.createLinearGradient(0, hor - r * 2, 0, hor);
      sol.addColorStop(0, col(p, 0.5 * I, 0.55)); sol.addColorStop(1, col(p, 0.28 * I, 0.1));
      ctx.fillStyle = sol; ctx.beginPath(); ctx.arc(cx, hor, r, Math.PI, TAU); ctx.fill();
      ctx.restore();
      ctx.lineWidth = 1.2;
      for (let i = 0; i < n; i++) {
        const z = (i + ((t * 0.18) % 1)) / n, y = hor + (H - hor) * z * z;
        ctx.strokeStyle = col(p, 0.46 * I * z, 0.2); ctx.beginPath(); ctx.moveTo(0, y); ctx.lineTo(W, y); ctx.stroke();
      }
      for (let i = -columnas; i <= columnas; i++) {
        const g = ctx.createLinearGradient(0, hor, 0, H);
        g.addColorStop(0, col(p, 0, 0.2)); g.addColorStop(1, col(p, 0.38 * I, 0.2));
        ctx.strokeStyle = g; ctx.beginPath(); ctx.moveTo(cx + i * W * 0.012, hor); ctx.lineTo(cx + i * W * (op.ligero ? 0.22 : 0.14), H); ctx.stroke();
      }
      ctx.strokeStyle = col(p, 0.55 * I, 0.4); ctx.lineWidth = 1.6; ctx.beginPath(); ctx.moveTo(0, hor); ctx.lineTo(W, hor); ctx.stroke(); // la línea del horizonte
      normal(ctx);
    },
  };
}

// Los fondos nuevos y los inspirados en Marvel se descargan solo si se eligen
const NUEVOS = ['nebulosa', 'plasma', 'seda', 'lluvia', 'luciernagas', 'hexagonos', 'circuitos', 'caleidoscopio', 'sonar', 'flujo', 'cristales', 'nevada', 'cometas', 'haces'];
const MARVEL = ['multiverso', 'reactor', 'gamma', 'tormenta', 'mistico', 'vibranio', 'cosmico'];

export async function crearEscena(id: FondoId, canvas: HTMLCanvasElement, p: Paleta, op: Opciones): Promise<Escena | null> {
  if (NUEVOS.includes(id)) return (await import('./fondos-nuevos')).crearNuevo(id, canvas, p, op);
  if (MARVEL.includes(id)) return (await import('./fondos-marvel')).crearMarvel(id, canvas, p, op);
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
