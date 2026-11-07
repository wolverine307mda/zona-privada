import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import { firstValueFrom } from 'rxjs';

interface Momento { id: number; nombre: string; icono: string; playlistUri: string; playlistNombre: string; desde: string | null; hasta: string | null; volumen: number | null; mezclar: boolean; }
interface Lista { id: string; nombre: string; uri: string; imagen: string | null; }

// Momentos: un toque pone a sonar la lista de Spotify que elegiste para «Concentración», «Cocina», «Entrenar», «Dormir» o «Fiesta» (mezclada y con su volumen).
// Si es la hora de uno (según la franja que le pongas) se destaca y te lo sugiere. Hace falta Spotify Premium y un aparato con Spotify abierto.
@Component({
  selector: 'app-momentos-musica',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrls: ['../gastos/comun.css', '../gastos/dialogos.css'],
  template: `
    <section class="caja mom">
      <header class="caja__cab"><h3>Momentos</h3><span class="sub">un toque, tu lista de siempre</span></header>
      @if (sugerido(); as s) { <p class="sug">🕒 Son las {{ hora() }}: ¿<button type="button" class="enlace" (click)="poner(s)">{{ s.icono }} {{ s.nombre }}</button>?</p> }
      <div class="chips">
        @for (m of momentos(); track m.id) {
          <span class="chip" [class.chip--sug]="m.id === sugeridoId()">
            <button type="button" class="chip__poner" (click)="poner(m)" [disabled]="ocupado()" [attr.aria-label]="'Poner ' + m.nombre">{{ m.icono }} {{ m.nombre }}</button>
            <button type="button" class="chip__ed" (click)="editar(m)" [attr.aria-label]="'Editar ' + m.nombre">⋯</button>
          </span>
        }
      </div>
      @if (aviso()) { <p class="aviso" role="status">{{ aviso() }}</p> }
    </section>

    <dialog class="dlg" #dlg (click)="fuera($event)" aria-labelledby="dlg-mom-t">
      <div class="dlg__caja">
        <header class="dlg__cabeza"><h2 id="dlg-mom-t">{{ edit()?.icono }} {{ edit()?.nombre }}</h2>
          <button class="dlg__x" type="button" (click)="dlg.close()" aria-label="Cerrar"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6l12 12M18 6 6 18" /></svg></button></header>
        <label class="campo"><span class="campo__etiqueta">Lista de Spotify</span>
          @if (listas().length) {
            <select class="campo__input" (change)="elegirLista($any($event.target).value)">
              <option value="">Elige una de tus listas…</option>
              @for (l of listas(); track l.id) { <option [value]="l.uri" [selected]="l.uri === uri()">{{ l.nombre }}</option> }
            </select>
          }
          <input class="campo__input" type="text" placeholder="…o pega su dirección (open.spotify.com/playlist/…)" [value]="pegada()" (input)="pegada.set($any($event.target).value)">
        </label>
        @if (avisoListas()) { <p class="sub">{{ avisoListas() }}</p> }
        <div class="fila2">
          <label class="campo"><span class="campo__etiqueta">Desde</span><input class="campo__input" type="time" [value]="desde()" (input)="desde.set($any($event.target).value)"></label>
          <label class="campo"><span class="campo__etiqueta">Hasta</span><input class="campo__input" type="time" [value]="hasta()" (input)="hasta.set($any($event.target).value)"></label>
        </div>
        <label class="campo"><span class="campo__etiqueta">Volumen: {{ volumen() === null ? 'el que haya' : volumen() + ' %' }}</span>
          <input type="range" min="0" max="100" [value]="volumen() ?? 50" (input)="volumen.set(+$any($event.target).value)"></label>
        <label class="mezcla"><input type="checkbox" [checked]="mezclar()" (change)="mezclar.set($any($event.target).checked)"> Mezclar la lista</label>
        @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
        <footer class="dlg__pie"><button class="button" type="button" (click)="guardar()">Guardar</button></footer>
      </div>
    </dialog>
  `,
  styles: `
    :host { display: block; }
    .mom { display: flex; flex-direction: column; gap: 10px; }
    .sub { margin: 0; color: var(--soft); font-size: .85rem; }
    .sug { margin: 0; padding: 8px 12px; border-radius: 12px; background: var(--tarjeta-2); font-size: .9rem; }
    .chips { display: flex; flex-wrap: wrap; gap: 8px; }
    .chip { display: inline-flex; align-items: center; border: 1px solid var(--vidrio-borde); border-radius: 999px; background: var(--vidrio); overflow: hidden; }
    .chip--sug { border-color: var(--brand); box-shadow: 0 0 0 2px color-mix(in srgb, var(--brand) 25%, transparent); }
    .chip button { border: 0; background: none; color: var(--texto); font: inherit; cursor: pointer; min-height: 44px; }
    .chip__poner { padding: 0 14px; }
    .chip__ed { padding: 0 12px; color: var(--soft) !important; border-left: 1px solid var(--vidrio-borde) !important; }
    .aviso { margin: 0; padding: 8px 12px; border-radius: 10px; background: var(--tarjeta-2); font-size: .88rem; }
    .enlace { background: none; border: 0; color: var(--brand); font: inherit; cursor: pointer; }
    .mezcla { display: flex; align-items: center; gap: 10px; }
  `,
})
export class MomentosMusica {
  private readonly http = inject(HttpClient);
  protected readonly dlg = viewChild.required<ElementRef<HTMLDialogElement>>('dlg');
  protected readonly momentos = signal<Momento[]>([]);
  protected readonly sugeridoId = signal<number | null>(null);
  protected readonly sugerido = computed(() => this.momentos().find((m) => m.id === this.sugeridoId()) ?? null);
  protected readonly aviso = signal('');
  protected readonly ocupado = signal(false);
  protected readonly edit = signal<Momento | null>(null);
  protected readonly listas = signal<Lista[]>([]);
  protected readonly avisoListas = signal('');
  protected readonly uri = signal('');
  protected readonly pegada = signal('');
  protected readonly desde = signal('');
  protected readonly hasta = signal('');
  protected readonly volumen = signal<number | null>(null);
  protected readonly mezclar = signal(true);
  protected readonly error = signal('');

  constructor() { void this.cargar(); }

  protected hora(): string { return new Date().toLocaleTimeString('es-ES', { hour: '2-digit', minute: '2-digit' }); }

  private async cargar(): Promise<void> {
    try { const r = await firstValueFrom(this.http.get<{ momentos: Momento[]; sugerido: number | null }>('/api/musica/momentos')); this.momentos.set(r.momentos); this.sugeridoId.set(r.sugerido); } catch { /* sin momentos */ }
  }

  protected async poner(m: Momento): Promise<void> {
    if (!m.playlistUri) { this.editar(m); this.aviso.set(`«${m.nombre}» aún no tiene lista: elige una.`); return; }
    this.ocupado.set(true); this.aviso.set('');
    try { this.aviso.set((await firstValueFrom(this.http.post<{ mensaje: string }>(`/api/musica/momento/${m.id}`, null))).mensaje); }
    catch (e) { this.aviso.set((e as HttpErrorResponse).error?.detalle ?? 'No se pudo poner.'); }
    finally { this.ocupado.set(false); }
  }

  protected async editar(m: Momento): Promise<void> {
    this.edit.set(m); this.uri.set(m.playlistUri); this.pegada.set(''); this.desde.set(m.desde ?? ''); this.hasta.set(m.hasta ?? ''); this.volumen.set(m.volumen); this.mezclar.set(m.mezclar); this.error.set('');
    this.dlg().nativeElement.showModal();
    if (this.listas().length === 0) {
      try { this.listas.set(await firstValueFrom(this.http.get<Lista[]>('/api/musica/playlists'))); this.avisoListas.set(''); }
      catch (e) { this.avisoListas.set((e as HttpErrorResponse).error?.detail ?? 'No pude leer tus listas: pega la dirección a mano.'); }
    }
  }

  protected elegirLista(uri: string): void { this.uri.set(uri); this.pegada.set(''); }

  protected async guardar(): Promise<void> {
    const m = this.edit(); if (!m) return;
    const nombreLista = this.pegada() ? '' : this.listas().find((l) => l.uri === this.uri())?.nombre ?? m.playlistNombre;
    try {
      await firstValueFrom(this.http.put(`/api/musica/momentos/${m.id}`, {
        playlistUri: this.pegada().trim() || this.uri(), playlistNombre: nombreLista, desde: this.desde(), hasta: this.hasta(),
        volumen: this.volumen(), quitarVolumen: this.volumen() === null, mezclar: this.mezclar(),
      }));
      this.dlg().nativeElement.close();
      await this.cargar();
    } catch (e) {
      const errores = (e as HttpErrorResponse).error?.errors as Record<string, string[]> | undefined;
      this.error.set(errores ? Object.values(errores)[0]?.[0] ?? 'Revisa los datos.' : 'No se pudo guardar.');
    }
  }

  protected fuera(e: MouseEvent): void { const d = e.target as HTMLElement; if (d.tagName === 'DIALOG') (d as HTMLDialogElement).close(); }
}
