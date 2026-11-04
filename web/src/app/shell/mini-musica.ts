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

    <dialog class="modal" #dlg aria-labelledby="mm-titulo" (click)="cerrarFuera($event)" (close)="abierto.set(false)">
      @if (ahora(); as a) {
        @if (a.cancion; as c) {
          <div class="caja" [style.--tono]="tono()">
            <button class="cerrar" type="button" (click)="modal().nativeElement.close()" aria-label="Cerrar el reproductor">
              <svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6l12 12M18 6 6 18" /></svg>
            </button>
            <div class="portada" [class.portada--suena]="a.sonando">
              <span class="vinilo" aria-hidden="true"></span>
              @if (c.imagen) { <img [src]="c.imagen" [alt]="'Portada de ' + (c.album ?? c.nombre)"> }
            </div>
            <div class="datos">
              <h2 id="mm-titulo">{{ c.nombre }}</h2>
              <p class="artistas">{{ c.artistas }}</p>
              @if (c.album) { <p class="album">{{ c.album }}</p> }
            </div>
            <div class="progreso" role="progressbar" aria-label="Progreso de la canción" [attr.aria-valuenow]="pct()" aria-valuemin="0" aria-valuemax="100"><i [style.width.%]="pct()"></i></div>
            <div class="tiempos"><span>{{ mmss(progreso()) }}</span><span>{{ mmss(c.duracion) }}</span></div>
            <div class="controles">
              <button type="button" (click)="control('previous')" aria-label="Anterior"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M18 6v12l-9-6zM6 6v12" /></svg></button>
              <button class="principal" type="button" (click)="control(a.sonando ? 'pause' : 'play')" [attr.aria-label]="a.sonando ? 'Pausa' : 'Reproducir'">
                @if (a.sonando) { <svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M8 5v14M16 5v14" /></svg> }
                @else { <svg class="icon relleno" viewBox="0 0 24 24" aria-hidden="true"><path d="M7 5l12 7-12 7z" /></svg> }
              </button>
              <button type="button" (click)="control('next')" aria-label="Siguiente"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6v12l9-6zM18 6v12" /></svg></button>
            </div>
            @if (a.dispositivo) { <p class="dispositivo">Sonando en {{ a.dispositivo }}{{ a.volumen != null ? ' · volumen ' + a.volumen + ' %' : '' }}</p> }
            @if (cola().length) {
              <h3>A continuación</h3>
              <ol class="cola">
                @for (q of cola(); track q.id) {
                  <li>@if (q.imagen) { <img [src]="q.imagen" alt="" width="36" height="36"> }<span><strong>{{ q.nombre }}</strong><small>{{ q.artistas }}</small></span></li>
                }
              </ol>
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
    @media (max-width: 1180px) { .mini__texto, .mini__sig { display: none; } .mini { padding-right: 4px; } }

    /* la ventana del reproductor */
    .modal { width: min(400px, calc(100vw - 24px)); max-height: calc(100svh - 24px); padding: 0; border: 1px solid var(--vidrio-borde); border-radius: 30px; background: var(--tarjeta); color: var(--texto); box-shadow: 0 30px 90px rgba(0, 0, 0, .5); overflow: auto; }
    .modal[open] { animation: modal-entra 460ms var(--ease-expo); }
    .modal::backdrop { background: rgba(3, 8, 11, .6); backdrop-filter: blur(6px); }
    @keyframes modal-entra { from { opacity: 0; transform: translateY(24px) scale(.95); } }
    .caja { --tono: var(--brand); position: relative; display: grid; gap: 6px; justify-items: center; padding: 22px 22px 20px; text-align: center; background: radial-gradient(120% 60% at 50% 0, color-mix(in srgb, var(--tono) 38%, transparent), transparent 70%); }
    .caja .icon { width: 22px; height: 22px; }
    .cerrar { position: absolute; right: 14px; top: 14px; display: grid; place-items: center; width: 40px; height: 40px; border: 0; border-radius: 12px; background: color-mix(in srgb, var(--texto) 10%, transparent); color: var(--texto); cursor: pointer; z-index: 2; }
    .portada { position: relative; width: min(64vw, 250px); aspect-ratio: 1; margin: 18px 0 14px; }
    .portada img { position: relative; z-index: 1; width: 100%; height: 100%; border-radius: 22px; object-fit: cover; box-shadow: 0 18px 44px color-mix(in srgb, var(--tono) 55%, rgba(0, 0, 0, .5)); }
    .vinilo { position: absolute; top: 6%; left: 22%; width: 90%; aspect-ratio: 1; border-radius: 50%; background: radial-gradient(circle, color-mix(in srgb, var(--tono) 70%, #000) 0 18%, #0a0a0a 19% 22%, transparent 23%), repeating-radial-gradient(circle, #151515 0 2px, #0b0b0b 3px 4px); box-shadow: 0 8px 24px rgba(0, 0, 0, .5); transition: transform 700ms var(--ease-expo); animation: gira 4s linear infinite paused; }
    .portada--suena .vinilo { transform: translateX(14%); animation-play-state: running; }
    .datos { display: grid; gap: 2px; max-width: 100%; }
    h2 { margin: 0; font: 500 1.25rem/1.2 var(--font-text); overflow-wrap: anywhere; }
    .artistas { margin: 0; color: var(--texto); font-size: .95rem; opacity: .9; }
    .album { margin: 0; color: var(--soft); font-size: .82rem; }
    .progreso { width: 100%; height: 6px; margin-top: 12px; border-radius: 99px; background: color-mix(in srgb, var(--texto) 14%, transparent); overflow: hidden; }
    .progreso i { display: block; height: 100%; border-radius: inherit; background: var(--tono); transition: width 1s linear; }
    .tiempos { display: flex; justify-content: space-between; width: 100%; font: 500 .74rem/1 var(--font-code); color: var(--soft); font-variant-numeric: tabular-nums; }
    .controles { display: flex; align-items: center; justify-content: center; gap: 18px; margin: 8px 0 2px; }
    .controles button { display: grid; place-items: center; width: 52px; height: 52px; border: 0; border-radius: 50%; background: color-mix(in srgb, var(--texto) 9%, transparent); color: var(--texto); cursor: pointer; transition: transform 160ms var(--ease-out), background 160ms; }
    .controles button:active { transform: scale(.92); }
    .controles .principal { width: 66px; height: 66px; background: var(--tono); color: #fff; box-shadow: 0 10px 28px color-mix(in srgb, var(--tono) 55%, transparent); }
    .controles .principal .icon { width: 28px; height: 28px; }
    .dispositivo { margin: 4px 0 0; color: var(--soft); font-size: .78rem; }
    h3 { align-self: start; margin: 14px 0 0; font-size: .72rem; font-weight: 600; letter-spacing: .1em; text-transform: uppercase; color: var(--soft); }
    .cola { width: 100%; margin: 0; padding: 0; list-style: none; display: grid; gap: 6px; text-align: left; }
    .cola li { display: flex; align-items: center; gap: 10px; min-width: 0; }
    .cola img { flex: none; border-radius: 8px; object-fit: cover; }
    .cola span { display: grid; min-width: 0; }
    .cola strong { font-size: .86rem; font-weight: 600; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .cola small { color: var(--soft); font-size: .76rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .ir { width: 100%; min-height: 48px; margin-top: 14px; border: 1px solid var(--vidrio-borde); border-radius: 999px; background: transparent; color: var(--acento); font: 700 .92rem/1 var(--font-text); cursor: pointer; }
    @media (prefers-reduced-motion: reduce) { .mini__disco, .vinilo, .modal[open], .mini { animation: none; } .progreso i, .vinilo { transition: none; } }
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
