import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, OnDestroy, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

interface Carpeta { nombre: string; bytes: number; }
interface Resultado { raiz: string; hora: string; carpetas: Carpeta[]; totalBytes: number; parcial: boolean; segundos: number; }

// «Qué ocupa»: las 15 carpetas más grandes de una unidad o carpeta del PC (solo lectura, unidades locales, 60 s como mucho).
// El agente cuenta y manda el resultado; aquí se pide y se pinta con barras.
@Component({
  selector: 'app-espacio-pc',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="caja espacio">
      <header class="caja__cab"><h3>Qué ocupa</h3><span class="sub">las carpetas más grandes de una unidad · solo mira, no borra nada</span></header>
      <form class="espacio__form" (submit)="pedir($event)">
        <input class="espacio__raiz" type="text" inputmode="url" autocomplete="off" spellcheck="false" aria-label="Carpeta o unidad" placeholder="C:\\ o D:\\Juegos" [value]="raiz()" (input)="raiz.set($any($event.target).value)">
        <button class="button" type="submit" [disabled]="buscando()">{{ buscando() ? 'Contando…' : 'Mirar' }}</button>
      </form>
      @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
      @if (r(); as d) {
        <p class="espacio__cab"><strong>{{ d.raiz }}</strong> · {{ tamano(d.totalBytes) }} contados · {{ hace(d.hora) }}
          @if (d.parcial) { <em class="espacio__parcial">resultado parcial: se cortó a los {{ d.segundos }} s</em> }</p>
        <ol class="espacio__lista">
          @for (c of d.carpetas; track c.nombre) {
            <li><span class="espacio__nombre">{{ c.nombre }}</span>
              <span class="espacio__barra" aria-hidden="true"><i [style.width.%]="porcentaje(c.bytes)"></i></span>
              <span class="espacio__valor">{{ tamano(c.bytes) }}</span></li>
          } @empty { <li class="sub">No hay nada que contar ahí (o no se pudo entrar).</li> }
        </ol>
      } @else if (!buscando()) { <p class="sub">Escribe una unidad o una carpeta (por ejemplo <code>C:\\</code>) y pulsa «Mirar». Tarda unos segundos, hasta un minuto en discos grandes.</p> }
    </section>
  `,
  styles: `
    :host { display: block; }
    .espacio { display: flex; flex-direction: column; gap: 12px; }
    .sub { color: var(--soft); font-size: .85rem; margin: 0; }
    .espacio__form { display: flex; gap: 8px; }
    .espacio__raiz { flex: 1; min-width: 0; min-height: 44px; padding: 0 14px; border-radius: 12px; border: 1px solid var(--linea); background: var(--tarjeta-2); color: var(--texto); font: inherit; }
    .espacio__cab { margin: 0; font-size: .85rem; color: var(--soft); }
    .espacio__cab strong { color: var(--texto); }
    .espacio__parcial { display: block; color: var(--acento, #ff7a1f); font-style: normal; }
    .espacio__lista { list-style: none; margin: 0; padding: 0; display: grid; gap: 6px; }
    .espacio__lista li { display: grid; grid-template-columns: minmax(80px, 1fr) minmax(60px, 2fr) 72px; gap: 10px; align-items: center; font-size: .85rem; }
    .espacio__nombre { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .espacio__barra { height: 8px; border-radius: 99px; background: var(--tarjeta-2); overflow: hidden; }
    .espacio__barra i { display: block; height: 100%; border-radius: 99px; background: var(--brand); }
    .espacio__valor { text-align: right; font-family: var(--font-code); font-size: .8rem; }
    code { font-family: var(--font-code); }
  `,
})
export class EspacioPc implements OnDestroy {
  private readonly http = inject(HttpClient);
  protected readonly raiz = signal('C:\\');
  protected readonly r = signal<Resultado | null>(null);
  protected readonly buscando = signal(false);
  protected readonly error = signal('');
  protected readonly mayor = computed(() => Math.max(1, ...(this.r()?.carpetas.map((c) => c.bytes) ?? [1])));
  private sondeo?: ReturnType<typeof setInterval>;

  constructor() { void this.cargar(); }

  private async cargar(): Promise<Resultado | null> {
    try { const x = await firstValueFrom(this.http.get<Resultado | null>('/api/pc/espacio')); this.r.set(x); if (x) this.raiz.set(x.raiz); return x; } catch { return null; }
  }

  protected async pedir(e: Event): Promise<void> {
    e.preventDefault();
    this.error.set('');
    const antes = this.r()?.hora ?? '';
    try { await firstValueFrom(this.http.post('/api/pc/espacio', { raiz: this.raiz() })); }
    catch (x) {
      const errores = x instanceof HttpErrorResponse ? (x.error?.errors as Record<string, string[]> | undefined) : undefined;
      this.error.set(errores ? Object.values(errores)[0]?.[0] ?? 'Esa carpeta no vale.' : 'No se pudo pedir al PC (¿está el agente en marcha?).');
      return;
    }
    // el agente cuenta y lo manda: se mira cada 3 s hasta que llegue algo nuevo (o a los 90 s)
    this.buscando.set(true);
    let intentos = 0;
    clearInterval(this.sondeo);
    this.sondeo = setInterval(async () => {
      intentos++;
      const x = await this.cargar();
      if ((x && x.hora !== antes) || intentos >= 30) {
        clearInterval(this.sondeo); this.buscando.set(false);
        if (!x || x.hora === antes) this.error.set('El PC no ha contestado: mira que el agente esté en marcha.');
      }
    }, 3000);
  }

  protected porcentaje(b: number): number { return Math.max(2, Math.round(100 * b / this.mayor())); }

  protected tamano(b: number): string {
    return b >= 2 ** 30 ? `${(b / 2 ** 30).toLocaleString('es-ES', { maximumFractionDigits: 1 })} GB` : b >= 2 ** 20 ? `${Math.round(b / 2 ** 20)} MB` : `${Math.round(b / 1024)} KB`;
  }

  protected hace(iso: string): string {
    const min = Math.max(0, Math.round((Date.now() - new Date(iso).getTime()) / 60000));
    return min < 1 ? 'ahora mismo' : min < 60 ? `hace ${min} min` : min < 1440 ? `hace ${Math.round(min / 60)} h` : `hace ${Math.round(min / 1440)} d`;
  }

  ngOnDestroy(): void { clearInterval(this.sondeo); }
}
