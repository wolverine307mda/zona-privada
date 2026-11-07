import { Component, DestroyRef, ElementRef, afterNextRender, effect, inject, untracked, viewChild } from '@angular/core';
import { TemaService } from '../core/tema.service';
import type { Escena, Opciones, Paleta } from './fondos';
import { resolverColor } from '../core/color-css';
import { SenalesFondo, factorIntensidad, factorVelocidad } from './senales-fondo';

// Capa fija detrás de toda la app con el fondo animado elegido en Ajustes. «Aura» y «Nada» no usan el canvas (los pinta styles.css).
// El dibujo se descarga solo cuando hace falta, va a 30 fotogramas, se para con la pestaña oculta y, con «reducir movimiento», se queda quieto.
@Component({
  selector: 'app-fondo',
  template: `<canvas #lienzo aria-hidden="true"></canvas>`,
  styles: `
    /* altura de la ventana «grande» (100lvh): no cambia cuando en el móvil se esconde la barra del navegador al hacer scroll, así el lienzo no se redimensiona ni parpadea */
    :host { position: fixed; top: 0; left: 0; right: 0; height: 100vh; height: 100lvh; z-index: -1; display: block; pointer-events: none; transition: opacity 500ms ease; transform: translateZ(0); }
    :host(.fondo--oculto) { opacity: 0; }
    canvas { display: block; width: 100%; height: 100%; }
    /* se funde hacia los bordes para no competir con el contenido (en pantalla táctil no: la máscara obliga al móvil a repintar el lienzo entero en cada fotograma) */
    @media (pointer: fine) { :host { mask-image: radial-gradient(130% 120% at 50% 40%, #000 55%, transparent 100%); -webkit-mask-image: radial-gradient(130% 120% at 50% 40%, #000 55%, transparent 100%); } }
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
  private readonly senales = inject(SenalesFondo);
  private baseIntensidad = 0.6;
  private readonly lienzo = viewChild.required<ElementRef<HTMLCanvasElement>>('lienzo');
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  protected oculto = true;
  private listo = false;
  private escena: Escena | null = null;
  private paleta: Paleta | null = null;
  private readonly tactil = matchMedia('(pointer: coarse)').matches || innerWidth < 760;
  private opciones: Opciones = { intensidad: 0.6, velocidad: 1, ligero: this.tactil };
  private tamano = { w: 0, h: 0 };
  private desplazando = false;
  private finScroll = 0;
  private marco = 0;
  private ultimo = 0;
  private t = 0;
  private generacion = 0;
  private readonly reducido = matchMedia('(prefers-reduced-motion: reduce)').matches;

  constructor() {
    afterNextRender(() => { this.listo = true; this.montar(); });
    // otro fondo, otro color o modo claro/oscuro: se vuelve a montar
    effect(() => { this.tema.fondo(); this.tema.cambio(); this.tema.ambito(); untracked(() => { if (this.listo) this.montar(); }); });
    // intensidad, velocidad y fuerza del color se aplican sobre la marcha, sin volver a montar
    effect(() => {
      const i = this.tema.intensidad(), v = this.tema.velocidad(), f = this.tema.fuerza();
      untracked(() => { this.baseIntensidad = i / 100; this.opciones.intensidad = i / 100; this.opciones.velocidad = v / 100; if (this.paleta) this.paleta.fuerza = f / 100; if (this.reducido && this.escena) this.escena.dibujar(this.t, 0); });
    });
    // «reaccionar a»: música, carga del PC u hora (solo se pregunta al servidor mientras haga falta)
    effect(() => { this.senales.modo(); untracked(() => this.senales.vigilar(this.tactil)); });
    // en el móvil, al esconderse o aparecer la barra del navegador el alto cambia unos píxeles: no se vuelve a crear el dibujo por eso
    const ajustar = () => {
      const w = this.host.nativeElement.clientWidth, h = this.host.nativeElement.clientHeight;
      if (this.tactil && this.tamano.w === w && Math.abs(this.tamano.h - h) < 160) return;
      this.tamano = { w, h };
      this.escena?.cambiar(w, h);
    };
    // mientras se hace scroll el dibujo se queda quieto: así todo el trabajo del móvil es para desplazar la página y no se «pilla»
    const alScroll = () => { this.desplazando = true; clearTimeout(this.finScroll); this.finScroll = window.setTimeout(() => (this.desplazando = false), 160); };
    window.addEventListener('scroll', alScroll, { passive: true, capture: true });
    const ro = new ResizeObserver(() => { ajustar(); if (this.reducido && this.escena) this.escena.dibujar(this.t, 0); });
    afterNextRender(() => ro.observe(this.host.nativeElement));
    const visibilidad = () => { if (document.hidden) this.parar(); else if (this.escena && !this.reducido) this.arrancar(); };
    document.addEventListener('visibilitychange', visibilidad);
    inject(DestroyRef).onDestroy(() => { this.generacion++; this.parar(); this.senales.parar(); ro.disconnect(); document.removeEventListener('visibilitychange', visibilidad); window.removeEventListener('scroll', alScroll, { capture: true } as EventListenerOptions); clearTimeout(this.finScroll); });
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
      const paleta = paletaDe(resolverColor(css.getPropertyValue('--brand'), '#ff7a1f'), document.documentElement.dataset['mode'] !== 'light');
      const canvas = this.lienzo().nativeElement;
      paleta.fuerza = this.tema.fuerza() / 100;
      const escena = await crearEscena(id, canvas, paleta, this.opciones);
      if (!escena || mi !== this.generacion) return; // (o mientras tanto se eligió otro)
      this.paleta = paleta;
      canvas.style.imageRendering = escena.pixelado ? 'pixelated' : 'auto';
      this.escena = escena;
      this.tamano = { w: this.host.nativeElement.clientWidth || innerWidth, h: this.host.nativeElement.clientHeight || innerHeight };
      escena.cambiar(this.tamano.w, this.tamano.h);
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
      if (dt < (this.tactil ? 1 / 20 : 1 / 30)) return; // 30 fotogramas (20 en el móvil): de sobra para un fondo y gasta la mitad
      if (this.desplazando) { this.ultimo = ahora; return; } // haciendo scroll no se dibuja
      this.ultimo = ahora;
      // la energía de lo que pasa (0 sin reacción) acelera un poco el fondo y lo aviva; siempre con tope
      const e = this.senales.energia(this.t, this.tactil), cuanto = this.senales.cuanto() / 100;
      this.opciones.intensidad = Math.min(1, this.baseIntensidad * factorIntensidad(e, cuanto));
      const paso = Math.min(dt, 0.1) * this.opciones.velocidad * factorVelocidad(e, cuanto);
      this.t += paso;
      this.escena?.dibujar(this.t, paso);
    };
    this.marco = requestAnimationFrame(bucle);
  }

  private parar(): void { cancelAnimationFrame(this.marco); this.marco = 0; }
}
