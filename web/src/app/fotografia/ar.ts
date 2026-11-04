import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, OnInit, inject, signal, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { elevacionDe, proyectar, rumboDe } from './ar-mat';

interface Pos { alt: number; az: number; }
interface PuntoDia { min: number; sol: Pos; luna: Pos; }
interface Dia { sinLugar: boolean; serie?: PuntoDia[]; }
interface EventoOrientacion extends DeviceOrientationEvent { webkitCompassHeading?: number; }

// El arco del sol y de la luna de hoy dibujado sobre la cámara del móvil, con la brújula y el giroscopio. Los acimut y alturas vienen de /api/luz/dia.
@Component({
  selector: 'app-ar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  template: `
    <div class="ar">
      <video #video playsinline muted aria-hidden="true"></video>
      <canvas #lienzo role="img" aria-label="El arco del sol y la luna sobre la cámara"></canvas>
      @if (!activo()) {
        <div class="ar__inicio">
          <h1>El sol y la luna en el cielo, de verdad</h1>
          <p>Apunta el móvil al horizonte y verás por dónde sale, sube y se pone el sol (amarillo) y la luna (blanco) hoy. Usa la cámara y la brújula; no se envía nada.</p>
          @if (error()) { <p class="ar__error" role="alert">{{ error() }}</p> }
          <button type="button" class="boton" (click)="empezar()">Empezar</button>
          <a routerLink="/fotografia" class="enlace">Volver a Fotografía</a>
        </div>
      } @else {
        <div class="ar__barra">
          <a routerLink="/fotografia" class="enlace" aria-label="Salir">✕</a>
          <span>{{ rumboTexto() }}</span>
          <label>Campo <input type="range" min="30" max="90" [value]="fov()" (input)="fov.set(+$any($event.target).value)" aria-label="Campo de visión de la cámara"></label>
        </div>
        @if (!calibrada()) { <p class="ar__aviso">Mueve el móvil dibujando un 8 para calibrar la brújula.</p> }
      }
    </div>
  `,
  styles: `
    :host { display: block; }
    .ar { position: fixed; inset: 0; z-index: 60; background: #000; color: #fff; }
    video, canvas { position: absolute; inset: 0; width: 100%; height: 100%; object-fit: cover; }
    .ar__inicio { position: absolute; inset: 0; display: grid; align-content: center; justify-items: center; gap: 14px; padding: 24px; text-align: center; background: rgb(0 0 0 / .7); }
    .ar__inicio h1 { margin: 0; font: var(--peso-ligero) 1.5rem/1.2 var(--font-text); }
    .ar__inicio p { margin: 0; max-width: 40ch; opacity: .85; }
    .ar__error { color: #ff8a8a; }
    .ar__barra { position: absolute; top: max(12px, env(safe-area-inset-top)); left: 12px; right: 12px; display: flex; align-items: center; justify-content: space-between; gap: 12px; padding: 8px 14px; border-radius: 999px; background: rgb(0 0 0 / .55); backdrop-filter: blur(8px); }
    .ar__barra label { display: flex; align-items: center; gap: 8px; font-size: .78rem; }
    .ar__barra input { width: 90px; }
    .ar__aviso { position: absolute; bottom: max(24px, env(safe-area-inset-bottom)); left: 50%; transform: translateX(-50%); margin: 0; padding: 8px 14px; border-radius: 999px; background: rgb(0 0 0 / .6); font-size: .82rem; white-space: nowrap; }
  `,
})
export class Ar implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly video = viewChild.required<ElementRef<HTMLVideoElement>>('video');
  private readonly lienzo = viewChild.required<ElementRef<HTMLCanvasElement>>('lienzo');
  protected readonly activo = signal(false);
  protected readonly error = signal('');
  protected readonly fov = signal(50);
  protected readonly calibrada = signal(false);
  protected readonly rumboTexto = signal('—');
  private serie: PuntoDia[] = [];
  private rumbo: number | null = null;
  private elev = 0;
  private flujo: MediaStream | null = null;
  private marco = 0;
  private readonly alOrientar = (e: Event): void => {
    const o = e as EventoOrientacion;
    const r = rumboDe(o.alpha, o.webkitCompassHeading ?? null);
    if (r !== null) { this.rumbo = r; this.rumboTexto.set(`${Math.round(r)}° ${this.punto(r)}`); }
    if (o.beta !== null) this.elev = elevacionDe(o.beta);
    // sin brújula absoluta (alpha relativo) el aviso se queda
    if (o.absolute || o.webkitCompassHeading !== undefined) this.calibrada.set(true);
  };

  constructor() {
    inject(DestroyRef).onDestroy(() => this.parar());
  }

  ngOnInit(): void {
    firstValueFrom(this.http.get<Dia>('/api/luz/dia')).then((d) => { this.serie = d.serie ?? []; })
      .catch(() => this.error.set('No se pudieron leer las posiciones del sol y la luna.'));
  }

  private punto(r: number): string { return ['N', 'NE', 'E', 'SE', 'S', 'SO', 'O', 'NO'][Math.round(r / 45) % 8]; }

  protected async empezar(): Promise<void> {
    this.error.set('');
    if (!this.serie.length) { this.error.set('Necesito tu ciudad (tarjeta del tiempo de Inicio) para calcular el sol y la luna.'); return; }
    try {
      // iOS pide permiso explícito para los sensores de movimiento
      const O = DeviceOrientationEvent as unknown as { requestPermission?: () => Promise<string> };
      if (typeof O.requestPermission === 'function' && (await O.requestPermission()) !== 'granted') { this.error.set('Sin permiso para la brújula no puedo orientarme.'); return; }
      this.flujo = await navigator.mediaDevices.getUserMedia({ video: { facingMode: { ideal: 'environment' } }, audio: false });
    } catch { this.error.set('No pude abrir la cámara: revisa el permiso del navegador.'); return; }
    const v = this.video().nativeElement; v.srcObject = this.flujo; await v.play().catch(() => {});
    window.addEventListener('deviceorientationabsolute', this.alOrientar, true);
    window.addEventListener('deviceorientation', this.alOrientar, true);
    this.activo.set(true);
    this.marco = requestAnimationFrame(() => this.dibujar());
  }

  private parar(): void {
    cancelAnimationFrame(this.marco);
    window.removeEventListener('deviceorientationabsolute', this.alOrientar, true);
    window.removeEventListener('deviceorientation', this.alOrientar, true);
    this.flujo?.getTracks().forEach((t) => t.stop()); this.flujo = null;
  }

  private dibujar(): void {
    const c = this.lienzo().nativeElement, ctx = c.getContext('2d');
    const w = c.clientWidth, h = c.clientHeight;
    if (ctx && w && h) {
      if (c.width !== w || c.height !== h) { c.width = w; c.height = h; }
      ctx.clearRect(0, 0, w, h);
      if (this.rumbo !== null) {
        const fov = this.fov(), ahora = new Date(), min = ahora.getHours() * 60 + ahora.getMinutes();
        const traza = (tipo: 'sol' | 'luna', color: string) => {
          ctx.beginPath(); ctx.lineWidth = 3; ctx.strokeStyle = color; let abierto = false;
          for (const p of this.serie) {
            const q = proyectar(p[tipo].az, p[tipo].alt, this.rumbo!, this.elev, fov, w, h);
            if (q.visible && p[tipo].alt > -2) { abierto ? ctx.lineTo(q.x, q.y) : ctx.moveTo(q.x, q.y); abierto = true; } else abierto = false;
          }
          ctx.stroke();
          ctx.font = '12px sans-serif'; ctx.fillStyle = color; ctx.textAlign = 'center';
          for (const p of this.serie.filter((x) => x.min % 60 === 0 && x[tipo].alt > 0)) {
            const q = proyectar(p[tipo].az, p[tipo].alt, this.rumbo!, this.elev, fov, w, h);
            if (q.visible) { ctx.beginPath(); ctx.arc(q.x, q.y, 3, 0, 7); ctx.fill(); ctx.fillText(`${p.min / 60}h`, q.x, q.y - 8); }
          }
          const ahoraP = this.serie.reduce((a, b) => (Math.abs(b.min - min) < Math.abs(a.min - min) ? b : a));
          const q = proyectar(ahoraP[tipo].az, ahoraP[tipo].alt, this.rumbo!, this.elev, fov, w, h);
          if (q.visible) { ctx.beginPath(); ctx.arc(q.x, q.y, 12, 0, 7); ctx.fillStyle = color; ctx.fill(); ctx.strokeStyle = '#fff'; ctx.lineWidth = 2; ctx.stroke(); }
        };
        // el horizonte
        const hz = proyectar(this.rumbo, 0, this.rumbo, this.elev, fov, w, h);
        if (hz.visible) { ctx.strokeStyle = 'rgb(255 255 255 / .35)'; ctx.setLineDash([6, 6]); ctx.lineWidth = 1; ctx.beginPath(); ctx.moveTo(0, hz.y); ctx.lineTo(w, hz.y); ctx.stroke(); ctx.setLineDash([]); }
        traza('luna', '#e8eefc'); traza('sol', '#ffc83d');
      }
    }
    this.marco = requestAnimationFrame(() => this.dibujar());
  }
}
