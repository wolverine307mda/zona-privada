// Fondos con aire de superhéroes. Son dibujos originales hechos con código (telarañas, energía, rayos, runas, gemas…): no llevan personajes, logos ni
// imágenes con derechos. Cada uno tiene sus colores propios (no siguen el acento del tema), pero sí respetan «cuánto se ve», «velocidad» y «fuerza del color».
// Se descargan solo si se eligen.

import type { FondoId } from '../core/tema.service';
import { type Escena, type Opciones, type Paleta, TAU, aditivo, ajustar, alfaFuerza, azar, col, fija, fuerte, halo, lerp, normal, plasma, suave } from './fondos-base';

const SIN_RATON = () => { /* este fondo no sigue al ratón */ };
/** los fondos de superhéroes parten con más color que el resto: aquí lo que se busca es que se note */
const FUERZA_BASE = 1.9;

// ---- Multiverso: una telaraña gigante que se desdobla en rojo y azul, con trama de cómic y fallos de señal ----
function multiverso(canvas: HTMLCanvasElement, p: Paleta, op: Opciones): Escena {
  const ctx = canvas.getContext('2d')!;
  let W = 0, H = 0;
  const rojo = fija(p, 226, 54, 54), azul = fija(p, 30, 94, 255), cian = fija(p, 70, 225, 255);
  return {
    cambiar(w, h) { W = w; H = h; ajustar(canvas, ctx, w, h, op); },
    puntero: SIN_RATON,
    dibujar(t) {
      ctx.clearRect(0, 0, W, H);
      aditivo(ctx, p);
      const I = op.intensidad * FUERZA_BASE, R = Math.hypot(W, H) * 0.78, espinas = op.ligero ? 12 : 20, anillos = op.ligero ? 8 : 12;
      const telarana = (cx: number, cy: number, color: Paleta, a: number) => {
        const punto = (i: number, rr: number) => { const ang = (i * TAU) / espinas + Math.sin(t * 0.2 + i * 0.7) * 0.014; return [cx + Math.cos(ang) * rr, cy + Math.sin(ang) * rr]; };
        ctx.strokeStyle = col(color, a * I); ctx.lineWidth = 1.7; ctx.beginPath();
        for (let i = 0; i < espinas; i++) { const [x, y] = punto(i, R); ctx.moveTo(cx, cy); ctx.lineTo(x, y); }
        for (let k = 1; k <= anillos; k++) {
          const rr = Math.pow(k / anillos, 1.4) * R, hundir = 0.9 - 0.02 * Math.sin(t * 0.7 + k);
          for (let i = 0; i < espinas; i++) {
            const [x0, y0] = punto(i, rr), [x1, y1] = punto(i + 1, rr), mx = (x0 + x1) / 2, my = (y0 + y1) / 2;
            ctx.moveTo(x0, y0); ctx.quadraticCurveTo(cx + (mx - cx) * hundir, cy + (my - cy) * hundir, x1, y1); // cada hilo se curva hacia el centro
          }
        }
        ctx.stroke();
      };
      const cx = W * 0.64, cy = H * 0.36, s = 4.5 + Math.sin(t * 0.9) * 1.5;
      const m = Math.max(W, H);
      halo(ctx, W * 0.08, H * 0.1, m * 0.6, col(azul, 0.24 * I), col(azul, 0)); halo(ctx, W * 0.95, H * 0.92, m * 0.6, col(rojo, 0.24 * I), col(rojo, 0)); // dos luces de color, azul arriba y rojo abajo
      telarana(cx - s, cy, azul, 0.5); telarana(cx + s, cy, rojo, 0.55); telarana(cx, cy, cian, 0.1); // copias desplazadas: efecto de imagen partida
      const fallo = (t % 3.4) < 0.16; // un fallo de señal: una franja que se desplaza
      if (fallo) { const y0 = H * (0.2 + 0.5 * ((t * 7.3) % 1)); ctx.save(); ctx.beginPath(); ctx.rect(0, y0, W, 26 + 40 * ((t * 3) % 1)); ctx.clip(); telarana(cx + 26, cy, rojo, 0.5); telarana(cx - 20, cy, cian, 0.5); ctx.restore(); }
      halo(ctx, cx, cy, R * 0.12, col(rojo, 0.3 * I), col(rojo, 0));
      // trama de puntos de imprenta, en la esquina de abajo a la izquierda
      const paso = op.ligero ? 26 : 18;
      ctx.fillStyle = col(rojo, 0.3 * I);
      for (let y = H * 0.45; y < H + paso; y += paso) for (let x = -paso; x < W * 0.6; x += paso) {
        const fila = Math.round(y / paso), cerca = 1 - Math.hypot((x - 0) / (W * 0.6), (y - H) / (H * 0.6));
        const r = paso * 0.42 * Math.max(0, cerca) * (0.75 + 0.25 * Math.sin(t * 0.8 + x * 0.02 + y * 0.015));
        if (r < 0.7) continue;
        ctx.beginPath(); ctx.arc(x + (fila % 2 ? paso / 2 : 0), y, r, 0, TAU); ctx.fill();
      }
      normal(ctx);
    },
  };
}

// ---- Reactor: anillos de energía y mira de pantalla de combate alrededor de un núcleo que late ----
function reactor(canvas: HTMLCanvasElement, p: Paleta, op: Opciones): Escena {
  const ctx = canvas.getContext('2d')!;
  let W = 0, H = 0;
  const cian = fija(p, 70, 225, 255), oro = fija(p, 255, 196, 64);
  const anillos = [
    { r: 0.98, w: 1.2, d: [2, 9], v: 0.1, c: 0 }, { r: 0.88, w: 7, d: [70, 36], v: -0.16, c: 0 }, { r: 0.76, w: 1.6, d: [5, 5], v: 0.28, c: 1 },
    { r: 0.64, w: 11, d: [100, 44, 14, 44], v: -0.11, c: 0 }, { r: 0.5, w: 2, d: [], v: 0, c: 0 }, { r: 0.43, w: 5, d: [24, 24], v: 0.22, c: 1 },
  ];
  return {
    cambiar(w, h) { W = w; H = h; ajustar(canvas, ctx, w, h, op); },
    puntero: SIN_RATON,
    dibujar(t) {
      ctx.clearRect(0, 0, W, H);
      aditivo(ctx, p);
      const I = op.intensidad * FUERZA_BASE, cx = W * 0.62, cy = H * 0.5, R = Math.min(W, H) * 0.46, latido = 0.85 + 0.15 * Math.sin(t * 2.2);
      ctx.shadowColor = col(cian, 0.95); ctx.shadowBlur = op.ligero ? 0 : 14;
      for (const a of anillos) {
        ctx.strokeStyle = col(a.c ? oro : cian, (a.w > 4 ? 0.5 : 0.7) * I, 0.18); ctx.lineWidth = a.w * 1.1; ctx.setLineDash(a.d.map((x) => x)); ctx.lineDashOffset = -t * a.v * 160;
        ctx.beginPath(); ctx.arc(cx, cy, a.r * R, 0, TAU); ctx.stroke();
      }
      ctx.setLineDash([]);
      // las marcas de una regla alrededor
      ctx.strokeStyle = col(cian, 0.7 * I, 0.2); ctx.lineWidth = 1.3; ctx.beginPath();
      for (let i = 0; i < 90; i++) { const ang = (i * TAU) / 90 + t * 0.03, l = i % 6 === 0 ? 14 : 6, r0 = R * 1.03; ctx.moveTo(cx + Math.cos(ang) * r0, cy + Math.sin(ang) * r0); ctx.lineTo(cx + Math.cos(ang) * (r0 + l), cy + Math.sin(ang) * (r0 + l)); }
      ctx.stroke();
      // el núcleo: diez bobinas y una luz que late
      for (let i = 0; i < 10; i++) {
        const a0 = (i * TAU) / 10 + t * 0.12, a1 = a0 + TAU / 10 - 0.16;
        ctx.strokeStyle = col(cian, (0.5 + 0.25 * Math.sin(t * 1.6 + i)) * I, 0.25); ctx.lineWidth = 14; ctx.beginPath(); ctx.arc(cx, cy, R * 0.3, a0, a1); ctx.stroke();
      }
      ctx.shadowBlur = 0;
      halo(ctx, cx, cy, R * 0.42 * latido, col(cian, 0.7 * I, 0.5), col(cian, 0));
      halo(ctx, cx, cy, R * 0.12, col(cian, 0.9 * I, 0.85), col(cian, 0));
      // el barrido de un radar
      const ang = t * 0.9, g = ctx.createLinearGradient(cx, cy, cx + Math.cos(ang) * R, cy + Math.sin(ang) * R);
      g.addColorStop(0, col(cian, 0.5 * I)); g.addColorStop(1, col(cian, 0));
      ctx.strokeStyle = g; ctx.lineWidth = 2; ctx.beginPath(); ctx.moveTo(cx, cy); ctx.lineTo(cx + Math.cos(ang) * R, cy + Math.sin(ang) * R); ctx.stroke();
      // esquinas de la mira
      ctx.strokeStyle = col(oro, 0.45 * I); ctx.lineWidth = 1.6; const m = 26, l = 40;
      for (const [x, y, sx, sy] of [[m, m, 1, 1], [W - m, m, -1, 1], [m, H - m, 1, -1], [W - m, H - m, -1, -1]] as const) { ctx.beginPath(); ctx.moveTo(x, y + sy * l); ctx.lineTo(x, y); ctx.lineTo(x + sx * l, y); ctx.stroke(); }
      normal(ctx);
    },
  };
}

// ---- Gamma: energía verde tóxica con venas violeta y burbujas que suben ----
function gamma(canvas: HTMLCanvasElement, p: Paleta, op: Opciones): Escena {
  const verde = fija(p, 40, 210, 80), lima = fija(p, 200, 255, 90), violeta = fija(p, 140, 70, 220);
  const burbujas = Array.from({ length: 7 }, (_, i) => ({ x: 0.1 + 0.8 * ((i * 0.37) % 1), v: 0.03 + 0.02 * (i % 3), r: 0.12 + 0.07 * (i % 4), f: i * 1.7 }));
  const vv = fuerte(verde, verde.r, verde.g, verde.b), ll = fuerte(lima, lima.r, lima.g, lima.b), vi = fuerte(violeta, violeta.r, violeta.g, violeta.b);
  return plasma(canvas, op, 7,
    (v, _b, x, y) => {
      const u = suave(0.2, 0.95, v), vena = 1 - suave(0, 0.045, Math.abs(v - 0.24)), claro = Math.pow(suave(0.6, 1, v), 2);
      const r = lerp(lerp(vv[0], ll[0], claro), vi[0], vena * 0.85), g = lerp(lerp(vv[1], ll[1], claro), vi[1], vena * 0.85), b = lerp(lerp(vv[2], ll[2], claro), vi[2], vena * 0.85);
      return [r, g, b, Math.min(1, (0.05 + 0.5 * u + vena * 0.22) * op.intensidad * FUERZA_BASE * alfaFuerza(p) * (0.7 + 0.3 * y)) + 0 * x];
    },
    (x, y, t) => {
      let v = 0.5 + 0.5 * (Math.sin(x * 2 + t * 0.5) + Math.sin(y * 2.4 - t * 0.35 + Math.sin(x * 1.3 + t * 0.2) * 1.4) + Math.sin((x * 1.1 + y) * 1.4 + t * 0.3)) / 3;
      for (const b of burbujas) { // cada burbuja suma luz a su alrededor
        const by = 3.2 - (((t * b.v * 4 + b.f) % 1.4) * 3.2), bx = b.x * 5.2, d = Math.hypot(x - bx, y - by) / (b.r * 3);
        if (d < 1) v += (1 - d) * (1 - d) * 0.45;
      }
      return Math.min(1, v);
    });
}

// ---- Tormenta: nubes de trueno, lluvia y rayos que se ramifican y iluminan el cielo ----
interface Rayo { pts: [number, number][]; ramas: [number, number][][]; vida: number; x: number }
function tormenta(canvas: HTMLCanvasElement, p: Paleta, op: Opciones): Escena {
  const ctx = canvas.getContext('2d')!;
  let W = 0, H = 0, rayos: Rayo[] = [], espera = 1.2, gotas: { x: number; y: number; v: number; l: number }[] = [];
  const electrico = fija(p, 110, 170, 255), blanco = fija(p, 225, 238, 255), pizarra = fija(p, 80, 110, 190);
  const desplazar = (x0: number, y0: number, x1: number, y1: number, d: number, out: [number, number][]) => {
    if (d < 5) { out.push([x1, y1]); return; }
    const mx = (x0 + x1) / 2 + azar(-d, d), my = (y0 + y1) / 2 + azar(-d, d) * 0.4;
    desplazar(x0, y0, mx, my, d / 2, out); desplazar(mx, my, x1, y1, d / 2, out);
  };
  const crear = (): Rayo => {
    const x0 = azar(W * 0.34, W * 0.94), y1 = azar(H * 0.45, H * 0.9), x1 = x0 + azar(-W * 0.14, W * 0.14), pts: [number, number][] = [[x0, -10]];
    desplazar(x0, -10, x1, y1, H * 0.09, pts);
    const ramas = Array.from({ length: 3 }, () => { const o = pts[Math.floor(azar(pts.length * 0.2, pts.length * 0.8))], r: [number, number][] = [[o[0], o[1]]]; desplazar(o[0], o[1], o[0] + azar(-W * 0.14, W * 0.14), o[1] + azar(H * 0.1, H * 0.3), H * 0.05, r); return r; });
    return { pts, ramas, vida: 1, x: x0 };
  };
  const trazo = (pts: [number, number][], ancho: number, color: string) => { ctx.strokeStyle = color; ctx.lineWidth = ancho; ctx.beginPath(); pts.forEach((q, i) => (i ? ctx.lineTo(q[0], q[1]) : ctx.moveTo(q[0], q[1]))); ctx.stroke(); };
  return {
    cambiar(w, h) { W = w; H = h; ajustar(canvas, ctx, w, h, op); gotas = Array.from({ length: op.ligero ? 45 : 110 }, () => ({ x: Math.random() * w, y: Math.random() * h, v: azar(600, 1000), l: azar(10, 24) })); rayos = []; },
    puntero: SIN_RATON,
    dibujar(t, dt) {
      ctx.clearRect(0, 0, W, H);
      const I = op.intensidad * FUERZA_BASE, m = Math.max(W, H);
      aditivo(ctx, p);
      for (let i = 0; i < 9; i++) halo(ctx, (0.5 + 0.55 * Math.sin(t * 0.04 * (1 + i * 0.3) + i * 1.9)) * W, H * (0.08 + 0.1 * Math.cos(t * 0.05 + i * 2.3)), m * 0.34, col(pizarra, 0.34 * I), col(pizarra, 0)); // las nubes
      ctx.strokeStyle = col(blanco, 0.3 * I); ctx.lineWidth = 1.2; ctx.beginPath();
      for (const g of gotas) { g.y += g.v * dt; g.x -= g.v * dt * 0.18; if (g.y > H) { g.y = -20; g.x = Math.random() * W * 1.2; } ctx.moveTo(g.x, g.y); ctx.lineTo(g.x + g.l * 0.18, g.y - g.l); }
      ctx.stroke();
      espera -= dt;
      if (espera <= 0 && rayos.length < 3) { rayos.push(crear()); espera = azar(1.4, 3.8); }
      rayos = rayos.filter((r) => r.vida > 0);
      for (const r of rayos) {
        r.vida -= dt * 1.15;
        const a = Math.max(0, r.vida), parpadeo = a > 0.55 || Math.sin(r.vida * 60) > 0 ? 1 : 0.4;
        halo(ctx, r.x, H * 0.15, m * 0.6, col(electrico, 0.13 * a * I * parpadeo), col(electrico, 0)); // el cielo se ilumina
        for (const pts of [r.pts, ...r.ramas]) { trazo(pts, 12, col(electrico, 0.16 * a * I)); trazo(pts, 4.2, col(electrico, 0.5 * a * I * parpadeo, 0.3)); trazo(pts, 1.8, col(blanco, 0.98 * a * I * parpadeo)); }
      }
      normal(ctx);
    },
  };
}

// ---- Místico: un portal de anillos con runas y chispas que saltan, como un hechizo ----
function mistico(canvas: HTMLCanvasElement, p: Paleta, op: Opciones): Escena {
  const ctx = canvas.getContext('2d')!;
  let W = 0, H = 0, chispas: { a: number; r: number; v: number; vr: number; vida: number; max: number }[] = [];
  const naranja = fija(p, 255, 140, 40), oro = fija(p, 255, 205, 90), rojo = fija(p, 235, 80, 30);
  const runa = (i: number, r: number) => { // seis dibujos sencillos que se repiten por el anillo
    ctx.beginPath();
    switch (i % 6) {
      case 0: ctx.arc(0, -r, 6, 0, Math.PI); ctx.moveTo(0, -r + 6); ctx.lineTo(0, -r + 16); break;
      case 1: ctx.moveTo(-6, -r + 8); ctx.lineTo(0, -r - 6); ctx.lineTo(6, -r + 8); ctx.closePath(); break;
      case 2: ctx.moveTo(-5, -r); ctx.lineTo(5, -r); ctx.moveTo(0, -r - 7); ctx.lineTo(0, -r + 9); break;
      case 3: ctx.moveTo(0, -r - 7); ctx.lineTo(6, -r); ctx.lineTo(0, -r + 7); ctx.lineTo(-6, -r); ctx.closePath(); break;
      case 4: ctx.arc(0, -r, 5, 0, TAU); ctx.moveTo(-8, -r + 12); ctx.lineTo(8, -r + 12); break;
      default: ctx.moveTo(-6, -r - 6); ctx.lineTo(6, -r + 6); ctx.moveTo(6, -r - 6); ctx.lineTo(-6, -r + 6);
    }
    ctx.stroke();
  };
  const estrella = (r: number, n: number, salto: number) => { ctx.beginPath(); for (let i = 0; i <= n; i++) { const a = ((i * salto) % n) * (TAU / n) - Math.PI / 2; const x = Math.cos(a) * r, y = Math.sin(a) * r; if (i) ctx.lineTo(x, y); else ctx.moveTo(x, y); } ctx.closePath(); ctx.stroke(); };
  return {
    cambiar(w, h) { W = w; H = h; ajustar(canvas, ctx, w, h, op); chispas = []; },
    puntero: SIN_RATON,
    dibujar(t, dt) {
      ctx.clearRect(0, 0, W, H);
      aditivo(ctx, p);
      const I = op.intensidad * FUERZA_BASE, cx = W * 0.62, cy = H * 0.5, R = Math.min(W, H) * 0.46;
      halo(ctx, cx, cy, R * 1.2, col(naranja, 0.3 * I), col(naranja, 0));
      ctx.lineCap = 'round';
      ctx.shadowColor = col(naranja, 0.95); ctx.shadowBlur = op.ligero ? 0 : 16; // el brillo de los trazos
      // anillo exterior con runas
      ctx.save(); ctx.translate(cx, cy); ctx.rotate(t * 0.12);
      ctx.strokeStyle = col(oro, 0.8 * I, 0.1); ctx.lineWidth = 3.4; ctx.beginPath(); ctx.arc(0, 0, R, 0, TAU); ctx.stroke();
      ctx.lineWidth = 1.8; ctx.beginPath(); ctx.arc(0, 0, R * 0.9, 0, TAU); ctx.stroke();
      ctx.strokeStyle = col(naranja, 0.85 * I, 0.1); ctx.lineWidth = 2.6;
      for (let i = 0; i < 12; i++) { ctx.save(); ctx.rotate((i * TAU) / 12); runa(i, R * 0.95); ctx.restore(); }
      ctx.restore();
      // el anillo del medio gira al revés con una estrella de ocho puntas
      ctx.save(); ctx.translate(cx, cy); ctx.rotate(-t * 0.2);
      ctx.strokeStyle = col(naranja, 0.8 * I, 0.1); ctx.lineWidth = 2.6; ctx.beginPath(); ctx.arc(0, 0, R * 0.74, 0, TAU); ctx.stroke(); estrella(R * 0.74, 8, 3);
      ctx.strokeStyle = col(rojo, 0.75 * I, 0.1); estrella(R * 0.52, 6, 2); ctx.restore();
      ctx.save(); ctx.translate(cx, cy); ctx.rotate(t * 0.3); ctx.strokeStyle = col(oro, 0.8 * I, 0.1); ctx.lineWidth = 2.2; ctx.setLineDash([8, 10]); ctx.beginPath(); ctx.arc(0, 0, R * 0.38, 0, TAU); ctx.stroke(); ctx.setLineDash([]); ctx.restore();
      ctx.shadowBlur = 0;
      // chispas que saltan del anillo exterior
      for (let i = 0; i < 2; i++) if (chispas.length < (op.ligero ? 60 : 160)) chispas.push({ a: azar(0, TAU), r: R, v: azar(0.5, 1.6), vr: azar(-10, 60), vida: 0, max: azar(1.2, 3) });
      chispas = chispas.filter((c) => c.vida < c.max);
      for (const c of chispas) {
        c.vida += dt; c.a += c.v * dt * 0.8; c.r += c.vr * dt;
        const f = 1 - c.vida / c.max, x = cx + Math.cos(c.a) * c.r, y = cy + Math.sin(c.a) * c.r;
        ctx.strokeStyle = col(oro, 0.8 * f * I, 0.2); ctx.lineWidth = 1.6; ctx.beginPath(); ctx.moveTo(x, y); ctx.lineTo(cx + Math.cos(c.a - 0.07) * (c.r - c.vr * 0.05), cy + Math.sin(c.a - 0.07) * (c.r - c.vr * 0.05)); ctx.stroke();
        halo(ctx, x, y, 7, col(naranja, 0.5 * f * I), col(naranja, 0));
      }
      normal(ctx);
    },
  };
}

// ---- Vibranio: un panal oscuro de metal con energía violeta que corre por sus aristas ----
function vibranio(canvas: HTMLCanvasElement, p: Paleta, op: Opciones): Escena {
  const ctx = canvas.getContext('2d')!;
  let W = 0, H = 0, motas: { x: number; y: number; v: number; f: number }[] = [];
  const violeta = fija(p, 150, 80, 255), magenta = fija(p, 230, 70, 200), oro = fija(p, 255, 215, 120);
  return {
    cambiar(w, h) { W = w; H = h; ajustar(canvas, ctx, w, h, op); motas = Array.from({ length: op.ligero ? 22 : 60 }, () => ({ x: Math.random() * w, y: Math.random() * h, v: azar(8, 30), f: Math.random() * 6.28 })); },
    puntero: SIN_RATON,
    dibujar(t, dt) {
      ctx.clearRect(0, 0, W, H);
      aditivo(ctx, p);
      const I = op.intensidad * FUERZA_BASE, r = op.ligero ? 48 : 34, dx = r * 1.5, dy = Math.sqrt(3) * r, m = Math.max(W, H);
      halo(ctx, W * (0.5 + 0.3 * Math.sin(t * 0.07)), H * (0.5 + 0.25 * Math.cos(t * 0.09)), m * 0.5, col(violeta, 0.16 * I), col(violeta, 0));
      const c1 = [W * (0.5 + 0.4 * Math.sin(t * 0.12)), H * (0.5 + 0.35 * Math.cos(t * 0.1))], c2 = [W * (0.5 + 0.4 * Math.cos(t * 0.08 + 2)), H * (0.5 + 0.3 * Math.sin(t * 0.13 + 1))];
      ctx.lineWidth = 1.1;
      for (let i = -1; i * dx < W + dx; i++) for (let j = -1; j * dy < H + dy; j++) {
        const cx = i * dx, cy = j * dy + (i % 2 ? dy / 2 : 0);
        const luz = Math.min(1, Math.pow(Math.max(0, Math.sin(Math.hypot(cx - c1[0], cy - c1[1]) * 0.012 - t * 1.5)), 8) + Math.pow(Math.max(0, Math.sin(Math.hypot(cx - c2[0], cy - c2[1]) * 0.01 - t * 1.1 + 1)), 8));
        ctx.strokeStyle = col(luz > 0.35 ? magenta : violeta, (0.08 + 0.7 * luz) * I, luz * 0.3); ctx.beginPath();
        for (let k = 0; k <= 6; k++) { const a = (k * Math.PI) / 3, x = cx + (r - 1.5) * Math.cos(a), y = cy + (r - 1.5) * Math.sin(a); if (k) ctx.lineTo(x, y); else ctx.moveTo(x, y); }
        ctx.stroke();
        if (luz > 0.5) { halo(ctx, cx, cy, r * 0.7, col(violeta, 0.22 * luz * I), col(violeta, 0)); ctx.fillStyle = col(oro, 0.8 * luz * I); ctx.beginPath(); ctx.arc(cx + r - 1.5, cy, 2.2, 0, TAU); ctx.fill(); }
      }
      for (const q of motas) { q.y -= q.v * dt; if (q.y < -6) { q.y = H + 6; q.x = Math.random() * W; } ctx.fillStyle = col(oro, (0.25 + 0.4 * Math.sin(t + q.f) ** 2) * I); ctx.beginPath(); ctx.arc(q.x + Math.sin(t * 0.3 + q.f) * 8, q.y, 1.5, 0, TAU); ctx.fill(); }
      normal(ctx);
    },
  };
}

// ---- Cósmico: una nebulosa con seis gemas de colores que orbitan dejando su estela ----
function cosmico(canvas: HTMLCanvasElement, p: Paleta, op: Opciones): Escena {
  const ctx = canvas.getContext('2d')!;
  let W = 0, H = 0, estrellas: { x: number; y: number; r: number; f: number }[] = [];
  const gemas = ([[138, 43, 226], [59, 130, 246], [239, 68, 68], [250, 204, 21], [34, 197, 94], [249, 115, 22]] as const).map((c, i) => ({ pal: fija(p, c[0], c[1], c[2]), rx: 0.2 + 0.045 * i, ry: 0.16 + 0.035 * i, v: 0.13 + 0.035 * ((i * 5) % 6), fase: i * 1.05, giro: i % 2 ? -1 : 1 }));
  const nubes = [[138, 43, 226], [59, 130, 246], [200, 60, 180]].map((c, i) => ({ pal: fija(p, c[0], c[1], c[2]), fx: 0.04 + 0.02 * i, fy: 0.05 + 0.015 * i, ph: i * 2.2, r: 0.5 + 0.1 * i }));
  return {
    cambiar(w, h) { W = w; H = h; ajustar(canvas, ctx, w, h, op); estrellas = Array.from({ length: op.ligero ? 70 : 190 }, () => ({ x: Math.random(), y: Math.random(), r: 0.4 + Math.random() * 1.2, f: Math.random() * 6.28 })); },
    puntero: SIN_RATON,
    dibujar(t) {
      ctx.clearRect(0, 0, W, H);
      aditivo(ctx, p);
      const I = op.intensidad * FUERZA_BASE, m = Math.max(W, H), cx = W * 0.58, cy = H * 0.5;
      for (const n of nubes) halo(ctx, (0.5 + 0.35 * Math.sin(t * n.fx + n.ph)) * W, (0.5 + 0.3 * Math.cos(t * n.fy + n.ph)) * H, n.r * m, col(n.pal, 0.26 * I), col(n.pal, 0));
      for (const s of estrellas) { ctx.fillStyle = p.oscuro ? `rgba(235,240,255,${((0.2 + 0.5 * Math.sin(t * 1.2 + s.f) ** 2) * (0.35 + 0.65 * I)).toFixed(3)})` : col(p, 0.3 * I); ctx.beginPath(); ctx.arc(s.x * W, s.y * H, s.r, 0, TAU); ctx.fill(); }
      for (const g of gemas) {
        const pos = (tt: number) => [cx + Math.cos(tt * g.v * g.giro + g.fase) * g.rx * W, cy + Math.sin(tt * g.v * g.giro + g.fase) * g.ry * H];
        ctx.lineCap = 'round';
        for (let k = 0; k < 22; k++) { // la estela son tramos que se apagan hacia atrás
          const a = pos(t - k * 0.35), b = pos(t - (k + 1) * 0.35), f = 1 - k / 22;
          ctx.strokeStyle = col(g.pal, 0.8 * f * I, 0.1); ctx.lineWidth = (6.5 * f + 0.6) * Math.min(1, Math.min(W, H) / 400); ctx.beginPath(); ctx.moveTo(a[0], a[1]); ctx.lineTo(b[0], b[1]); ctx.stroke();
        }
        const [x, y] = pos(t);
        halo(ctx, x, y, Math.min(64, Math.min(W, H) * 0.12), col(g.pal, 0.7 * I, 0.2), col(g.pal, 0));
        ctx.save(); ctx.translate(x, y); ctx.rotate(t * 0.8 + g.fase); ctx.fillStyle = col(g.pal, 0.95 * I, 0.55); ctx.beginPath(); const k = Math.min(1, Math.min(W, H) / 400); ctx.moveTo(0, -12 * k); ctx.lineTo(8 * k, 0); ctx.lineTo(0, 12 * k); ctx.lineTo(-8 * k, 0); ctx.closePath(); ctx.fill(); ctx.restore();
      }
      normal(ctx);
    },
  };
}

export function crearMarvel(id: FondoId, canvas: HTMLCanvasElement, p: Paleta, op: Opciones): Escena | null {
  switch (id) {
    case 'multiverso': return multiverso(canvas, p, op);
    case 'reactor': return reactor(canvas, p, op);
    case 'gamma': return gamma(canvas, p, op);
    case 'tormenta': return tormenta(canvas, p, op);
    case 'mistico': return mistico(canvas, p, op);
    case 'vibranio': return vibranio(canvas, p, op);
    case 'cosmico': return cosmico(canvas, p, op);
    default: return null;
  }
}
