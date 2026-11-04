import { Component, DestroyRef, ElementRef, afterNextRender, effect, inject, untracked, viewChild } from '@angular/core';
import { TemaService } from '../core/tema.service';
import type { Escena, Opciones } from './fondos';

// Capa fija detrás de toda la app con el fondo animado elegido en Ajustes. «Aura» y «Nada» no usan el canvas (los pinta styles.css).
// El dibujo se descarga solo cuando hace falta, va a 30 fotogramas, se para con la pestaña oculta y, con «reducir movimiento», se queda quieto.
@Component({
  selector: 'app-fondo',
  template: `<canvas #lienzo aria-hidden="true"></canvas>`,
  styles: `
    :host { position: fixed; inset: 0; z-index: -1; display: block; pointer-events: none; transition: opacity 500ms ease; }
    :host(.fondo--oculto) { opacity: 0; }
    canvas { display: block; width: 100%; height: 100%; }
    /* se funde hacia los bordes para no competir con el contenido */
    :host { mask-image: radial-gradient(130% 120% at 50% 40%, #000 55%, transparent 100%); -webkit-mask-image: radial-gradient(130% 120% at 50% 40%, #000 55%, transparent 100%); }
    /* solo detrás de las páginas o solo del menú (en escritorio): el ancho del menú lo pone el marco en --ancho-lateral-html */
    @media (min-width: 961px) {
      :host-context(html[data-ambito='paginas']) { left: var(--ancho-lateral-html, 272px); }
      :host-context(html[data-ambito='menu']) { right: auto; width: var(--ancho-lateral-html, 272px); }
    }
  `,
  host: { '[class.fondo--oculto]': 'oculto', '(window:pointermove)': 'mover($event)' },
})
export class Fondo {
  private readonly tema = inject(TemaService);
  private readonly lienzo = viewChild.required<ElementRef<HTMLCanvasElement>>('lienzo');
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  protected oculto = true;
  private listo = false;
  private escena: Escena | null = null;
  private opciones: Opciones = { intensidad: 0.6, velocidad: 1 };
  private marco = 0;
  private ultimo = 0;
  private t = 0;
  private generacion = 0;
  private readonly reducido = matchMedia('(prefers-reduced-motion: reduce)').matches;

  constructor() {
    afterNextRender(() => { this.listo = true; this.montar(); });
    // otro fondo, otro color o modo claro/oscuro: se vuelve a montar
    effect(() => { this.tema.fondo(); this.tema.cambio(); this.tema.ambito(); untracked(() => { if (this.listo) this.montar(); }); });
    // intensidad y velocidad se aplican sobre la marcha, sin volver a montar
    effect(() => {
      const i = this.tema.intensidad(), v = this.tema.velocidad();
      untracked(() => { this.opciones.intensidad = i / 100; this.opciones.velocidad = v / 100; if (this.reducido && this.escena) this.escena.dibujar(this.t, 0); });
    });
    const ajustar = () => this.escena?.cambiar(this.host.nativeElement.clientWidth, this.host.nativeElement.clientHeight);
    const ro = new ResizeObserver(() => { ajustar(); if (this.reducido && this.escena) this.escena.dibujar(this.t, 0); });
    afterNextRender(() => ro.observe(this.host.nativeElement));
    const visibilidad = () => { if (document.hidden) this.parar(); else if (this.escena && !this.reducido) this.arrancar(); };
    document.addEventListener('visibilitychange', visibilidad);
    inject(DestroyRef).onDestroy(() => { this.generacion++; this.parar(); ro.disconnect(); document.removeEventListener('visibilitychange', visibilidad); });
  }

  protected mover(e: PointerEvent): void {
    const r = this.host.nativeElement.getBoundingClientRect();
    this.escena?.puntero(e.clientX - r.left, e.clientY - r.top);
  }

  private async montar(): Promise<void> {
    const id = this.tema.fondo(), mi = ++this.generacion;
    this.parar();
    this.escena = null;
    if (id === 'aura' || id === 'ninguno') { this.oculto = true; this.host.nativeElement.classList.add('fondo--oculto'); return; }
    try {
      const { crearEscena, paletaDe } = await import('./fondos');
      if (mi !== this.generacion) return; // mientras tanto se eligió otro
      const css = getComputedStyle(document.documentElement);
      const paleta = paletaDe(css.getPropertyValue('--brand'), document.documentElement.dataset['mode'] !== 'light');
      const canvas = this.lienzo().nativeElement;
      const escena = crearEscena(id, canvas, paleta, this.opciones);
      if (!escena) return;
      canvas.style.imageRendering = escena.pixelado ? 'pixelated' : 'auto';
      this.escena = escena;
      escena.cambiar(this.host.nativeElement.clientWidth || innerWidth, this.host.nativeElement.clientHeight || innerHeight);
      this.oculto = false; this.host.nativeElement.classList.remove('fondo--oculto');
      if (this.reducido) escena.dibujar(0, 0); else this.arrancar();
    } catch { this.oculto = true; this.host.nativeElement.classList.add('fondo--oculto'); } // sin canvas o sin red: la app sigue como si nada
  }

  private arrancar(): void {
    this.parar();
    this.ultimo = performance.now();
    const bucle = (ahora: number) => {
      this.marco = requestAnimationFrame(bucle);
      const dt = (ahora - this.ultimo) / 1000;
      if (dt < 1 / 30) return; // 30 fotogramas: de sobra para un fondo y gasta la mitad
      this.ultimo = ahora;
      const paso = Math.min(dt, 0.1) * this.opciones.velocidad;
      this.t += paso;
      this.escena?.dibujar(this.t, paso);
    };
    this.marco = requestAnimationFrame(bucle);
  }

  private parar(): void { cancelAnimationFrame(this.marco); this.marco = 0; }
}
