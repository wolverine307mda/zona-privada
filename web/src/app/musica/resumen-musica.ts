import { HttpClient } from '@angular/common/http';
import { Component, DestroyRef, ElementRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { PerfilService } from '../core/perfil.service';
import { tonoDe } from './musica';

interface Cancion { id: string; nombre: string; artistas: string; imagen: string | null; url: string | null; }
interface Artista { id: string; nombre: string; imagen: string | null; url: string | null; generos: string[]; popularidad: number; }

const PERIODOS = [{ id: 'long_term', nombre: 'El último año' }, { id: 'medium_term', nombre: '6 meses' }, { id: 'short_term', nombre: '4 semanas' }];
const DURACION = 6500;

// «Tu música»: un resumen a pantalla completa, en historias que pasan solas (como el resumen anual de Spotify),
// con tus artistas y canciones más escuchados, tus géneros y qué tipo de oyente eres. Toca a los lados para avanzar.
@Component({
  selector: 'app-resumen-musica',
  imports: [RouterLink],
  template: `
    <div class="escena" [style.--tono]="tono()" (pointerdown)="pausar(true)" (pointerup)="pausar(false)" (pointerleave)="pausar(false)">
      <div class="fondo" aria-hidden="true">@if (imagen(); as i) { <img [src]="i" alt=""> }</div>

      <header class="arriba">
        <div class="barras" aria-hidden="true">
          @for (d of diapos(); track d; let i = $index) {
            <span><i [style.width.%]="i < actual() ? 100 : i === actual() ? progreso() : 0"></i></span>
          }
        </div>
        <div class="arriba__fila">
          <a class="cerrar" routerLink="/musica" aria-label="Volver a Música"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6l12 12M18 6 6 18" /></svg></a>
          <div class="periodos" role="group" aria-label="Periodo">
            @for (p of periodos; track p.id) { <button type="button" [attr.aria-pressed]="periodo() === p.id" (click)="verPeriodo(p.id); $event.stopPropagation()">{{ p.nombre }}</button> }
          </div>
        </div>
      </header>

      @if (cargando()) {
        <div class="diapo"><p class="grande">Preparando tu música…</p></div>
      } @else if (!artistas().length) {
        <div class="diapo"><p class="grande">Aún no hay bastantes escuchas</p><p>Prueba con otro periodo o vuelve dentro de unos días.</p><a class="boton" routerLink="/musica">Volver</a></div>
      } @else {
        @switch (diapos()[actual()]) {
          @case ('hola') {
            <div class="diapo diapo--hola">
              <p class="pre">{{ periodoNombre() }}</p>
              <h1>{{ nombre() }},<br>esta es<br>tu música</h1>
              <p>Toca a la derecha para seguir →</p>
            </div>
          }
          @case ('artista') {
            <div class="diapo">
              <p class="pre">Tu artista número 1</p>
              @if (artistas()[0].imagen) { <img class="redonda" [src]="artistas()[0].imagen" alt="" width="260" height="260"> }
              <h2>{{ artistas()[0].nombre }}</h2>
              @if (artistas()[0].generos.length) { <p>{{ artistas()[0].generos.slice(0, 2).join(' · ') }}</p> }
            </div>
          }
          @case ('artistas') {
            <div class="diapo diapo--lista">
              <p class="pre">Tus artistas</p>
              <ol>
                @for (a of artistas().slice(0, 5); track a.id; let i = $index) {
                  <li [style.animation-delay.ms]="i * 120"><b>{{ i + 1 }}</b>@if (a.imagen) { <img [src]="a.imagen" alt="" width="64" height="64"> }<span>{{ a.nombre }}</span></li>
                }
              </ol>
            </div>
          }
          @case ('cancion') {
            <div class="diapo">
              <p class="pre">La canción que más has puesto</p>
              @if (canciones()[0]?.imagen) { <img class="portada" [src]="canciones()[0].imagen" alt="" width="280" height="280"> }
              <h2>{{ canciones()[0]?.nombre }}</h2>
              <p>{{ canciones()[0]?.artistas }}</p>
            </div>
          }
          @case ('canciones') {
            <div class="diapo diapo--lista">
              <p class="pre">Tus canciones</p>
              <ol>
                @for (c of canciones().slice(0, 5); track c.id; let i = $index) {
                  <li [style.animation-delay.ms]="i * 120"><b>{{ i + 1 }}</b>@if (c.imagen) { <img class="cuadrada" [src]="c.imagen" alt="" width="64" height="64"> }<span>{{ c.nombre }}<small>{{ c.artistas }}</small></span></li>
                }
              </ol>
            </div>
          }
          @case ('generos') {
            <div class="diapo diapo--lista">
              <p class="pre">Lo que más suena en ti</p>
              <ul class="generos">
                @for (g of generos(); track g.nombre; let i = $index) {
                  <li [style.animation-delay.ms]="i * 120"><span>{{ g.nombre }}</span><i [style.width.%]="g.pct"></i></li>
                }
              </ul>
            </div>
          }
          @case ('oyente') {
            <div class="diapo">
              <p class="pre">Tu forma de escuchar</p>
              <span class="emoji">{{ oyente().emoji }}</span>
              <h2>{{ oyente().titulo }}</h2>
              <p>{{ oyente().texto }}</p>
            </div>
          }
          @case ('fin') {
            <div class="diapo diapo--fin">
              <p class="pre">{{ periodoNombre() }} en resumen</p>
              <div class="tarjeta">
                <div><small>Artista</small><strong>{{ artistas()[0].nombre }}</strong></div>
                <div><small>Canción</small><strong>{{ canciones()[0]?.nombre }}</strong></div>
                <div><small>Género</small><strong>{{ generos()[0]?.nombre ?? '—' }}</strong></div>
                <div><small>Oyente</small><strong>{{ oyente().emoji }} {{ oyente().titulo }}</strong></div>
              </div>
              <div class="botones">
                <button class="boton" type="button" (click)="ir(0); $event.stopPropagation()">Otra vez</button>
                <a class="boton boton--claro" routerLink="/musica">Volver a Música</a>
              </div>
            </div>
          }
        }
      }

      <button class="zona zona--izq" type="button" aria-label="Anterior" (click)="ir(actual() - 1)"></button>
      <button class="zona zona--der" type="button" aria-label="Siguiente" (click)="ir(actual() + 1)"></button>
    </div>
  `,
  styles: `
    :host { display: block; position: fixed; inset: 0; z-index: 40; }
    .escena { --tono: 40, 40, 60; position: absolute; inset: 0; overflow: hidden; color: #fff; user-select: none; touch-action: manipulation;
      background: radial-gradient(120% 90% at 50% 0%, rgb(var(--tono)), color-mix(in srgb, rgb(var(--tono)) 25%, #050608) 70%); transition: background 1.2s ease; }
    .fondo { position: absolute; inset: 0; pointer-events: none; }
    .fondo img { position: absolute; inset: -20%; width: 140%; height: 140%; object-fit: cover; filter: blur(70px) saturate(1.5); opacity: 0.45; animation: deriva 30s ease-in-out infinite alternate; }
    .fondo::after { content: ''; position: absolute; inset: 0; background: linear-gradient(180deg, rgba(0, 0, 0, 0.2), rgba(0, 0, 0, 0.55)); }
    @keyframes deriva { to { transform: scale(1.15) rotate(6deg); } }
    .arriba { position: absolute; z-index: 3; top: 0; left: 0; right: 0; padding: calc(12px + var(--seguro-arriba)) 14px 0; display: flex; flex-direction: column; gap: 12px; max-width: 720px; margin: 0 auto; }
    .barras { display: flex; gap: 4px; }
    .barras span { flex: 1; height: 3px; border-radius: 3px; background: rgba(255, 255, 255, 0.3); overflow: hidden; }
    .barras i { display: block; height: 100%; background: #fff; }
    .arriba__fila { display: flex; align-items: center; gap: 10px; }
    .cerrar { display: grid; place-items: center; width: 40px; height: 40px; border-radius: 50%; background: rgba(0, 0, 0, 0.3); color: #fff; }
    .periodos { display: flex; gap: 4px; margin-left: auto; padding: 4px; border-radius: 999px; background: rgba(0, 0, 0, 0.3); }
    .periodos button { min-height: 32px; padding: 0 12px; border-radius: 999px; border: 0; background: transparent; color: rgba(255, 255, 255, 0.75); font: 600 12px/1 var(--font-text); cursor: pointer; }
    .periodos button[aria-pressed='true'] { background: #fff; color: #000; }
    .diapo { position: absolute; inset: 0; z-index: 1; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 14px; padding: 110px 24px 60px; text-align: center; max-width: 720px; margin: 0 auto; animation: entra 700ms var(--ease-expo) both; }
    @keyframes entra { from { opacity: 0; transform: translateY(24px) scale(0.97); } }
    .pre { font: 600 13px/1 var(--font-text); letter-spacing: 0.14em; text-transform: uppercase; opacity: 0.8; }
    h1 { font: 700 clamp(2.6rem, 11vw, 5.6rem)/0.92 var(--font-display); letter-spacing: -0.05em; text-transform: uppercase; }
    h2 { font: 700 clamp(2rem, 8vw, 4rem)/0.95 var(--font-display); letter-spacing: -0.045em; text-wrap: balance; overflow-wrap: anywhere; }
    .grande { font: 700 clamp(1.6rem, 6vw, 2.6rem)/1 var(--font-display); letter-spacing: -0.04em; }
    .diapo p { max-width: 40ch; opacity: 0.85; }
    .redonda { width: min(62vw, 280px); height: auto; aspect-ratio: 1; border-radius: 50%; object-fit: cover; box-shadow: 0 30px 80px -20px rgba(0, 0, 0, 0.7); animation: aparece 1s var(--ease-expo) both 150ms; }
    .portada { width: min(66vw, 300px); height: auto; aspect-ratio: 1; border-radius: 14px; object-fit: cover; box-shadow: 0 30px 80px -20px rgba(0, 0, 0, 0.7); animation: aparece 1s var(--ease-expo) both 150ms; }
    @keyframes aparece { from { opacity: 0; transform: scale(0.8) rotate(-6deg); } }
    .diapo--lista ol, .generos { width: 100%; max-width: 460px; display: flex; flex-direction: column; gap: 10px; list-style: none; margin: 0; padding: 0; text-align: left; }
    .diapo--lista li { display: flex; align-items: center; gap: 14px; padding: 8px 12px 8px 8px; border-radius: 16px; background: rgba(255, 255, 255, 0.1); backdrop-filter: blur(10px); animation: entra 600ms var(--ease-expo) both; }
    .diapo--lista b { width: 30px; text-align: center; font: 700 22px/1 var(--font-display); }
    .diapo--lista img { flex: none; width: 56px; height: 56px; border-radius: 50%; object-fit: cover; }
    .diapo--lista img.cuadrada { border-radius: 8px; }
    .diapo--lista span { display: flex; flex-direction: column; min-width: 0; font-weight: 600; font-size: 17px; overflow: hidden; }
    .diapo--lista small { font-weight: 400; font-size: 13px; opacity: 0.75; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .generos li { position: relative; display: flex; align-items: center; height: 46px; padding: 0 16px; border-radius: 14px; background: rgba(255, 255, 255, 0.08); overflow: hidden; animation: entra 600ms var(--ease-expo) both; }
    .generos li span { position: relative; z-index: 1; font-weight: 600; text-transform: capitalize; }
    .generos li i { position: absolute; inset: 0 auto 0 0; background: rgba(255, 255, 255, 0.22); border-radius: inherit; animation: crece 1.2s var(--ease-expo) both 300ms; }
    @keyframes crece { from { width: 0 !important; } }
    .emoji { font-size: 90px; line-height: 1; animation: aparece 1s var(--ease-expo) both 150ms; }
    .tarjeta { width: 100%; max-width: 420px; display: grid; grid-template-columns: 1fr 1fr; gap: 10px; padding: 16px; border-radius: 22px; background: rgba(255, 255, 255, 0.12); backdrop-filter: blur(14px); text-align: left; }
    .tarjeta div { display: flex; flex-direction: column; gap: 4px; padding: 12px; border-radius: 14px; background: rgba(0, 0, 0, 0.2); min-width: 0; }
    .tarjeta small { font-size: 11px; text-transform: uppercase; letter-spacing: 0.1em; opacity: 0.7; }
    .tarjeta strong { font: 700 17px/1.15 var(--font-display); letter-spacing: -0.02em; overflow-wrap: anywhere; }
    .botones { position: relative; z-index: 3; display: flex; gap: 10px; }
    .boton { position: relative; z-index: 3; display: inline-grid; place-items: center; min-height: 48px; padding: 0 22px; border-radius: 999px; border: 0; background: #fff; color: #000; font: 600 15px/1 var(--font-text); text-decoration: none; cursor: pointer; }
    .boton--claro { background: rgba(255, 255, 255, 0.16); color: #fff; }
    .zona { position: absolute; z-index: 2; top: 90px; bottom: 0; width: 32%; border: 0; background: transparent; cursor: pointer; }
    .zona--izq { left: 0; } .zona--der { right: 0; width: 68%; }
    .diapo--fin ~ .zona { bottom: 120px; }
    @media (prefers-reduced-motion: reduce) { .fondo img { animation: none; } }
  `,
})
export class ResumenMusica {
  private readonly http = inject(HttpClient);
  private readonly perfil = inject(PerfilService);
  protected readonly periodos = PERIODOS;
  protected readonly periodo = signal('long_term');
  protected readonly canciones = signal<Cancion[]>([]);
  protected readonly artistas = signal<Artista[]>([]);
  protected readonly cargando = signal(true);
  protected readonly actual = signal(0);
  protected readonly progreso = signal(0);
  protected readonly tono = signal('40, 40, 60');
  private pausado = false;

  protected readonly diapos = computed(() => ['hola', 'artista', 'artistas', 'cancion', 'canciones', ...(this.generos().length ? ['generos'] : []), 'oyente', 'fin']);
  protected readonly periodoNombre = computed(() => PERIODOS.find((p) => p.id === this.periodo())!.nombre);
  protected readonly nombre = computed(() => this.perfil.datos()?.nombre || this.perfil.datos()?.usuario || 'Oye');
  protected readonly imagen = computed(() => {
    const d = this.diapos()[this.actual()];
    return d === 'cancion' || d === 'canciones' ? this.canciones()[0]?.imagen ?? null : this.artistas()[0]?.imagen ?? null;
  });

  // los géneros de tus artistas, ponderados por su puesto
  protected readonly generos = computed(() => {
    const c = new Map<string, number>();
    this.artistas().forEach((a, i) => a.generos.forEach((g) => c.set(g, (c.get(g) ?? 0) + (20 - i))));
    const lista = [...c.entries()].sort((a, b) => b[1] - a[1]).slice(0, 5);
    const max = lista[0]?.[1] ?? 1;
    return lista.map(([nombre, v]) => ({ nombre, pct: Math.round((v / max) * 100) }));
  });

  // qué tipo de oyente: por lo conocidos que son tus artistas y lo repartido que está tu top de canciones
  protected readonly oyente = computed(() => {
    const a = this.artistas().slice(0, 10);
    const pop = a.length ? a.reduce((t, x) => t + x.popularidad, 0) / a.length : 50;
    const distintos = new Set(this.canciones().map((c) => c.artistas.split(',')[0].trim())).size;
    if (pop < 45) return { emoji: '💎', titulo: 'Buscador de joyas', texto: `Tus artistas no son de los más sonados: la popularidad media de tu top es ${Math.round(pop)}/100. Encuentras lo bueno antes que nadie.` };
    if (distintos <= 6) return { emoji: '🎯', titulo: 'Fiel a los tuyos', texto: `Tu top de canciones se reparte entre solo ${distintos} artistas. Cuando algo te gusta, lo exprimes.` };
    if (pop > 75) return { emoji: '🔥', titulo: 'En la cresta de la ola', texto: `Escuchas lo que está sonando: la popularidad media de tus artistas es ${Math.round(pop)}/100.` };
    return { emoji: '🌈', titulo: 'Explorador', texto: `Tu top mezcla ${distintos} artistas distintos: un poco de todo, siempre algo nuevo.` };
  });

  constructor() {
    // a pantalla completa de verdad: fuera del marco (la barra de arriba y las pestañas quedarían encima)
    const el = inject(ElementRef<HTMLElement>).nativeElement as HTMLElement;
    queueMicrotask(() => document.body.appendChild(el));
    document.documentElement.style.overflow = 'hidden';
    inject(DestroyRef).onDestroy(() => { document.documentElement.style.overflow = ''; el.remove(); });
    this.perfil.cargar();
    effect(() => { const p = this.periodo(); untracked(() => this.cargar(p)); });
    // el color de fondo sale de la imagen de cada historia
    effect(() => { const i = this.imagen(); if (i) tonoDe(i).then((t) => t && this.tono.set(t)); });
    // las historias pasan solas; mantener pulsado las pausa
    let ultimo = performance.now();
    const t = setInterval(() => {
      const ahora = performance.now(), dt = ahora - ultimo;
      ultimo = ahora;
      if (this.pausado || this.cargando() || document.hidden) return;
      if (this.actual() >= this.diapos().length - 1) { this.progreso.set(100); return; }
      const p = this.progreso() + (dt / DURACION) * 100;
      if (p >= 100) this.ir(this.actual() + 1); else this.progreso.set(p);
    }, 50);
    const teclas = (e: KeyboardEvent) => { if (e.key === 'ArrowRight') this.ir(this.actual() + 1); if (e.key === 'ArrowLeft') this.ir(this.actual() - 1); };
    document.addEventListener('keydown', teclas);
    inject(DestroyRef).onDestroy(() => { clearInterval(t); document.removeEventListener('keydown', teclas); });
  }

  private async cargar(periodo: string): Promise<void> {
    this.cargando.set(true);
    try {
      const [c, a] = await Promise.all([
        firstValueFrom(this.http.get<Cancion[]>('/api/musica/top', { params: { tipo: 'canciones', periodo } })),
        firstValueFrom(this.http.get<Artista[]>('/api/musica/top', { params: { tipo: 'artistas', periodo } })),
      ]);
      this.canciones.set(c);
      this.artistas.set(a);
    } catch { this.canciones.set([]); this.artistas.set([]); }
    this.cargando.set(false);
    this.ir(0);
  }

  protected verPeriodo(p: string): void { if (p !== this.periodo()) this.periodo.set(p); }
  protected ir(i: number): void {
    this.actual.set(Math.max(0, Math.min(this.diapos().length - 1, i)));
    this.progreso.set(0);
  }
  protected pausar(v: boolean): void { this.pausado = v; }
}
