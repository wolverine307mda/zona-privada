import { HttpClient } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

const MODOS = [{ id: 'light', nombre: 'Día' }, { id: 'dark', nombre: 'Noche' }, { id: 'system', nombre: 'Sistema' }] as const;
// los mismos cinco colores que ofrece el pincel del portfolio (wolvem.dev)
const COLORES = [
  { id: 'naranja', nombre: 'Naranja', tono: '#ff7a1f' }, { id: 'lima', nombre: 'Lima', tono: '#c8f53c' },
  { id: 'cobalto', nombre: 'Cobalto', tono: '#3355ff' }, { id: 'rosa', nombre: 'Rosa', tono: '#ff5fa2' }, { id: 'rojo', nombre: 'Rojo', tono: '#e62429' },
] as const;

interface TemaWeb { configurado: boolean; modo: string; color: string; }

// «Tema del portfolio» dentro de Ajustes: el modo y el color por defecto de wolvem.dev para todos los visitantes, guardados en el servidor
// (a diferencia del resto de Ajustes, que se guardan en este navegador). La web lo lee de /api/publico/tema; quien elija el suyo con el pincel lo conserva.
@Component({
  selector: 'app-ajustes-web',
  template: `
    <section class="seccion" aria-labelledby="ajustes-web">
      <h3 id="ajustes-web">Tema del portfolio</h3>
      <p class="nota">El aspecto por defecto de wolvem.dev para todo el mundo. Se guarda aquí, en el servidor, no en cada navegador. Quien elija el suyo con el pincel lo conserva y puede volver a este.</p>
      <div class="grupo" role="group" aria-label="Modo del portfolio"><span>Modo</span>
        @for (m of modos; track m.id) { <button type="button" class="chip" [attr.aria-pressed]="modo() === m.id" (click)="modo.set(m.id)">{{ m.nombre }}</button> }</div>
      <div class="grupo" role="group" aria-label="Color del portfolio"><span>Color</span>
        @for (c of colores; track c.id) {
          <button type="button" class="chip" [attr.aria-pressed]="color() === c.id" (click)="color.set(c.id)"><i [style.background]="c.tono" aria-hidden="true"></i>{{ c.nombre }}</button>
        }</div>
      <div class="acciones">
        <button type="button" class="button" [disabled]="cargando()" (click)="guardar()">Guardar para todos</button>
        @if (configurado()) { <button type="button" class="restablecer" [disabled]="cargando()" (click)="quitar()">Quitar tema fijo</button> }
        <span class="estado" role="status">{{ mensaje() }}</span>
      </div>
      <p class="nota">{{ configurado() ? 'Ahora mismo wolvem.dev se abre en este tema para quien no haya elegido otro.' : 'Sin tema fijo: cada navegador usa el suyo (el del sistema, en naranja).' }}</p>
    </section>
  `,
  styles: `
    :host { display: block; }
    .seccion { display: flex; flex-direction: column; gap: 10px; }
    h3 { font-size: 12px; font-weight: 500; text-transform: uppercase; letter-spacing: .08em; color: var(--soft); margin: 0; }
    .nota { margin: 0; color: var(--soft); font-size: .82rem; line-height: 1.5; }
    .grupo { display: flex; flex-wrap: wrap; align-items: center; gap: 6px; font-size: .9rem; } .grupo > span { margin-right: 6px; min-width: 3.2em; }
    .chip { display: inline-flex; align-items: center; gap: 8px; min-height: 40px; padding: 0 14px; border-radius: 999px; border: 1px solid var(--linea); background: none; color: var(--texto); font: inherit; cursor: pointer; }
    .chip i { width: 12px; height: 12px; border-radius: 50%; box-shadow: 0 0 0 1px rgb(0 0 0 / .25); }
    .chip[aria-pressed='true'] { background: var(--brand); border-color: var(--brand); color: var(--on-brand); }
    .acciones { display: flex; flex-wrap: wrap; align-items: center; gap: 8px 14px; }
    .restablecer { min-height: 40px; padding: 0 4px; border: 0; background: none; color: var(--brand); font: inherit; cursor: pointer; }
    .estado { font-size: .85rem; color: var(--soft); }
  `,
})
export class AjustesWeb {
  private readonly http = inject(HttpClient);
  protected readonly modos = MODOS;
  protected readonly colores = COLORES;
  protected readonly modo = signal<string>('system');
  protected readonly color = signal<string>('naranja');
  protected readonly configurado = signal(false);
  protected readonly cargando = signal(true);
  protected readonly mensaje = signal('');

  constructor() {
    firstValueFrom(this.http.get<TemaWeb>('/api/tema-web'))
      .then((t) => { this.modo.set(t.modo); this.color.set(t.color); this.configurado.set(t.configurado); })
      .catch(() => this.mensaje.set('No se pudo leer el tema actual.'))
      .finally(() => this.cargando.set(false));
  }

  protected async guardar(): Promise<void> {
    await this.hacer(() => this.http.put('/api/tema-web', { modo: this.modo(), color: this.color() }), true, 'Guardado: wolvem.dev ya lo usa.');
  }

  protected async quitar(): Promise<void> {
    await this.hacer(() => this.http.delete('/api/tema-web'), false, 'Quitado: cada navegador vuelve a usar el suyo.');
  }

  private async hacer(llamada: () => ReturnType<HttpClient['put']>, fijo: boolean, ok: string): Promise<void> {
    this.cargando.set(true);
    this.mensaje.set('');
    try { await firstValueFrom(llamada()); this.configurado.set(fijo); this.mensaje.set(ok); }
    catch { this.mensaje.set('No se pudo guardar. Prueba otra vez.'); }
    finally { this.cargando.set(false); }
  }
}
