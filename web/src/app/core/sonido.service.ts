import { Injectable, effect, inject, signal } from '@angular/core';
import { SistemaService } from '../inicio/sistema.service';

const CLAVE = 'mda.sonido';

// La carga del PC (0-100 %) hecha sonido: una nota grave (La, 110 Hz) que sube hasta una octava y se abre con la carga.
export const frecuenciaBase = (cpu: number): number => 110 * Math.pow(2, Math.min(100, Math.max(0, cpu)) / 100);
export const corteFiltro = (cpu: number): number => 300 + Math.min(100, Math.max(0, cpu)) * 18;
export const ganancia = (volumen: number): number => Math.pow(Math.min(100, Math.max(0, volumen)) / 100, 2) * 0.12;

// Sonificación: un tono ambiental suave (dos senos, base y quinta, con un temblor lento) ligado a la carga del PC. Apagado por defecto, se enciende
// con un gesto del usuario (los navegadores no dejan sonar solos) y se acuerda en este navegador.
@Injectable({ providedIn: 'root' })
export class SonidoService {
  private readonly sistema = inject(SistemaService);
  readonly activo = signal(false);
  readonly volumen = signal(30);
  private ctx: AudioContext | null = null;
  private base: OscillatorNode | null = null;
  private quinta: OscillatorNode | null = null;
  private filtro: BiquadFilterNode | null = null;
  private salida: GainNode | null = null;

  constructor() {
    try { const g = JSON.parse(localStorage.getItem(CLAVE) ?? '{}') as { volumen?: number }; if (typeof g.volumen === 'number') this.volumen.set(Math.min(100, Math.max(0, g.volumen))); } catch { /* sin almacenamiento */ }
    effect(() => {
      const cpu = this.sistema.actual()?.cpu ?? 0;
      const v = this.volumen();
      if (!this.ctx || !this.base) return;
      const t = this.ctx.currentTime;
      this.base.frequency.setTargetAtTime(frecuenciaBase(cpu), t, 0.8);
      this.quinta?.frequency.setTargetAtTime(frecuenciaBase(cpu) * 1.5, t, 0.8);
      this.filtro?.frequency.setTargetAtTime(corteFiltro(cpu), t, 0.8);
      this.salida?.gain.setTargetAtTime(ganancia(v), t, 0.3);
    });
    document.addEventListener('visibilitychange', () => {
      if (!this.ctx) return;
      void (document.hidden ? this.ctx.suspend() : this.ctx.resume());
    });
  }

  // se llama desde un clic: así el navegador permite el audio
  async ponerActivo(si: boolean): Promise<void> {
    if (si === this.activo()) return;
    if (!si) { this.parar(); return; }
    const Ctx = window.AudioContext ?? (window as unknown as { webkitAudioContext?: typeof AudioContext }).webkitAudioContext;
    if (!Ctx) return;
    const ctx = new Ctx();
    const salida = ctx.createGain(); salida.gain.value = 0;
    const filtro = ctx.createBiquadFilter(); filtro.type = 'lowpass'; filtro.frequency.value = corteFiltro(0);
    const base = ctx.createOscillator(); base.type = 'sine'; base.frequency.value = 110;
    const quinta = ctx.createOscillator(); quinta.type = 'sine'; quinta.frequency.value = 165;
    const lfo = ctx.createOscillator(); lfo.frequency.value = 0.15;
    const lfoGanancia = ctx.createGain(); lfoGanancia.gain.value = 0.25;
    const tremolo = ctx.createGain(); tremolo.gain.value = 0.75;
    lfo.connect(lfoGanancia).connect(tremolo.gain);
    base.connect(tremolo); quinta.connect(tremolo); tremolo.connect(filtro).connect(salida).connect(ctx.destination);
    base.start(); quinta.start(); lfo.start();
    this.ctx = ctx; this.base = base; this.quinta = quinta; this.filtro = filtro; this.salida = salida;
    this.activo.set(true);
    salida.gain.setTargetAtTime(ganancia(this.volumen()), ctx.currentTime, 0.6);   // entra poco a poco
    await ctx.resume().catch(() => {});
  }

  ponerVolumen(v: number): void {
    this.volumen.set(v);
    try { localStorage.setItem(CLAVE, JSON.stringify({ volumen: v })); } catch { /* sin almacenamiento */ }
  }

  private parar(): void {
    this.activo.set(false);
    const { ctx, salida } = this;
    this.ctx = this.base = this.quinta = this.filtro = this.salida = null;
    if (ctx) { salida?.gain.setTargetAtTime(0, ctx.currentTime, 0.1); setTimeout(() => void ctx.close(), 400); }
  }
}
