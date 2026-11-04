import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';

interface Entidad { id: string; dominio: string; nombre: string; estado: string; brillo: number | null; }
interface Estado { homeAssistant: boolean; }

const ICONOS: Record<string, string> = { light: '💡', switch: '🔌', fan: '🌀', input_boolean: '🎚️', scene: '🎬' };

// La casa conectada: luces, enchufes, ventiladores y escenas de Home Assistant, con un toque. Se configura en Seguridad → Claves (la dirección y el token van cifrados).
@Component({
  selector: 'app-hogar',
  imports: [RouterLink],
  styleUrls: ['../gastos/comun.css', './modulos.css'],
  template: `
    @if (estado(); as e) {
      <section class="caja">
        <header class="caja__cab"><h3>Casa conectada</h3><span class="sub">luces, enchufes y escenas de Home Assistant</span></header>
        @if (!e.homeAssistant) {
          <p class="nada">Conecta Home Assistant para manejar tus luces y enchufes desde aquí: añade su dirección y un token en <a routerLink="/seguridad">Seguridad → Claves</a>.</p>
        } @else {
          @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
          <ul class="cosas">
            @for (c of entidades(); track c.id) {
              <li class="cosa" [class.cosa--on]="c.estado === 'on'">
                <span class="cosa__icono" aria-hidden="true">{{ icono(c.dominio) }}</span>
                <span class="cosa__texto"><strong>{{ c.nombre }}</strong><small>{{ c.dominio === 'scene' ? 'escena' : c.estado === 'on' ? 'encendido' + (c.brillo !== null ? ' · ' + c.brillo + ' %' : '') : c.estado === 'off' ? 'apagado' : c.estado }}</small></span>
                @if (c.dominio === 'scene') { <button class="enlace" type="button" (click)="llamar(c, 'activar')" [disabled]="ocupado() === c.id">Activar</button> }
                @else { <button class="interruptor-pastilla" type="button" role="switch" [attr.aria-checked]="c.estado === 'on'" [attr.aria-label]="c.nombre" (click)="llamar(c, 'alternar')" [disabled]="ocupado() === c.id"><i></i></button> }
              </li>
            } @empty { <li class="nada">{{ cargado() ? 'Home Assistant no tiene luces, enchufes ni escenas que enseñar.' : 'Cargando…' }}</li> }
          </ul>
        }
      </section>
    }
  `,
  styles: `
    .cosas { list-style: none; margin: 0; padding: 0; display: grid; gap: 8px; }
    .cosa { display: grid; grid-template-columns: 40px minmax(0, 1fr) auto; align-items: center; gap: 12px; padding: 10px 12px; border-radius: 14px; border: 1px solid var(--vidrio-borde); background: color-mix(in srgb, var(--texto) 4%, transparent); transition: border-color 200ms, background 200ms; }
    .cosa--on { border-color: color-mix(in srgb, var(--brand) 50%, transparent); background: color-mix(in srgb, var(--brand) 8%, transparent); }
    .cosa__icono { display: grid; place-items: center; width: 40px; height: 40px; border-radius: 12px; background: color-mix(in srgb, var(--texto) 8%, transparent); font-size: 1.2rem; filter: grayscale(1); opacity: .7; transition: filter 200ms, opacity 200ms, background 200ms; }
    .cosa--on .cosa__icono { filter: none; opacity: 1; background: color-mix(in srgb, var(--brand) 22%, transparent); }
    .cosa__texto { display: grid; min-width: 0; }
    .cosa__texto strong { font-weight: 500; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .cosa__texto small { color: var(--soft); font-size: .78rem; }
    .interruptor-pastilla { position: relative; width: 52px; height: 30px; border-radius: 999px; border: 1px solid var(--vidrio-borde); background: color-mix(in srgb, var(--texto) 12%, transparent); cursor: pointer; transition: background 220ms, border-color 220ms; }
    .interruptor-pastilla i { position: absolute; top: 3px; left: 3px; width: 22px; height: 22px; border-radius: 50%; background: var(--texto); transition: transform 280ms var(--ease-expo, ease), background 220ms; }
    .interruptor-pastilla[aria-checked='true'] { background: var(--brand); border-color: var(--brand); }
    .interruptor-pastilla[aria-checked='true'] i { transform: translateX(22px); background: var(--on-brand); }
    .interruptor-pastilla:disabled { opacity: .6; }
    @media (prefers-reduced-motion: reduce) { .interruptor-pastilla, .interruptor-pastilla i, .cosa, .cosa__icono { transition: none; } }
  `,
})
export class Hogar implements OnInit {
  private readonly http = inject(HttpClient);
  protected readonly estado = signal<Estado | null>(null);
  protected readonly entidades = signal<Entidad[]>([]);
  protected readonly cargado = signal(false);
  protected readonly error = signal('');
  protected readonly ocupado = signal<string | null>(null);

  constructor() {
    const t = setInterval(() => { if (!document.hidden && this.estado()?.homeAssistant) void this.leer(); }, 20_000);
    inject(DestroyRef).onDestroy(() => clearInterval(t));
  }

  ngOnInit(): void { void this.empezar(); }

  protected icono(d: string): string { return ICONOS[d] ?? '🏠'; }

  private async empezar(): Promise<void> {
    try { this.estado.set(await firstValueFrom(this.http.get<Estado>('/api/hogar/estado'))); } catch { return; }
    if (this.estado()?.homeAssistant) await this.leer();
  }

  private async leer(): Promise<void> {
    try { this.entidades.set(await firstValueFrom(this.http.get<Entidad[]>('/api/hogar/entidades'))); this.error.set(''); }
    catch (e) { this.error.set((e as HttpErrorResponse).error?.detalle ?? 'No se pudo leer Home Assistant.'); }
    this.cargado.set(true);
  }

  protected async llamar(c: Entidad, accion: 'alternar' | 'activar'): Promise<void> {
    this.ocupado.set(c.id); this.error.set('');
    // el interruptor se mueve al momento; si Home Assistant no contesta, se vuelve a leer y se deshace solo
    if (accion === 'alternar') this.entidades.update((l) => l.map((x) => (x.id === c.id ? { ...x, estado: x.estado === 'on' ? 'off' : 'on' } : x)));
    try { await firstValueFrom(this.http.post(`/api/hogar/entidades/${c.id}/${accion}`, null)); setTimeout(() => void this.leer(), 700); }
    catch (e) { this.error.set((e as HttpErrorResponse).error?.detalle ?? 'No se pudo.'); await this.leer(); }
    finally { this.ocupado.set(null); }
  }
}
