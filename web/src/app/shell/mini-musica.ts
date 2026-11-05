import { HttpClient } from '@angular/common/http';
import { Component, DestroyRef, ElementRef, computed, inject, input, signal, viewChild } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';

interface Cancion { id: string; nombre: string; artistas: string; album: string | null; imagen: string | null; duracion: number; }
interface Ahora { sonando: boolean; cancion: Cancion | null; progreso: number; dispositivo: string | null; volumen: number | null; }

// Lo que suena, en la barra de arriba: portada que gira, título y pausa / siguiente desde cualquier sección.
// Al tocar la portada o el título se abre una ventana con todo (portada grande, progreso, controles y lo que viene después) y desde ella
// se salta a Música sin dejar la pantalla en la que estabas hasta que se pulsa. Pregunta cada 15 s con la pestaña a la vista (cada 5 s con
// la ventana abierta); si Spotify no está enlazado, no vuelve a preguntar.
@Component({
  selector: 'app-mini-musica',
  host: { '[class.suena]': '!oculto() && !!ahora()?.cancion' }, // el marco lo usa para dejar sitio al título de la barra en el móvil
  template: `
    @if (!oculto() && ahora()?.cancion; as c) {
      <div class="mini" [class.mini--parado]="!ahora()!.sonando">
        <button class="mini__disco" type="button" (click)="abrir()" [attr.aria-label]="'Sonando: ' + c.nombre + '. Abrir el reproductor'" aria-haspopup="dialog">
          @if (c.imagen) { <img [src]="c.imagen" alt="" width="34" height="34"> }
        </button>
        <button class="mini__texto" type="button" (click)="abrir()" aria-haspopup="dialog"><strong>{{ c.nombre }}</strong><span>{{ c.artistas }}</span></button>
        <button type="button" (click)="control(ahora()!.sonando ? 'pause' : 'play')" [attr.aria-label]="ahora()!.sonando ? 'Pausa' : 'Reproducir'">
          @if (ahora()!.sonando) { <svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M8 5v14M16 5v14" /></svg> }
          @else { <svg class="icon relleno" viewBox="0 0 24 24" aria-hidden="true"><path d="M7 5l12 7-12 7z" /></svg> }
        </button>
        <button class="mini__sig" type="button" (click)="control('next')" aria-label="Siguiente"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6v12l9-6zM18 6v12" /></svg></button>
      </div>
    }

    <dialog class="modal" #dlg tabindex="-1" aria-labelledby="mm-titulo" (click)="cerrarFuera($event)" (close)="abierto.set(false)">
      @if (ahora(); as a) {
        @if (a.cancion; as c) {
          <div class="caja" [style.--tono]="tono()" [class.caja--suena]="a.sonando">
            <header class="alto">
              <span class="estado"><i class="onda" aria-hidden="true"><b></b><b></b><b></b></i>{{ a.sonando ? 'Sonando ahora' : 'En pausa' }}</span>
              <button class="cerrar" type="button" (click)="modal().nativeElement.close()" aria-label="Cerrar el reproductor">
                <svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6l12 12M18 6 6 18" /></svg>
              </button>
            </header>
            <div class="portada">
              @if (c.imagen) {
                <img class="luz" [src]="c.imagen" alt="" aria-hidden="true">
                <img class="caratula" [src]="c.imagen" [alt]="'Portada de ' + (c.album ?? c.nombre)">
              }
            </div>
            <div class="datos">
              <h2 id="mm-titulo">{{ c.nombre }}</h2>
              <p class="artistas">{{ c.artistas }}</p>
              @if (c.album && c.album !== c.nombre) { <p class="album">{{ c.album }}</p> }
            </div>
            <div class="barra-t">
              <div class="progreso" role="progressbar" aria-label="Progreso de la canción" [attr.aria-valuenow]="pct()" aria-valuemin="0" aria-valuemax="100"><i [style.width.%]="pct()"></i></div>
              <div class="tiempos"><span>{{ mmss(progreso()) }}</span><span>{{ mmss(c.duracion) }}</span></div>
            </div>
            <div class="controles">
              <button type="button" (click)="control('previous')" aria-label="Anterior"><svg class="icon relleno" viewBox="0 0 24 24" aria-hidden="true"><path d="M18 6v12l-9-6zM6 6v12" /></svg></button>
              <button class="principal" type="button" (click)="control(a.sonando ? 'pause' : 'play')" [attr.aria-label]="a.sonando ? 'Pausa' : 'Reproducir'">
                @if (a.sonando) { <svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M8 5v14M16 5v14" /></svg> }
                @else { <svg class="icon relleno" viewBox="0 0 24 24" aria-hidden="true"><path d="M7 5l12 7-12 7z" /></svg> }
              </button>
              <button type="button" (click)="control('next')" aria-label="Siguiente"><svg class="icon relleno" viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6v12l9-6zM18 6v12" /></svg></button>
            </div>
            @if (a.dispositivo) {
              <p class="dispositivo">
                <svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M11 5 6 9H3v6h3l5 4zM15.5 8.5a5 5 0 0 1 0 7M18.5 5.5a9 9 0 0 1 0 13" /></svg>
                {{ a.dispositivo }}{{ a.volumen != null ? ' · ' + a.volumen + ' %' : '' }}
              </p>
            }
            @if (cola().length) {
              <section class="siguiente" aria-labelledby="mm-cola">
                <h3 id="mm-cola">A continuación</h3>
                <ol class="cola">
                  @for (q of cola(); track q.id) {
                    <li>@if (q.imagen) { <img [src]="q.imagen" alt="" width="44" height="44"> }<span><strong>{{ q.nombre }}</strong><small>{{ q.artistas }}</small></span></li>
                  }
                </ol>
              </section>
            }
            <button class="ir" type="button" (click)="irAMusica()">Abrir Música</button>
          </div>
        }
      }
    </dialog>
  `,
  styles: `
    :host { display: contents; }
    .mini { display: flex; align-items: center; gap: 8px; height: 42px; padding: 4px 6px 4px 4px; max-width: 280px; border-radius: 999px; border: 1px solid var(--vidrio-borde); background: var(--vidrio); backdrop-filter: var(--desenfoque); animation: entra 500ms var(--ease-expo); }
    @keyframes entra { from { opacity: 0; transform: translateY(-6px) scale(0.96); } }
    .mini__disco { flex: none; width: 34px; height: 34px; padding: 0; border: 0; border-radius: 50%; overflow: hidden; background: #111; cursor: pointer; box-shadow: 0 0 0 2px color-mix(in srgb, #1ed760 70%, transparent); animation: gira 6s linear infinite; }
    .mini--parado .mini__disco { animation-play-state: paused; box-shadow: 0 0 0 2px var(--linea); }
    .mini__disco img { width: 100%; height: 100%; object-fit: cover; mask: radial-gradient(circle, transparent 0 3px, #000 3.5px); }
    @keyframes gira { to { rotate: 360deg; } }
    .mini__texto { display: flex; flex-direction: column; align-items: flex-start; min-width: 0; width: auto; height: auto; padding: 0; border-radius: 8px; line-height: 1.15; color: var(--texto); text-align: left; }
    .mini__texto strong { max-width: 100%; font-size: 12.5px; font-weight: 600; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .mini__texto span { max-width: 100%; font-size: 11px; color: var(--soft); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .mini button { flex: none; display: grid; place-items: center; width: 32px; height: 32px; border-radius: 50%; border: 0; background: transparent; color: var(--texto); cursor: pointer; }
    .mini .mini__disco { width: 34px; height: 34px; }
    .icon { width: 16px; height: 16px; fill: none; stroke: currentColor; stroke-width: 1.8; stroke-linecap: round; stroke-linejoin: round; }
    .relleno { fill: currentColor; }
    @media (hover: hover) { .mini button:hover:not(.mini__disco) { background: var(--tarjeta-2); } }
    /* con .mini delante para ganar en especificidad a la regla «.mini button» (display: grid): si no, en el móvil el reproductor seguía ancho y empujaba el título de la barra */
    @media (max-width: 1180px) { .mini .mini__texto, .mini .mini__sig { display: none; } .mini { padding-right: 4px; } }

    /* la ventana del reproductor: portada grande y centrada sobre su propia luz, datos, progreso, controles y lo que viene después, todo en una sola columna */
    .modal { width: min(400px, calc(100vw - 24px)); max-height: calc(100svh - 24px); padding: 0; border: 1px solid var(--vidrio-borde); border-radius: 32px; background: var(--tarjeta); color: var(--texto); box-shadow: 0 30px 90px rgba(0, 0, 0, .5); overflow-x: hidden; overflow-y: auto; margin: auto; }
    .modal:focus { outline: none; }
    .modal[open] { animation: modal-entra 460ms var(--ease-expo); }
    .modal::backdrop { background: rgba(3, 8, 11, .6); backdrop-filter: blur(6px); }
    @keyframes modal-entra { from { opacity: 0; transform: translateY(24px) scale(.95); } }
    .caja { --tono: var(--brand); display: grid; grid-template-columns: minmax(0, 1fr); gap: 6px; justify-items: center; padding: 14px 22px 22px; text-align: center; background: radial-gradient(130% 52% at 50% 0, color-mix(in srgb, var(--tono) 42%, transparent), transparent 72%); }
    .caja .icon { width: 22px; height: 22px; }

    /* arriba: qué pasa (con una onda que baila al sonar) y el botón de cerrar, en su propia fila para no tapar la portada */
    .alto { display: flex; align-items: center; justify-content: space-between; width: 100%; margin-right: -8px; }
    .estado { display: inline-flex; align-items: center; gap: 8px; padding: 7px 13px 7px 11px; border-radius: 999px; background: color-mix(in srgb, var(--texto) 9%, transparent); font: 600 .7rem/1 var(--font-code); letter-spacing: .12em; text-transform: uppercase; color: var(--soft); }
    .onda { display: inline-flex; align-items: flex-end; gap: 2px; height: 12px; }
    .onda b { width: 3px; height: 4px; border-radius: 2px; background: var(--tono); }
    .caja--suena .onda b { animation: baila 900ms ease-in-out infinite alternate; }
    .caja--suena .onda b:nth-child(2) { animation-delay: -300ms; } .caja--suena .onda b:nth-child(3) { animation-delay: -600ms; }
    @keyframes baila { from { height: 3px; } to { height: 12px; } }
    .cerrar { display: grid; place-items: center; width: 44px; height: 44px; border: 0; border-radius: 50%; background: transparent; color: var(--texto); cursor: pointer; }
    .cerrar:hover { background: color-mix(in srgb, var(--texto) 10%, transparent); }
    .cerrar:focus-visible { outline: 2px solid var(--acento); outline-offset: -2px; }

    /* la portada se ajusta al alto de la pantalla (así el reproductor cabe sin desplazarse) y respira: algo más pequeña en pausa */
    .portada { position: relative; width: min(100%, 260px, 27svh); aspect-ratio: 1; margin: 8px 0 12px; }
    .portada img { position: absolute; inset: 0; width: 100%; height: 100%; object-fit: cover; }
    .caratula { z-index: 1; border-radius: 26px; box-shadow: 0 16px 40px rgba(0, 0, 0, .45); transform: scale(.9); transition: transform 600ms var(--ease-expo); }
    .caja--suena .caratula { transform: scale(1); }
    .luz { inset: 8% 6% -6%; width: 88%; height: 92%; border-radius: 30px; filter: blur(26px) saturate(1.4); opacity: .65; }

    .datos { display: grid; gap: 3px; width: 100%; }
    h2 { margin: 0; font: 600 1.4rem/1.15 var(--font-text); overflow-wrap: anywhere; }
    .artistas { margin: 0; font-size: 1rem; color: var(--texto); opacity: .92; }
    .album { margin: 0; color: var(--soft); font-size: .84rem; }

    .barra-t { display: grid; gap: 8px; width: 100%; margin-top: 10px; padding: 0 6px; box-sizing: border-box; }
    .progreso { position: relative; width: 100%; height: 6px; border-radius: 99px; background: color-mix(in srgb, var(--texto) 16%, transparent); }
    .progreso i { position: relative; display: block; height: 100%; border-radius: inherit; background: var(--tono); transition: width 1s linear; }
    .progreso i::after { content: ''; position: absolute; right: -6px; top: 50%; width: 12px; height: 12px; border-radius: 50%; background: var(--texto); translate: 0 -50%; box-shadow: 0 2px 6px rgba(0, 0, 0, .4); }
    .tiempos { display: flex; justify-content: space-between; font: 500 .74rem/1 var(--font-code); color: var(--soft); font-variant-numeric: tabular-nums; }

    .controles { display: flex; align-items: center; justify-content: center; gap: 22px; margin: 10px 0 4px; }
    .controles button { display: grid; place-items: center; width: 56px; height: 56px; border: 0; border-radius: 50%; background: color-mix(in srgb, var(--texto) 10%, transparent); color: var(--texto); cursor: pointer; transition: transform 160ms var(--ease-out), background 160ms; }
    .controles button:active { transform: scale(.92); }
    .controles .principal { width: 74px; height: 74px; background: var(--texto); color: var(--fondo); box-shadow: 0 12px 30px color-mix(in srgb, var(--tono) 55%, transparent); }
    .controles .principal .icon { width: 30px; height: 30px; }
    .dispositivo { display: inline-flex; align-items: center; gap: 7px; margin: 10px 0 0; padding: 7px 13px; border-radius: 999px; background: color-mix(in srgb, var(--texto) 7%, transparent); color: var(--soft); font-size: .78rem; }
    .caja .dispositivo .icon { width: 15px; height: 15px; }

    .siguiente { width: 100%; margin-top: 10px; text-align: left; }
    h3 { margin: 0 0 8px 6px; font-size: .7rem; font-weight: 600; letter-spacing: .12em; text-transform: uppercase; color: var(--soft); }
    .cola { margin: 0; padding: 6px; list-style: none; display: grid; gap: 2px; border-radius: 22px; background: color-mix(in srgb, var(--texto) 6%, transparent); }
    .cola li { display: flex; align-items: center; gap: 12px; min-width: 0; padding: 5px 8px; border-radius: 16px; }
    .cola img { flex: none; width: 44px; height: 44px; border-radius: 10px; object-fit: cover; }
    .cola span { display: grid; gap: 1px; min-width: 0; }
    .cola strong { font-size: .9rem; font-weight: 600; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .cola small { color: var(--soft); font-size: .78rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .ir { width: 100%; min-height: 52px; margin-top: 10px; border: 1px solid color-mix(in srgb, var(--tono) 45%, transparent); border-radius: 999px; background: color-mix(in srgb, var(--tono) 18%, transparent); color: var(--texto); font: 700 .95rem/1 var(--font-text); cursor: pointer; }
    /* en pantallas bajas la cola enseña solo dos (y en las muy bajas ninguna) para que todo quepa sin desplazar */
    @media (max-height: 900px) { .cola li:nth-child(n + 3) { display: none; } }
    @media (max-height: 700px) { .siguiente { display: none; } }
    .ir:focus-visible, .controles button:focus-visible { outline: 2px solid var(--acento); outline-offset: 2px; }
    @media (prefers-reduced-motion: reduce) { .mini__disco, .onda b, .modal[open], .mini { animation: none; } .progreso i, .caratula { transition: none; } }
  `,
})
export class MiniMusica {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  readonly oculto = input(false);
  protected readonly modal = viewChild.required<ElementRef<HTMLDialogElement>>('dlg');
  protected readonly ahora = signal<Ahora | null>(null);
  protected readonly cola = signal<Cancion[]>([]);
  protected readonly tono = signal('#1ed760');
  protected readonly abierto = signal(false);
  protected readonly progreso = signal(0);
  protected readonly pct = computed(() => { const c = this.ahora()?.cancion; return c?.duracion ? Math.round(Math.min(100, (this.progreso() / c.duracion) * 100)) : 0; });
  private sinSpotify = false;
  private ultimaId = '';
  private ultimaPortada = '';

  constructor() {
    this.leer();
    // 15 s con la barra sola, 5 s con el reproductor abierto; el progreso avanza solo cada segundo
    let ciclo = 0;
    const t = setInterval(() => {
      ciclo += 1;
      if (!document.hidden && !this.sinSpotify && (this.abierto() || ciclo % 3 === 0)) this.leer();
    }, 5_000);
    const s = setInterval(() => { if (this.abierto() && this.ahora()?.sonando) this.progreso.update((p) => p + 1000); }, 1000);
    const visible = () => { if (!document.hidden && !this.sinSpotify) this.leer(); };
    document.addEventListener('visibilitychange', visible);
    inject(DestroyRef).onDestroy(() => { clearInterval(t); clearInterval(s); document.removeEventListener('visibilitychange', visible); });
  }

  private async leer(): Promise<void> {
    try {
      const r = await firstValueFrom(this.http.get<Ahora>('/api/musica/ahora'));
      this.ahora.set(r);
      this.progreso.set(r.progreso);
      const c = r.cancion;
      if (this.abierto() && c) {
        if ((c.id) !== this.ultimaId) { this.ultimaId = c.id; void this.leerCola(); }
        if (c.imagen && c.imagen !== this.ultimaPortada) { this.ultimaPortada = c.imagen; void this.leerTono(c.imagen); }
      }
    } catch (e) { if ((e as { status?: number }).status === 409) this.sinSpotify = true; this.ahora.set(null); }
  }

  private async leerCola(): Promise<void> {
    try { this.cola.set((await firstValueFrom(this.http.get<Cancion[]>('/api/musica/cola'))).slice(0, 3)); } catch { this.cola.set([]); }
  }

  // el color de la portada tiñe la ventana; se baja el código de la pantalla de Música solo cuando se abre el reproductor
  private async leerTono(url: string): Promise<void> {
    try { const t = await (await import('../musica/musica')).tonoDe(url); if (t) this.tono.set(t); } catch { /* se queda el verde de Spotify */ }
  }

  protected abrir(): void {
    this.abierto.set(true);
    this.ultimaId = ''; this.ultimaPortada = '';
    this.modal().nativeElement.showModal();
    this.modal().nativeElement.focus({ preventScroll: true }); // el foco al diálogo y no al botón de cerrar (que si no sale con el aro de foco al abrir)
    void this.leer();
  }

  protected cerrarFuera(e: MouseEvent): void { if (e.target === this.modal().nativeElement) this.modal().nativeElement.close(); }

  protected irAMusica(): void {
    this.modal().nativeElement.close();
    void this.router.navigateByUrl('/musica');
  }

  protected mmss(ms: number): string {
    const s = Math.max(0, Math.round(ms / 1000));
    return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`;
  }

  protected async control(accion: 'play' | 'pause' | 'next' | 'previous'): Promise<void> {
    if (accion === 'play' || accion === 'pause') this.ahora.update((a) => (a ? { ...a, sonando: accion === 'play' } : a));
    try { await firstValueFrom(this.http.post(`/api/musica/control/${accion}`, null)); } catch { /* sin Premium o sin dispositivo */ }
    setTimeout(() => this.leer(), 600);
  }
}
