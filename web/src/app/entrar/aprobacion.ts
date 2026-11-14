import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, inject, output, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

interface Peticion { id: string; codigo: string; secreto: string; caduca: string; }

// «Entrar con el iPhone»: sin escribir la contraseña. Este ordenador pide la entrada y enseña un código de dos cifras; en el iPhone te llega un aviso,
// tecleas ese código y confirmas con Face ID. Cuando se aprueba, esta pantalla recoge la sesión sola.
@Component({
  selector: 'app-entrada-aprobada',
  template: `
    @if (!peticion()) {
      <button class="button entrar entrar--passkey" type="button" (click)="pedir()" [disabled]="ocupado()" [attr.aria-busy]="ocupado() || null">
        {{ ocupado() ? 'Pidiendo…' : 'Entrar aprobándolo desde el iPhone' }}</button>
    } @else {
      <div class="aprob" role="status" aria-live="polite">
        @switch (estado()) {
          @case ('pendiente') {
            <p class="aprob__texto">En el iPhone te ha llegado un aviso. Ábrelo, escribe este código y confirma con Face ID:</p>
            <p class="aprob__codigo" aria-label="Código de aprobación">{{ peticion()!.codigo }}</p>
            <p class="aprob__pie">Caduca en {{ quedan() }}</p>
            <button class="aprob__cancelar" type="button" (click)="dejar()">Cancelar</button>
          }
          @case ('aprobada') { <p class="aprob__texto">¡Aprobada! Entrando…</p> }
          @case ('denegada') { <p class="aprob__texto">La han denegado desde el iPhone.</p> <button class="aprob__cancelar" type="button" (click)="dejar()">Volver</button> }
          @default { <p class="aprob__texto">La petición ha caducado.</p> <button class="aprob__cancelar" type="button" (click)="dejar()">Volver</button> }
        }
      </div>
    }
    @if (error()) { <p class="aprob__error" role="alert">{{ error() }}</p> }
  `,
  styles: `
    .aprob { display: grid; gap: 8px; justify-items: center; text-align: center; padding: 14px; border-radius: 18px; border: 1px solid var(--brand); background: color-mix(in srgb, var(--brand) 9%, transparent); }
    .aprob__texto { margin: 0; font-size: .92rem; line-height: 1.45; }
    .aprob__codigo { margin: 0; font: 800 3.4rem/1 var(--font-code); letter-spacing: .12em; color: var(--brand); }
    .aprob__pie { margin: 0; color: var(--soft); font-size: .82rem; }
    .aprob__cancelar { background: none; border: 0; color: var(--acento); font: 600 .88rem/1 var(--font-text); min-height: 40px; cursor: pointer; }
    .aprob__error { margin: 8px 0 0; text-align: center; font-size: .88rem; color: var(--bad); }
  `,
})
export class EntradaAprobada {
  private readonly http = inject(HttpClient);
  /** la sesión ya está abierta: la pantalla de entrar sigue como tras un passkey */
  readonly entrado = output<void>();
  protected readonly peticion = signal<Peticion | null>(null);
  protected readonly estado = signal('pendiente');
  protected readonly ocupado = signal(false);
  protected readonly error = signal('');
  protected readonly quedan = signal('3:00');
  private temporizador: ReturnType<typeof setTimeout> | undefined;
  private vivo = true;

  constructor() { inject(DestroyRef).onDestroy(() => { this.vivo = false; clearTimeout(this.temporizador); }); }

  protected async pedir(): Promise<void> {
    this.ocupado.set(true); this.error.set('');
    try {
      this.peticion.set(await firstValueFrom(this.http.post<Peticion>('/api/auth/aprobacion', null)));
      this.estado.set('pendiente');
      void this.sondear();
    } catch (x) {
      const h = x as HttpErrorResponse;
      this.error.set(h.status === 429 ? 'Ya hay peticiones pendientes: espera un poco.' : h.error?.mensaje ?? 'No se pudo pedir la aprobación.');
    } finally { this.ocupado.set(false); }
  }

  protected dejar(): void { clearTimeout(this.temporizador); this.peticion.set(null); this.error.set(''); }

  // pregunta cada 2 s si ya está aprobada y cuenta atrás hasta que caduque
  private async sondear(): Promise<void> {
    const p = this.peticion();
    if (!p || !this.vivo) return;
    const s = Math.max(0, Math.round((new Date(p.caduca).getTime() - Date.now()) / 1000));
    this.quedan.set(`${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`);
    try {
      const r = await firstValueFrom(this.http.post<{ estado: string }>(`/api/auth/aprobacion/${p.id}/estado`, { secreto: p.secreto }));
      this.estado.set(r.estado);
      if (r.estado === 'aprobada') { await this.recoger(p); return; }
      if (r.estado !== 'pendiente') return;
    } catch { /* un fallo de red no cancela la petición: se sigue preguntando */ }
    this.temporizador = setTimeout(() => void this.sondear(), 2000);
  }

  private async recoger(p: Peticion): Promise<void> {
    try { await firstValueFrom(this.http.post(`/api/auth/aprobacion/${p.id}/entrar`, { secreto: p.secreto })); this.entrado.emit(); }
    catch (x) { this.error.set((x as HttpErrorResponse).error?.mensaje ?? 'No se pudo abrir la sesión.'); this.estado.set('caducada'); }
  }
}
