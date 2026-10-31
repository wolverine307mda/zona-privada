import { HttpClient } from '@angular/common/http';
import { Component, DestroyRef, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';

interface Ahora { sonando: boolean; cancion: { nombre: string; artistas: string; imagen: string | null } | null; }

// Lo que suena, en la barra de arriba: portada que gira, título y pausa / siguiente desde cualquier sección.
// Pregunta cada 15 s solo con la pestaña a la vista; si Spotify no está enlazado, no vuelve a preguntar.
@Component({
  selector: 'app-mini-musica',
  imports: [RouterLink],
  template: `
    @if (!oculto() && ahora()?.cancion; as c) {
      <div class="mini" [class.mini--parado]="!ahora()!.sonando">
        <a class="mini__disco" routerLink="/musica" [attr.aria-label]="'Sonando: ' + c.nombre + '. Abrir Música'">
          @if (c.imagen) { <img [src]="c.imagen" alt="" width="34" height="34"> }
        </a>
        <a class="mini__texto" routerLink="/musica"><strong>{{ c.nombre }}</strong><span>{{ c.artistas }}</span></a>
        <button type="button" (click)="control(ahora()!.sonando ? 'pause' : 'play')" [attr.aria-label]="ahora()!.sonando ? 'Pausa' : 'Reproducir'">
          @if (ahora()!.sonando) { <svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M8 5v14M16 5v14" /></svg> }
          @else { <svg class="icon relleno" viewBox="0 0 24 24" aria-hidden="true"><path d="M7 5l12 7-12 7z" /></svg> }
        </button>
        <button class="mini__sig" type="button" (click)="control('next')" aria-label="Siguiente"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6v12l9-6zM18 6v12" /></svg></button>
      </div>
    }
  `,
  styles: `
    :host { display: contents; }
    .mini { display: flex; align-items: center; gap: 8px; height: 42px; padding: 4px 6px 4px 4px; max-width: 280px; border-radius: 999px; border: 1px solid var(--vidrio-borde); background: var(--vidrio); backdrop-filter: var(--desenfoque); animation: entra 500ms var(--ease-expo); }
    @keyframes entra { from { opacity: 0; transform: translateY(-6px) scale(0.96); } }
    .mini__disco { flex: none; width: 34px; height: 34px; border-radius: 50%; overflow: hidden; background: #111; box-shadow: 0 0 0 2px color-mix(in srgb, #1ed760 70%, transparent); animation: gira 6s linear infinite; }
    .mini--parado .mini__disco { animation-play-state: paused; box-shadow: 0 0 0 2px var(--linea); }
    .mini__disco img { width: 100%; height: 100%; object-fit: cover; mask: radial-gradient(circle, transparent 0 3px, #000 3.5px); }
    @keyframes gira { to { rotate: 360deg; } }
    .mini__texto { display: flex; flex-direction: column; min-width: 0; line-height: 1.15; color: var(--texto); text-decoration: none; }
    .mini__texto strong { font-size: 12.5px; font-weight: 600; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .mini__texto span { font-size: 11px; color: var(--soft); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    button { flex: none; display: grid; place-items: center; width: 32px; height: 32px; border-radius: 50%; border: 0; background: transparent; color: var(--texto); cursor: pointer; }
    button .icon { width: 16px; height: 16px; }
    .relleno { fill: currentColor; }
    @media (hover: hover) { button:hover { background: var(--tarjeta-2); } }
    @media (max-width: 1180px) { .mini__texto, .mini__sig { display: none; } .mini { padding-right: 4px; } }
    @media (prefers-reduced-motion: reduce) { .mini__disco { animation: none; } }
  `,
})
export class MiniMusica {
  private readonly http = inject(HttpClient);
  readonly oculto = input(false);
  protected readonly ahora = signal<Ahora | null>(null);
  private sinSpotify = false;

  constructor() {
    this.leer();
    const t = setInterval(() => { if (!document.hidden && !this.sinSpotify) this.leer(); }, 15_000);
    const visible = () => { if (!document.hidden && !this.sinSpotify) this.leer(); };
    document.addEventListener('visibilitychange', visible);
    inject(DestroyRef).onDestroy(() => { clearInterval(t); document.removeEventListener('visibilitychange', visible); });
  }

  private async leer(): Promise<void> {
    try { this.ahora.set(await firstValueFrom(this.http.get<Ahora>('/api/musica/ahora'))); }
    catch (e) { if ((e as { status?: number }).status === 409) this.sinSpotify = true; this.ahora.set(null); }
  }

  protected async control(accion: 'play' | 'pause' | 'next'): Promise<void> {
    if (accion !== 'next') this.ahora.update((a) => (a ? { ...a, sonando: accion === 'play' } : a));
    try { await firstValueFrom(this.http.post(`/api/musica/control/${accion}`, null)); } catch { /* sin Premium o sin dispositivo */ }
    setTimeout(() => this.leer(), 600);
  }
}
