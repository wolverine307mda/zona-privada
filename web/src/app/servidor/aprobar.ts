import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ConfirmarService } from '../core/confirmar.service';
import { Pendiente, ServidorService, hace } from './servidor.service';

// «Aprobar una entrada»: se abre desde el aviso del iPhone. Cada petición de entrada de un ordenador nuevo sale con su equipo, lugar e IP; tecleas el código
// de dos cifras que enseña ESE ordenador (así no apruebas a ciegas una petición ajena) y confirmas con Face ID.
@Component({
  selector: 'app-aprobar',
  imports: [RouterLink],
  styleUrls: ['../gastos/comun.css'],
  template: `
    <section class="caja">
      <header class="caja__cab"><h3>Entradas por aprobar</h3><span class="sub">Un ordenador pide entrar sin contraseña</span></header>
      <p class="nota">Compara el equipo y el lugar. Si es el tuyo, escribe aquí el <strong>código de dos cifras</strong> que ves en su pantalla y confirma con Face ID.
        Si no lo reconoces, pulsa «No he sido yo».</p>
      @if (mensaje()) { <p class="aviso" role="status">{{ mensaje() }}</p> }
      @for (p of pendientes(); track p.id) {
        <article class="peticion">
          <div class="peticion__datos">
            <strong>{{ p.equipo }}</strong>
            <span>{{ p.lugar || 'Lugar desconocido' }} · {{ p.ip }}</span>
            <small>{{ hace(p.creada) }} · caduca {{ caduca(p) }}</small>
          </div>
          <form class="peticion__form" (submit)="aprobar($event, p)" novalidate>
            <input class="campo__input codigo" inputmode="numeric" pattern="[0-9]*" maxlength="2" autocomplete="one-time-code" placeholder="00" aria-label="Código que enseña el otro ordenador"
                   [value]="codigos()[p.id] ?? ''" (input)="poner(p.id, $any($event.target).value)">
            <button class="button" type="submit" [disabled]="ocupado() === p.id || (codigos()[p.id] ?? '').length !== 2">{{ ocupado() === p.id ? 'Comprobando…' : 'Aprobar con Face ID' }}</button>
            <button class="button button--ghost" type="button" (click)="denegar(p)" [disabled]="ocupado() === p.id">No he sido yo</button>
          </form>
        </article>
      } @empty {
        <p class="vacio">No hay ninguna entrada pendiente.</p>
      }
      <footer class="acciones"><a class="button button--ghost" routerLink="/servidor" [queryParams]="{ ver: 'linea' }">Ver la línea de tiempo</a></footer>
    </section>
  `,
  styles: `
    .nota { margin: 8px 0 12px; color: var(--soft); font-size: .88rem; line-height: 1.5; }
    .aviso { margin: 0 0 12px; padding: 8px 10px; border-radius: 10px; background: color-mix(in srgb, var(--brand) 12%, transparent); font-size: .9rem; }
    .peticion { display: grid; gap: 12px; padding: 14px; margin-bottom: 10px; border-radius: 16px; border: 1px solid var(--brand); background: color-mix(in srgb, var(--brand) 7%, transparent); }
    .peticion__datos { display: grid; gap: 2px; } .peticion__datos span, .peticion__datos small { color: var(--soft); font-size: .86rem; }
    .peticion__form { display: flex; flex-wrap: wrap; gap: 8px; align-items: center; }
    .codigo { width: 84px; text-align: center; font: 700 1.5rem/1 var(--font-code); letter-spacing: .15em; min-height: 52px; }
    .vacio { color: var(--soft); text-align: center; margin: 16px 0; }
  `,
})
export class Aprobar implements OnInit {
  private readonly api = inject(ServidorService);
  private readonly confirmador = inject(ConfirmarService);
  protected readonly hace = hace;
  protected readonly pendientes = signal<Pendiente[]>([]);
  protected readonly codigos = signal<Record<string, string>>({});
  protected readonly ocupado = signal<string | null>(null);
  protected readonly mensaje = signal('');
  private temporizador: ReturnType<typeof setTimeout> | undefined;
  private vivo = true;

  constructor() { inject(DestroyRef).onDestroy(() => { this.vivo = false; clearTimeout(this.temporizador); }); }

  ngOnInit(): void { void this.leer(); }

  // se repasa cada 4 s: la petición puede llegar mientras la pantalla está abierta, y las caducadas desaparecen
  private async leer(): Promise<void> {
    if (!this.vivo) return;
    try { this.pendientes.set(await this.api.pendientes()); } catch { /* sin red: se reintenta */ }
    this.temporizador = setTimeout(() => void this.leer(), 4000);
  }

  protected caduca(p: Pendiente): string {
    const s = Math.max(0, Math.round((new Date(p.caduca).getTime() - Date.now()) / 1000));
    return `en ${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`;
  }

  protected poner(id: string, valor: string): void { this.codigos.update((c) => ({ ...c, [id]: valor.replace(/\D/g, '').slice(0, 2) })); }

  protected async aprobar(e: Event, p: Pendiente): Promise<void> {
    e.preventDefault();
    this.mensaje.set('');
    this.ocupado.set(p.id);
    try {
      for (let intento = 0; intento < 2; intento++) {
        try { await this.api.aprobarEntrada(p.id, this.codigos()[p.id] ?? ''); this.mensaje.set('✅ Aprobada: el otro ordenador ya puede entrar.'); await this.refrescar(); return; }
        catch (x) {
          const h = x as HttpErrorResponse;
          // lo delicado pide Face ID justo antes: se pide y se repite una vez
          if (h.status === 403 && h.error?.reautenticar && intento === 0) { if (!(await this.confirmador.pedir('Confirma con Face ID que apruebas esta entrada.'))) return; continue; }
          this.mensaje.set(h.error?.mensaje ?? 'No se pudo aprobar.');
          await this.refrescar();
          return;
        }
      }
    } finally { this.ocupado.set(null); }
  }

  protected async denegar(p: Pendiente): Promise<void> {
    this.ocupado.set(p.id);
    try { await this.api.denegarEntrada(p.id); this.mensaje.set('🚫 Denegada. Si no fuiste tú, cambia la contraseña y mira Seguridad › Sesiones.'); await this.refrescar(); }
    catch { this.mensaje.set('No se pudo denegar.'); }
    finally { this.ocupado.set(null); }
  }

  private async refrescar(): Promise<void> { clearTimeout(this.temporizador); await this.leer(); }
}
