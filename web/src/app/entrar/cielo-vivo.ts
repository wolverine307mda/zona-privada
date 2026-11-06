import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, afterNextRender, effect, inject, input, viewChild } from '@angular/core';
import { FaseLuna, faseLuna, posicionLuna, posicionSol } from '../core/astro';
import { CieloConfig } from '../core/cielo-config';

// El cielo de verdad, dibujado en un lienzo 2D que llena la pantalla: el Sol y la Luna están donde están ahora mismo desde tu sitio (este a la izquierda, oeste a la derecha,
// el sur en el centro), con la fase real de la Luna, estrellas que aparecen al oscurecer, el color del cielo según la altura del Sol, un par de montes y alguna estrella fugaz.
// `fecha` deja ver otro momento (la vista previa de Ajustes usa un deslizador); `destello()` da un pulso de color para decir «bien» o «mal».
const SEMILLA = 7;
function aleatorio(semilla: number): () => number { let s = semilla; return () => { s = (s * 16807) % 2147483647; return (s - 1) / 2147483646; }; }
const mezcla = (a: number, b: number, t: number) => a + (b - a) * t;
const limitar = (x: number, a = 0, b = 1) => Math.min(b, Math.max(a, x));

type Color = [number, number, number];
// colores de lo alto y del horizonte del cielo según la altura del Sol (grados); entre punto y punto se mezclan
const PASOS: { alt: number; arriba: Color; abajo: Color }[] = [
  { alt: -18, arriba: [4, 6, 16], abajo: [10, 16, 38] },
  { alt: -9, arriba: [10, 16, 44], abajo: [58, 40, 92] },
  { alt: -2, arriba: [38, 52, 110], abajo: [236, 122, 84] },
  { alt: 6, arriba: [64, 118, 196], abajo: [250, 190, 140] },
  { alt: 25, arriba: [40, 120, 214], abajo: [150, 200, 244] },
  { alt: 60, arriba: [26, 100, 206], abajo: [132, 190, 240] },
];

function colorCielo(altGrados: number): { arriba: Color; abajo: Color } {
  if (altGrados <= PASOS[0].alt) return { arriba: PASOS[0].arriba, abajo: PASOS[0].abajo };
  for (let i = 1; i < PASOS.length; i++) {
    if (altGrados <= PASOS[i].alt) {
      const a = PASOS[i - 1], b = PASOS[i], t = (altGrados - a.alt) / (b.alt - a.alt);
      return { arriba: a.arriba.map((v, k) => mezcla(v, b.arriba[k], t)) as Color, abajo: a.abajo.map((v, k) => mezcla(v, b.abajo[k], t)) as Color };
    }
  }
  const u = PASOS[PASOS.length - 1];
  return { arriba: u.arriba, abajo: u.abajo };
}
const rgb = (c: Color, a = 1) => `rgba(${c[0] | 0},${c[1] | 0},${c[2] | 0},${a})`;

@Component({
  selector: 'app-cielo-vivo',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<canvas #lienzo role="img" [attr.aria-label]="'El cielo de ' + config.lugar().nombre + ' ahora mismo'"></canvas>`,
  styles: `
    :host { position: absolute; inset: 0; display: block; overflow: hidden; z-index: 0; }
    canvas { width: 100%; height: 100%; display: block; }
  `,
})
export class CieloVivo {
  protected readonly config = inject(CieloConfig);
  readonly fecha = input<Date | null>(null);
  private readonly lienzo = viewChild.required<ElementRef<HTMLCanvasElement>>('lienzo');
  private ctx: CanvasRenderingContext2D | null = null;
  private ancho = 0; private alto = 0;
  private marco = 0; private ultimo = 0; private visible = true;
  private readonly reducido = matchMedia('(prefers-reduced-motion: reduce)').matches;
  private readonly estrellas: { x: number; y: number; r: number; fase: number; vel: number }[] = [];
  private readonly nubes: { x: number; y: number; w: number; v: number }[] = [];
  private fugaz: { x: number; y: number; vx: number; vy: number; vida: number } | null = null;
  private proximaFugaz = 8;
  private pulso: { color: string; t: number } | null = null;
  private fase: FaseLuna = { fraccion: 0, fase: 0 };
  private tFase = 0;

  constructor() {
    const aleat = aleatorio(SEMILLA);
    for (let i = 0; i < 240; i++) this.estrellas.push({ x: aleat(), y: aleat() * 0.8, r: 0.4 + aleat() * 1.3, fase: aleat() * 6.28, vel: 0.6 + aleat() * 1.8 });
    for (let i = 0; i < 6; i++) this.nubes.push({ x: aleat(), y: 0.1 + aleat() * 0.35, w: 0.18 + aleat() * 0.22, v: 0.002 + aleat() * 0.004 });
    const destruir = inject(DestroyRef);
    afterNextRender(() => {
      const c = this.lienzo().nativeElement;
      this.ctx = c.getContext('2d');
      const ro = new ResizeObserver(() => this.ajustar());
      ro.observe(c);
      const io = new IntersectionObserver((e) => { this.visible = e[0]?.isIntersecting ?? true; });
      io.observe(c);
      this.ajustar();
      if (this.reducido) { this.pintar(0); } else this.marco = requestAnimationFrame((t) => this.bucle(t));
      // en reposo se repinta cada minuto: el cielo avanza aunque no se mueva nada
      const minuto = setInterval(() => { if (this.reducido) this.pintar(0); }, 60_000);
      destruir.onDestroy(() => { cancelAnimationFrame(this.marco); ro.disconnect(); io.disconnect(); clearInterval(minuto); });
    });
    effect(() => { this.config.lugar(); this.fecha(); if (this.ctx && this.reducido) this.pintar(0); });
  }

  /** Un pulso de color sobre el cielo: verde al entrar, rojo al fallar. */
  destello(color: 'ok' | 'mal'): void { this.pulso = { color: color === 'ok' ? '61,220,132' : '255,68,56', t: 1 }; if (this.reducido) this.pintar(0); }

  private ajustar(): void {
    const c = this.lienzo().nativeElement;
    const dpr = Math.min(devicePixelRatio || 1, 2);
    const w = c.clientWidth, h = c.clientHeight;
    if (!w || !h) return;
    c.width = Math.round(w * dpr); c.height = Math.round(h * dpr);
    this.ctx?.setTransform(dpr, 0, 0, dpr, 0, 0);
    this.ancho = w; this.alto = h;
    if (this.reducido) this.pintar(0);
  }

  // 30 cuadros por segundo bastan para un fondo y ahorran batería
  private bucle(t: number): void {
    this.marco = requestAnimationFrame((x) => this.bucle(x));
    if (t - this.ultimo < 33) return;
    const dt = Math.min(0.1, (t - this.ultimo) / 1000);
    this.ultimo = t;
    if (this.visible && !document.hidden) { try { this.pintar(dt); } catch { /* ese cuadro se salta */ } }
  }

  private pintar(dt: number): void {
    const ctx = this.ctx; if (!ctx || !this.ancho) return;
    const w = this.ancho, h = this.alto, ahora = this.fecha() ?? new Date();
    const { lat, lon } = this.config.lugar();
    const sol = posicionSol(ahora, lat, lon), luna = posicionLuna(ahora, lat, lon);
    const altSol = (sol.altura * 180) / Math.PI;
    if (performance.now() - this.tFase > 30_000 || this.fecha()) { this.fase = faseLuna(ahora); this.tFase = performance.now(); }
    const horizonte = h * 0.8, cima = h * 0.06;
    const xDe = (az: number) => w * (0.5 + limitar(az / Math.PI, -0.62, 0.62));
    const yDe = (alt: number) => horizonte - (limitar(alt, -0.4, Math.PI / 2) / (Math.PI / 2)) * (horizonte - cima);

    // cielo
    const { arriba, abajo } = colorCielo(altSol);
    const g = ctx.createLinearGradient(0, 0, 0, horizonte);
    g.addColorStop(0, rgb(arriba)); g.addColorStop(1, rgb(abajo));
    ctx.fillStyle = g; ctx.fillRect(0, 0, w, h);

    // estrellas: aparecen cuando el Sol baja de unos −3° y brillan del todo hacia los −15°
    const oscuridad = limitar((-altSol - 3) / 12);
    const t = performance.now() / 1000;
    if (oscuridad > 0.01) {
      for (const e of this.estrellas) {
        const parpadeo = this.reducido ? 0.85 : 0.65 + 0.35 * Math.sin(t * e.vel + e.fase);
        ctx.fillStyle = `rgba(235,240,255,${(oscuridad * parpadeo).toFixed(3)})`;
        ctx.beginPath(); ctx.arc(e.x * w, e.y * horizonte, e.r, 0, 6.2832); ctx.fill();
      }
    }

    // el Sol: un disco con halo; baja de color hacia el naranja cerca del horizonte
    if (sol.altura > -0.25) {
      const x = xDe(sol.azimut), y = yDe(sol.altura), bajo = limitar(1 - altSol / 20);
      const col: Color = [mezcla(255, 255, bajo), mezcla(244, 150, bajo), mezcla(214, 70, bajo)];
      const halo = ctx.createRadialGradient(x, y, 0, x, y, Math.max(w, h) * 0.32);
      halo.addColorStop(0, rgb(col, 0.55 * limitar(altSol / 4 + 1))); halo.addColorStop(1, rgb(col, 0));
      ctx.fillStyle = halo; ctx.fillRect(0, 0, w, h);
      ctx.fillStyle = rgb(col, 0.98); ctx.beginPath(); ctx.arc(x, y, Math.min(w, h) * 0.035, 0, 6.2832); ctx.fill();
    }

    // la Luna, con su fase de verdad (se ve de día también, pálida)
    if (luna.altura > -0.15) this.dibujarLuna(ctx, xDe(luna.azimut), yDe(luna.altura), Math.min(w, h) * 0.04, this.fase, limitar(altSol / 12));

    // nubes suaves: poca cosa, solo para dar vida
    if (!this.reducido) for (const n of this.nubes) n.x = (n.x + n.v * dt * 0.1) % 1.3;
    const luzNube = limitar((altSol + 8) / 20);
    ctx.fillStyle = `rgba(${mezcla(40, 255, luzNube) | 0},${mezcla(46, 255, luzNube) | 0},${mezcla(70, 255, luzNube) | 0},${(0.05 + 0.12 * luzNube).toFixed(3)})`;
    for (const n of this.nubes) { const x = (n.x - 0.15) * w, y = n.y * h, ww = n.w * w * 0.5; ctx.beginPath(); ctx.ellipse(x, y, ww, ww * 0.1, 0, 0, 6.2832); ctx.fill(); ctx.beginPath(); ctx.ellipse(x + ww * 0.4, y - ww * 0.05, ww * 0.6, ww * 0.08, 0, 0, 6.2832); ctx.fill(); }

    // estrella fugaz, solo de noche y no muy a menudo
    if (!this.reducido && oscuridad > 0.6) {
      this.proximaFugaz -= dt;
      if (!this.fugaz && this.proximaFugaz <= 0) { this.fugaz = { x: 0.2 + Math.random() * 0.6, y: 0.05 + Math.random() * 0.3, vx: 0.5 + Math.random() * 0.3, vy: 0.22, vida: 1 }; this.proximaFugaz = 14 + Math.random() * 25; }
    }
    if (this.fugaz) {
      const f = this.fugaz; f.x += f.vx * dt; f.y += f.vy * dt; f.vida -= dt * 1.4;
      if (f.vida <= 0) this.fugaz = null;
      else { const x = f.x * w, y = f.y * h; const l = ctx.createLinearGradient(x, y, x - f.vx * w * 0.18, y - f.vy * h * 0.18); l.addColorStop(0, `rgba(255,255,255,${f.vida})`); l.addColorStop(1, 'rgba(255,255,255,0)'); ctx.strokeStyle = l; ctx.lineWidth = 1.6; ctx.beginPath(); ctx.moveTo(x, y); ctx.lineTo(x - f.vx * w * 0.18, y - f.vy * h * 0.18); ctx.stroke(); }
    }

    // montes del horizonte: se oscurecen de noche y se tiñen del color del cielo de día
    const noche = limitar((-altSol + 2) / 16);
    this.monte(ctx, w, h, horizonte - h * 0.015, h * 0.07, 0.9, mezcla(70, 12, noche), mezcla(86, 16, noche), mezcla(112, 28, noche), 0.55);
    this.monte(ctx, w, h, horizonte + h * 0.02, h * 0.06, 2.3, mezcla(40, 6, noche), mezcla(52, 9, noche), mezcla(70, 18, noche), 1);

    // el pulso de «bien» o «mal»
    if (this.pulso) {
      ctx.fillStyle = `rgba(${this.pulso.color},${(0.28 * this.pulso.t).toFixed(3)})`; ctx.fillRect(0, 0, w, h);
      this.pulso.t -= this.reducido ? 1 : dt * 1.6;
      if (this.pulso.t <= 0) this.pulso = null;
    }
  }

  private monte(ctx: CanvasRenderingContext2D, w: number, h: number, base: number, alto: number, semilla: number, r: number, g: number, b: number, a: number): void {
    ctx.fillStyle = `rgba(${r | 0},${g | 0},${b | 0},${a})`;
    ctx.beginPath(); ctx.moveTo(0, h);
    for (let x = 0; x <= w + 8; x += 8) { const k = x / w; ctx.lineTo(x, base - alto * (0.55 * Math.sin(k * 5 + semilla) + 0.3 * Math.sin(k * 11 + semilla * 2) + 0.15 * Math.sin(k * 23 + semilla * 3) + 0.6)); }
    ctx.lineTo(w, h); ctx.closePath(); ctx.fill();
  }

  // Disco oscuro y, encima, la parte iluminada: media circunferencia del lado del Sol y la mitad de una elipse que hace de terminador
  private dibujarLuna(ctx: CanvasRenderingContext2D, x: number, y: number, r: number, f: FaseLuna, dia: number): void {
    const brillo = 1 - 0.55 * dia;   // de día se ve más pálida
    ctx.save(); ctx.translate(x, y);
    const halo = ctx.createRadialGradient(0, 0, r * 0.6, 0, 0, r * 3.2);
    halo.addColorStop(0, `rgba(210,225,255,${(0.22 * f.fraccion * brillo).toFixed(3)})`); halo.addColorStop(1, 'rgba(210,225,255,0)');
    ctx.fillStyle = halo; ctx.beginPath(); ctx.arc(0, 0, r * 3.2, 0, 6.2832); ctx.fill();
    ctx.fillStyle = `rgba(44,56,86,${(0.9 * brillo).toFixed(3)})`; ctx.beginPath(); ctx.arc(0, 0, r, 0, 6.2832); ctx.fill();
    const creciente = f.fase <= 0.5;
    const k = creciente ? f.fase : 1 - f.fase;
    const rx = r * Math.cos(2 * Math.PI * k);
    if (!creciente) ctx.scale(-1, 1);
    ctx.fillStyle = `rgba(238,242,252,${(0.96 * brillo).toFixed(3)})`;
    ctx.beginPath();
    ctx.arc(0, 0, r, -Math.PI / 2, Math.PI / 2, false);
    ctx.ellipse(0, 0, Math.max(0.01, Math.abs(rx)), r, 0, Math.PI / 2, -Math.PI / 2, rx > 0);
    ctx.closePath(); ctx.fill();
    ctx.restore();
  }
}

