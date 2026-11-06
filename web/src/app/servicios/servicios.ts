import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { EstadoError, EstadoVacio, Esqueleto } from '../ui';

interface Servicio {
  id: number; nombre: string; tipo: 'http' | 'tcp'; direccion: string; activo: boolean; estado: '?' | 'ok' | 'caido';
  desde: string | null; ultima: string | null; ms: number | null; ultimoError: string | null; caidas: number; ultimaCaida: string | null;
}

// Páginas y servicios que vigilas: tu web, el router, el NAS, el servidor de un amigo. Se comprueban cada 2 minutos; tras dos fallos seguidos te avisa
// (app y Telegram) y, al volver, dice cuánto estuvo caído. «Web» responde si contesta con algo por debajo de 500; «Puerto» si el host:puerto acepta conexiones.
@Component({
  selector: 'app-servicios',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [EstadoError, EstadoVacio, Esqueleto],
  styleUrls: ['../gastos/comun.css', '../gastos/dialogos.css'],
  template: `
    <header class="cab">
      <p class="cab__sub" aria-live="polite">{{ resumen() }}</p>
      <button class="button" type="button" (click)="nuevo()">Vigilar algo</button>
    </header>

    @if (fallo()) { <app-estado-error titulo="No pude cargar los servicios" (reintentar)="cargar()" /> }
    @else if (lista() === null) { <section class="caja"><app-esqueleto forma="bloque" alto="64px" /><app-esqueleto forma="bloque" alto="64px" /></section> }
    @else if (lista()!.length === 0) {
      <app-estado-vacio icono="M3 12h4l3-8 4 16 3-8h4" titulo="Aún no vigilas nada" texto="Añade tu web, el router o el NAS y te aviso cuando caigan y cuando vuelvan.">
        <button class="button" type="button" (click)="nuevo()">Vigilar el primero</button>
      </app-estado-vacio>
    } @else {
      <ul class="lista" aria-label="Servicios vigilados">
        @for (s of lista()!; track s.id) {
          <li class="serv" [attr.data-e]="s.activo ? s.estado : 'off'">
            <span class="punto" aria-hidden="true"></span>
            <button type="button" class="serv__texto" (click)="editar(s)" [attr.aria-label]="'Editar ' + s.nombre">
              <strong>{{ s.nombre }}</strong>
              <small>{{ s.direccion }}</small>
              <small>{{ detalle(s) }}</small>
            </button>
            <button type="button" class="button button--suave" [disabled]="probando() === s.id" (click)="comprobar(s)">{{ probando() === s.id ? '…' : 'Comprobar' }}</button>
          </li>
        }
      </ul>
    }

    <dialog class="dlg" #dialogo (click)="fuera($event)" (close)="cerrado()" aria-labelledby="dlg-serv">
      <form class="dlg__caja" (submit)="guardar($event)" novalidate>
        <header class="dlg__cabeza">
          <h2 id="dlg-serv">{{ editando() ? 'Editar' : 'Vigilar algo' }}</h2>
          <button class="dlg__x" type="button" (click)="cerrar()" aria-label="Cerrar"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6l12 12M18 6 6 18" /></svg></button>
        </header>
        <label class="campo"><span class="campo__etiqueta">Nombre</span>
          <input class="campo__input" type="text" maxlength="60" placeholder="Mi web, Router, NAS…" [value]="nombre()" (input)="nombre.set(valor($event))"></label>
        <label class="campo"><span class="campo__etiqueta">Qué comprobar</span>
          <select class="campo__input" [value]="tipo()" (change)="tipo.set(valor($event))">
            <option value="http">Una página web</option>
            <option value="tcp">Un puerto (host:puerto)</option>
          </select></label>
        <label class="campo"><span class="campo__etiqueta">{{ tipo() === 'tcp' ? 'Host y puerto' : 'Dirección' }}</span>
          <input class="campo__input" [type]="tipo() === 'tcp' ? 'text' : 'url'" maxlength="300" autocapitalize="off" spellcheck="false" [placeholder]="tipo() === 'tcp' ? '192.168.1.1:80' : 'https://…'" [value]="direccion()" (input)="direccion.set(valor($event))"></label>
        <label class="campo campo--linea"><input type="checkbox" [checked]="activo()" (change)="activo.set($any($event.target).checked)"> <span>Vigilar ahora</span></label>
        @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
        <footer class="dlg__pie">
          @if (editando()) { <button type="button" class="boton-borrar" (click)="borrar()">{{ confirmarBorrado() ? 'Seguro: borrar' : 'Borrar' }}</button> }
          <button class="button" type="submit" [attr.aria-busy]="guardando() || null" [disabled]="guardando()">Guardar</button>
        </footer>
      </form>
    </dialog>
  `,
  styles: `
    .cab__sub { margin: 0; font: var(--peso-ligero) 1.3rem/1.2 var(--font-text); }
    .cab { display: flex; align-items: center; justify-content: space-between; gap: 12px; flex-wrap: wrap; margin-bottom: var(--esp-4); }
    .lista { list-style: none; margin: 0; padding: 0; display: grid; gap: 8px; }
    .serv { display: grid; grid-template-columns: auto minmax(0, 1fr) auto; align-items: center; gap: 14px; padding: 12px 16px; border-radius: var(--radio-l); border: 1px solid var(--vidrio-borde); background: var(--vidrio); }
    .serv[data-e='caido'] { border-color: color-mix(in srgb, var(--bad) 60%, transparent); }
    .serv[data-e='off'] { opacity: .55; }
    .punto { width: 12px; height: 12px; border-radius: 50%; background: var(--soft); }
    .serv[data-e='ok'] .punto { background: var(--ok); box-shadow: 0 0 0 4px color-mix(in srgb, var(--ok) 25%, transparent); }
    .serv[data-e='caido'] .punto { background: var(--bad); box-shadow: 0 0 0 4px color-mix(in srgb, var(--bad) 28%, transparent); }
    .serv__texto { display: grid; gap: 2px; min-width: 0; padding: 0; border: 0; background: none; color: inherit; font: inherit; text-align: left; cursor: pointer; }
    .serv__texto strong { font-weight: 500; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .serv__texto small { color: var(--soft); font-size: .78rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .campo--linea { display: flex; align-items: center; gap: 10px; }
  `,
})
export class Servicios {
  private readonly http = inject(HttpClient);
  private readonly dialogo = viewChild.required<ElementRef<HTMLDialogElement>>('dialogo');

  protected readonly lista = signal<Servicio[] | null>(null);
  protected readonly fallo = signal(false);
  protected readonly probando = signal<number | null>(null);

  protected readonly editando = signal<Servicio | null>(null);
  protected readonly nombre = signal('');
  protected readonly tipo = signal('http');
  protected readonly direccion = signal('');
  protected readonly activo = signal(true);
  protected readonly error = signal('');
  protected readonly guardando = signal(false);
  protected readonly confirmarBorrado = signal(false);

  protected readonly resumen = computed(() => {
    const l = this.lista();
    if (!l) return 'Que siga todo en pie';
    const activos = l.filter((s) => s.activo);
    if (activos.length === 0) return l.length ? 'Todo en pausa' : 'No vigilas nada todavía';
    const caidos = activos.filter((s) => s.estado === 'caido').length;
    return caidos ? `${caidos} caído${caidos === 1 ? '' : 's'} de ${activos.length}` : `Todo en pie (${activos.length})`;
  });

  constructor() { void this.cargar(); }

  protected async cargar(): Promise<void> {
    this.fallo.set(false);
    try { this.lista.set(await firstValueFrom(this.http.get<Servicio[]>('/api/servicios'))); } catch { this.fallo.set(true); }
  }

  protected detalle(s: Servicio): string {
    if (!s.activo) return 'En pausa';
    if (s.estado === '?' || !s.ultima) return 'Aún sin comprobar';
    const hora = (iso: string) => new Date(iso).toLocaleString('es-ES', { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' });
    if (s.estado === 'caido') return `Caído desde ${hora(s.desde!)}${s.ultimoError ? ' · ' + s.ultimoError : ''}`;
    return `En pie${s.ms !== null ? ' · ' + s.ms + ' ms' : ''} · visto ${hora(s.ultima)}${s.caidas ? ' · ' + s.caidas + (s.caidas === 1 ? ' caída' : ' caídas') : ''}`;
  }

  protected async comprobar(s: Servicio): Promise<void> {
    if (this.probando() !== null) return;
    this.probando.set(s.id);
    try {
      const nuevo = await firstValueFrom(this.http.post<Servicio>(`/api/servicios/${s.id}/comprobar`, {}));
      this.lista.update((l) => l?.map((x) => (x.id === s.id ? nuevo : x)) ?? null);
    } catch { void this.cargar(); } finally { this.probando.set(null); }
  }

  // ---- diálogo ----
  protected nuevo(): void { this.abrir(null); }
  protected editar(s: Servicio): void { this.abrir(s); }

  private abrir(s: Servicio | null): void {
    this.editando.set(s);
    this.nombre.set(s?.nombre ?? ''); this.tipo.set(s?.tipo ?? 'http'); this.direccion.set(s?.direccion ?? ''); this.activo.set(s?.activo ?? true);
    this.error.set(''); this.confirmarBorrado.set(false);
    this.dialogo().nativeElement.showModal();
  }

  protected cerrar(): void { this.dialogo().nativeElement.close(); }
  protected cerrado(): void { this.editando.set(null); }
  protected fuera(e: MouseEvent): void { if (e.target === this.dialogo().nativeElement) this.cerrar(); }
  protected valor(e: Event): string { return (e.target as HTMLInputElement).value; }

  protected async guardar(ev: Event): Promise<void> {
    ev.preventDefault();
    if (this.guardando()) return;
    if (!this.nombre().trim()) { this.error.set('Ponle un nombre.'); return; }
    let direccion = this.direccion().trim();
    if (!direccion) { this.error.set('Pon la dirección.'); return; }
    if (this.tipo() === 'http' && !/^[a-z][a-z0-9+.-]*:/i.test(direccion)) direccion = 'https://' + direccion;
    this.guardando.set(true); this.error.set('');
    const cuerpo = { nombre: this.nombre().trim(), tipo: this.tipo(), direccion, activo: this.activo() };
    try {
      const e = this.editando();
      if (e) await firstValueFrom(this.http.put(`/api/servicios/${e.id}`, cuerpo));
      else await firstValueFrom(this.http.post('/api/servicios', cuerpo));
      this.cerrar();
      await this.cargar();
    } catch (x) { this.error.set(this.mensaje(x)); } finally { this.guardando.set(false); }
  }

  protected async borrar(): Promise<void> {
    const e = this.editando(); if (!e) return;
    if (!this.confirmarBorrado()) { this.confirmarBorrado.set(true); return; }
    try { await firstValueFrom(this.http.delete(`/api/servicios/${e.id}`)); this.cerrar(); await this.cargar(); } catch (x) { this.error.set(this.mensaje(x)); }
  }

  private mensaje(x: unknown): string {
    const e = x as HttpErrorResponse;
    const errores = e.error?.errors as Record<string, string[]> | undefined;
    return (errores ? Object.values(errores).flat()[0] : null) ?? 'No se ha podido guardar.';
  }
}
