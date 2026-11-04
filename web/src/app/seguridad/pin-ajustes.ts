import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ConfirmarService } from '../core/confirmar.service';

// El PIN de Documentos: cuatro cifras que se piden al entrar en Documentos, en «tu carpeta de imágenes» y en la lista de señuelos.
// Cambiarlo pide confirmar con Face ID o la contraseña.
@Component({
  selector: 'app-pin-ajustes',
  styleUrls: ['../gastos/comun.css', './claves.css'],
  template: `
    <section class="caja">
      <header class="caja__cab"><h3>PIN de Documentos</h3><span class="sub">Una capa más: se pide al entrar y vale 10 minutos desde el último uso</span></header>
      @if (requerido() === false) {
        <p class="nada">Aún no hay PIN: Documentos se abre sin pedirlo. Crea uno de cuatro cifras.</p>
      } @else if (requerido()) {
        <p class="nada">Hay un PIN puesto. Cinco fallos seguidos bloquean esa sesión 15 minutos y te avisan por Telegram.</p>
      }
      @if (!formulario()) {
        <button class="button" type="button" (click)="formulario.set(true)">{{ requerido() ? 'Cambiar el PIN' : 'Crear un PIN' }}</button>
      } @else {
        <form class="confirmar" (submit)="guardar($event)" novalidate>
          <label class="campo"><span class="campo__etiqueta">Nuevo PIN (cuatro cifras)</span>
            <input class="campo__input" type="password" inputmode="numeric" autocomplete="off" maxlength="4" pattern="[0-9]*" [value]="nuevo()" (input)="nuevo.set($any($event.target).value.replace(/\\D/g, ''))"></label>
          @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
          <footer class="acciones">
            <button class="enlace" type="button" (click)="cancelar()">Cancelar</button>
            <button class="button" type="submit" [disabled]="ocupado() || nuevo().length !== 4">{{ ocupado() ? 'Guardando…' : 'Guardar' }}</button>
          </footer>
        </form>
      }
      @if (mensaje()) { <p class="nada" role="status">{{ mensaje() }}</p> }
    </section>
  `,
})
export class PinAjustes implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly confirmar = inject(ConfirmarService);
  protected readonly requerido = signal<boolean | null>(null);
  protected readonly formulario = signal(false);
  protected readonly nuevo = signal('');
  protected readonly error = signal('');
  protected readonly mensaje = signal('');
  protected readonly ocupado = signal(false);

  ngOnInit(): void { void this.leer(); }

  private async leer(): Promise<void> {
    try { this.requerido.set((await firstValueFrom(this.http.get<{ requerido: boolean }>('/api/seguridad/pin'))).requerido); } catch { /* se queda sin saber */ }
  }

  protected cancelar(): void { this.formulario.set(false); this.nuevo.set(''); this.error.set(''); }

  protected async guardar(ev: Event): Promise<void> {
    ev.preventDefault();
    if (this.ocupado() || this.nuevo().length !== 4) return;
    this.ocupado.set(true); this.error.set(''); this.mensaje.set('');
    try {
      for (let intento = 0; intento < 2; intento++) {
        try {
          await firstValueFrom(this.http.put('/api/seguridad/pin', { nuevo: this.nuevo() }));
          this.mensaje.set('PIN guardado.'); this.cancelar(); await this.leer();
          return;
        } catch (e) {
          const x = e as HttpErrorResponse;
          if (x.status === 403 && x.error?.reautenticar && intento === 0) { if (!(await this.confirmar.pedir('Confirma que eres tú para cambiar el PIN.'))) return; continue; }
          this.error.set(x.error?.errors?.nuevo?.[0] ?? x.error?.detail ?? 'No se pudo guardar el PIN.');
          return;
        }
      }
    } finally { this.ocupado.set(false); }
  }
}
