import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

interface Elemento { id: number; texto: string; origen: string; creado: string; }

// Un texto o un enlace que pasa del móvil al PC y al revés. Se guardan los 20 últimos durante 24 horas.
@Component({
  selector: 'app-portapapeles-pc',
  styleUrls: ['../gastos/comun.css', './modulos.css'],
  template: `
    <section class="caja">
      <header class="caja__cab"><h3>Portapapeles compartido</h3><span class="sub">del móvil al PC y al revés · 24 h</span></header>
      <form class="alta" (submit)="guardar($event)" novalidate>
        <textarea class="campo__input" rows="2" maxlength="2000" placeholder="Pega aquí un texto o un enlace…" aria-label="Texto para el portapapeles" [value]="texto()" (input)="texto.set($any($event.target).value)"></textarea>
        <div class="acciones">
          <button class="boton" type="button" (click)="delPc()">⬇️ Traer el del PC</button>
          <button class="button" type="submit" [disabled]="!texto().trim()">Guardar</button>
        </div>
      </form>
      @if (mensaje()) { <p class="msg" role="status">{{ mensaje() }}</p> }
      <ul class="clips">
        @for (e of elementos(); track e.id) {
          <li class="clip">
            <p class="clip__texto">{{ e.texto }}</p>
            <small>{{ e.origen }} · {{ hora(e.creado) }}</small>
            <div class="clip__acciones">
              <button class="enlace" type="button" (click)="copiar(e)">Copiar aquí</button>
              <button class="enlace" type="button" (click)="alPc(e)">Enviar al PC</button>
              <button class="enlace enlace--peligro" type="button" (click)="borrar(e)">Borrar</button>
            </div>
          </li>
        } @empty { <li class="nada">Nada guardado todavía.</li> }
      </ul>
      @if (elementos().length > 1) { <button class="enlace enlace--peligro" type="button" (click)="vaciar()">Vaciar todo</button> }
    </section>
  `,
})
export class PortapapelesPc implements OnInit {
  private readonly http = inject(HttpClient);
  protected readonly elementos = signal<Elemento[]>([]);
  protected readonly texto = signal('');
  protected readonly mensaje = signal('');

  ngOnInit(): void { void this.leer(); }

  private async leer(): Promise<void> {
    try { this.elementos.set(await firstValueFrom(this.http.get<Elemento[]>('/api/portapapeles'))); } catch { /* se queda como estaba */ }
  }

  protected hora(iso: string): string { return new Date(iso).toLocaleString('es-ES', { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' }); }

  protected async guardar(ev: Event): Promise<void> {
    ev.preventDefault();
    if (!this.texto().trim()) return;
    await firstValueFrom(this.http.post('/api/portapapeles', { texto: this.texto() }));
    this.texto.set('');
    await this.leer();
  }

  protected async copiar(e: Elemento): Promise<void> {
    try { await navigator.clipboard.writeText(e.texto); this.mensaje.set('Copiado en este dispositivo.'); }
    catch { this.mensaje.set('Este navegador no deja copiar solo: mantén pulsado el texto.'); }
  }

  private async orden(url: string, ok: string): Promise<void> {
    this.mensaje.set('');
    try { await firstValueFrom(this.http.post(url, null)); this.mensaje.set(ok); }
    catch (e) { this.mensaje.set((e as HttpErrorResponse).error?.detalle ?? 'No se pudo.'); }
  }

  protected alPc(e: Elemento): Promise<void> { return this.orden(`/api/portapapeles/${e.id}/al-pc`, 'Enviado: ya puedes pegarlo en el PC (Ctrl+V).'); }

  protected async delPc(): Promise<void> {
    await this.orden('/api/portapapeles/del-pc', 'Pidiendo el portapapeles del PC…');
    setTimeout(() => void this.leer(), 4000);
  }

  protected async borrar(e: Elemento): Promise<void> { await firstValueFrom(this.http.delete(`/api/portapapeles/${e.id}`)); await this.leer(); }
  protected async vaciar(): Promise<void> { await firstValueFrom(this.http.delete('/api/portapapeles')); await this.leer(); }
}
