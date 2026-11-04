import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

interface TokenAtajo { id: number; nombre: string; alcances: string[]; ultimoUso: string | null; ultimaIp: string; creado: string; revocado: boolean; }

const PERMISOS = [
  { id: 'gasto', texto: 'Apuntar gastos', icono: 'M12 3v18M17 7H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6' },
  { id: 'pago', texto: 'Apple Pay', icono: 'M3 6h18v12H3zM3 10h18M7 15h3' },
  { id: 'lectura', texto: 'Leer saldo y agenda', icono: 'M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12zM12 9a3 3 0 1 0 0 6 3 3 0 0 0 0-6z' },
  { id: 'casa', texto: 'Llegar y salir de casa', icono: 'M3 11l9-7 9 7M5 10v10h5v-6h4v6h5V10' },
];

interface Paso { titulo: string; texto: string; ruta?: string; cuerpo?: string; permiso?: string; }

// Los atajos del iPhone: un token por atajo (con permisos propios, que se puede revocar) y los pasos para montarlos en la app Atajos.
@Component({
  selector: 'app-atajos',
  styleUrls: ['../gastos/comun.css', './claves.css'],
  template: `
    <section class="caja">
      <header class="caja__cab"><h3>Atajos del iPhone</h3><span class="sub">Apunta gastos desde el Centro de control, con Siri o al pagar con Apple Pay</span></header>

      @if (tokenNuevo(); as t) {
        <div class="nuevo" role="status">
          <p><strong>Copia el token ahora</strong>: es la única vez que se enseña.</p>
          <code class="nuevo__token">{{ t }}</code>
          <div class="acciones">
            <button class="button" type="button" (click)="copiar(t)">{{ copiado() === t ? '✓ Copiado' : 'Copiar el token' }}</button>
            <button class="enlace" type="button" (click)="tokenNuevo.set(null)">Ya lo he guardado</button>
          </div>
        </div>
      }
      @if (error()) { <p class="error" role="alert">{{ error() }}</p> }

      <ul class="tokens">
        @for (t of tokens(); track t.id) {
          <li class="token" [class.token--revocado]="t.revocado">
            <span class="token__icono" aria-hidden="true">
              <svg class="icon" viewBox="0 0 24 24">@if (t.revocado) { <path d="M6 6l12 12M18 6 6 18"/> } @else { <path d="M15 7a4 4 0 1 1-3.5 5.9L4 20v-3l2-1v-2l2-1 3.5-3.1A4 4 0 0 1 15 7zM16 8h.01"/> }</svg>
            </span>
            <div class="token__cuerpo">
              <strong>{{ t.nombre }}</strong>
              <div class="chips">
                @for (a of t.alcances; track a) { <span class="chip"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path [attr.d]="icono(a)"/></svg>{{ texto(a) }}</span> }
              </div>
              <small>{{ t.revocado ? 'Revocado' : t.ultimoUso ? 'Usado ' + hace(t.ultimoUso) + (t.ultimaIp ? ' · ' + t.ultimaIp : '') : 'Aún sin usar' }}</small>
            </div>
            @if (!t.revocado) {
              @if (borrando() === t.id) { <button class="enlace enlace--peligro" type="button" (click)="revocar(t.id)">¿Revocar?</button> }
              @else { <button class="enlace" type="button" (click)="borrando.set(t.id)" [attr.aria-label]="'Revocar ' + t.nombre">Revocar</button> }
            }
          </li>
        } @empty { <li class="nada">Aún no tienes ningún token: crea uno y úsalo en tu atajo.</li> }
      </ul>

      <form class="crear" (submit)="crear($event)" novalidate>
        <label class="campo"><span class="campo__etiqueta">Nombre del token</span>
          <input class="campo__input" maxlength="40" placeholder="iPhone de Mario" [value]="nombre()" (input)="nombre.set($any($event.target).value)"></label>
        <div class="campo"><span class="campo__etiqueta">Qué puede hacer</span>
          <div class="chips chips--elegir" role="group" aria-label="Permisos del token">
            @for (p of permisos; track p.id) {
              <button type="button" class="chip chip--boton" [attr.aria-pressed]="elegidos().includes(p.id)" (click)="alternar(p.id)">
                <svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path [attr.d]="p.icono"/></svg>{{ p.texto }}
              </button>
            }
          </div>
        </div>
        <footer class="acciones"><button class="button" type="submit" [disabled]="ocupado()">{{ ocupado() ? 'Creando…' : 'Crear token' }}</button></footer>
      </form>

      <h4 class="titulo-guia">Cómo montarlo en la app Atajos</h4>
      <ol class="pasos">
        @for (p of pasos; track p.titulo; let i = $index) {
          <li class="paso">
            <span class="paso__n" aria-hidden="true">{{ i + 1 }}</span>
            <div class="paso__cuerpo">
              <strong>{{ p.titulo }}@if (p.permiso) { <span class="chip chip--mini">permiso «{{ p.permiso }}»</span> }</strong>
              <p>{{ p.texto }}</p>
              @if (p.ruta) {
                <button class="url" type="button" (click)="copiar(origen + p.ruta)" [attr.aria-label]="'Copiar ' + origen + p.ruta">
                  <span class="url__metodo">POST</span><code>{{ origen }}{{ p.ruta }}</code><span class="url__copiar">{{ copiado() === origen + p.ruta ? '✓' : 'Copiar' }}</span>
                </button>
              }
              @if (p.cuerpo) { <p class="cuerpo">Cuerpo JSON: <code>{{ p.cuerpo }}</code></p> }
            </div>
          </li>
        }
      </ol>
      <p class="nada">En todos los atajos que llaman a la API, añade la cabecera <code>Authorization: Bearer &lt;tu token&gt;</code>.</p>
    </section>
  `,
  styles: `
    .nuevo { display: grid; gap: 10px; margin: 12px 0; padding: 16px; border-radius: 18px; border: 1px solid var(--brand); background: color-mix(in srgb, var(--brand) 9%, transparent); animation: aparece 520ms var(--ease-expo, ease-out) both, brilla-borde 2.4s ease-in-out 2; }
    .nuevo p { margin: 0; }
    .nuevo__token { display: block; padding: 12px 14px; border-radius: 12px; background: color-mix(in srgb, var(--texto) 8%, transparent); font: 600 .84rem/1.5 var(--font-code); word-break: break-all; user-select: all; }
    @keyframes aparece { from { opacity: 0; transform: translateY(10px) scale(.98); } }
    @keyframes brilla-borde { 50% { box-shadow: 0 0 0 4px color-mix(in srgb, var(--brand) 25%, transparent); } }
    .tokens { list-style: none; margin: 12px 0; padding: 0; display: grid; gap: 10px; }
    .token { display: grid; grid-template-columns: 44px minmax(0, 1fr) auto; gap: 12px; align-items: center; padding: 12px 14px; border-radius: 16px; border: 1px solid var(--vidrio-borde); background: color-mix(in srgb, var(--texto) 4%, transparent); }
    .token--revocado { opacity: .55; }
    .token__icono { display: grid; place-items: center; width: 44px; height: 44px; border-radius: 14px; background: color-mix(in srgb, var(--brand) 16%, transparent); color: var(--acento); }
    .token--revocado .token__icono { background: color-mix(in srgb, var(--bad) 16%, transparent); color: var(--bad); }
    .icon { width: 20px; height: 20px; fill: none; stroke: currentColor; stroke-width: 1.8; stroke-linecap: round; stroke-linejoin: round; }
    .token__cuerpo { display: grid; gap: 6px; min-width: 0; }
    .token__cuerpo small { color: var(--soft); font-size: .8rem; }
    .chips { display: flex; flex-wrap: wrap; gap: 6px; }
    .chip { display: inline-flex; align-items: center; gap: 6px; padding: 5px 10px; border-radius: 999px; border: 1px solid var(--vidrio-borde); font: 500 .78rem/1 var(--font-text); color: var(--soft); }
    .chip .icon { width: 14px; height: 14px; }
    .chip--boton { min-height: 40px; padding: 0 14px; background: transparent; color: inherit; cursor: pointer; transition: background 160ms, border-color 160ms, color 160ms; }
    .chip--boton[aria-pressed='true'] { background: color-mix(in srgb, var(--brand) 18%, transparent); border-color: var(--brand); color: var(--texto); }
    .chip--mini { margin-left: 8px; padding: 3px 8px; font-size: .7rem; vertical-align: 1px; }
    .crear { display: grid; gap: 12px; margin: 14px 0; }
    .titulo-guia { margin: 22px 0 8px; font: 600 1rem/1.2 var(--font-text); }
    .pasos { list-style: none; margin: 0; padding: 0; display: grid; gap: 10px; }
    .paso { display: grid; grid-template-columns: 34px minmax(0, 1fr); gap: 12px; padding: 14px; border-radius: 16px; border: 1px solid var(--vidrio-borde); background: color-mix(in srgb, var(--texto) 3%, transparent); }
    .paso__n { display: grid; place-items: center; width: 30px; height: 30px; border-radius: 50%; background: var(--brand); color: var(--on-brand); font: 700 .85rem/1 var(--font-code); }
    .paso__cuerpo { display: grid; gap: 8px; min-width: 0; }
    .paso__cuerpo p { margin: 0; color: var(--soft); font-size: .88rem; line-height: 1.45; }
    .cuerpo code, .nada code { font-size: .82em; word-break: break-all; }
    .url { display: flex; align-items: center; gap: 10px; width: 100%; min-width: 0; padding: 10px 12px; border-radius: 12px; border: 1px dashed var(--vidrio-borde); background: color-mix(in srgb, var(--texto) 5%, transparent); color: inherit; text-align: left; cursor: pointer; }
    .url code { flex: 1; min-width: 0; font-size: .8rem; word-break: break-all; }
    .url__metodo { flex: none; padding: 3px 7px; border-radius: 6px; background: var(--brand); color: var(--on-brand); font: 700 .68rem/1 var(--font-code); }
    .url__copiar { flex: none; color: var(--acento); font: 600 .78rem/1 var(--font-text); }
    @media (max-width: 560px) { .token { grid-template-columns: 40px minmax(0, 1fr); } .token > .enlace { grid-column: 2; justify-self: start; } .token__icono { width: 40px; height: 40px; } .paso { grid-template-columns: 28px minmax(0, 1fr); padding: 12px; } }
    @media (prefers-reduced-motion: reduce) { .nuevo { animation: none; } }
  `,
})
export class Atajos implements OnInit {
  private readonly http = inject(HttpClient);
  protected readonly permisos = PERMISOS;
  protected readonly origen = location.origin;
  protected readonly tokens = signal<TokenAtajo[]>([]);
  protected readonly tokenNuevo = signal<string | null>(null);
  protected readonly nombre = signal('');
  protected readonly elegidos = signal<string[]>(['gasto']);
  protected readonly copiado = signal<string | null>(null);
  protected readonly borrando = signal<number | null>(null);
  protected readonly ocupado = signal(false);
  protected readonly error = signal('');

  protected readonly pasos: Paso[] = [
    { titulo: 'Gasto rápido', permiso: 'gasto', ruta: '/api/atajos/movimiento', cuerpo: 'importe, concepto, tipo',
      texto: 'Atajos › + › «Pedir entrada» (número: importe; texto: concepto), «Elegir del menú» (Gasto / Ingreso) y «Obtener contenido de URL». Termina con «Mostrar notificación» con la respuesta (mensaje).' },
    { titulo: 'Por voz o con Siri', permiso: 'gasto', ruta: '/api/atajos/frase', cuerpo: 'texto',
      texto: '«Dictar texto» y la misma acción hacia esta dirección. Ponle de nombre «Apunta gasto».' },
    { titulo: 'Centro de control', texto: 'Ajustes › Centro de control › Añadir control › Atajo, y elige el atajo. También sirve el botón de acción.' },
    { titulo: 'Apple Pay', permiso: 'pago', ruta: '/api/atajos/pago', cuerpo: 'comercio (Comercio), importe (Cantidad), lat y lon opcionales (Ubicación actual › Latitud y Longitud)',
      texto: 'Atajos › Automatización › Transacción › «Ejecutar inmediatamente». Acción «Obtener contenido de URL». Si añades «Obtener ubicación actual» y mandas lat y lon, Cartera te enseña dónde gastas. Solo se prueba pagando de verdad.' },
    { titulo: 'Llegué a casa / me voy', permiso: 'casa', ruta: '/api/atajos/casa', cuerpo: 'estado = llegada (o salida)',
      texto: 'Atajos › Automatización › Llegar (o Salir) de un lugar › «Obtener contenido de URL».' },
    { titulo: 'Pregunta del bot', ruta: '/api/atajos/ping',
      texto: 'Una automatización por lugar u hora que haga POST aquí: el bot te pregunta y respondes desde la notificación de Telegram.' },
  ];

  ngOnInit(): void { void this.leer(); }

  protected icono(id: string): string { return PERMISOS.find((p) => p.id === id)?.icono ?? PERMISOS[0].icono; }
  protected texto(id: string): string { return PERMISOS.find((p) => p.id === id)?.texto ?? id; }

  protected hace(iso: string): string {
    const m = Math.round((Date.now() - new Date(iso).getTime()) / 60000);
    return m < 1 ? 'ahora mismo' : m < 60 ? `hace ${m} min` : m < 1440 ? `hace ${Math.round(m / 60)} h` : `hace ${Math.round(m / 1440)} d`;
  }

  private async leer(): Promise<void> {
    try { this.tokens.set(await firstValueFrom(this.http.get<TokenAtajo[]>('/api/atajos-tokens'))); } catch { /* se queda como estaba */ }
  }

  protected alternar(id: string): void { this.elegidos.update((a) => (a.includes(id) ? a.filter((x) => x !== id) : [...a, id])); }

  protected async copiar(t: string): Promise<void> {
    try { await navigator.clipboard.writeText(t); this.copiado.set(t); setTimeout(() => this.copiado.set(null), 1600); } catch { /* sin portapapeles */ }
  }

  protected async crear(e: Event): Promise<void> {
    e.preventDefault();
    if (this.ocupado()) return;
    if (!this.nombre().trim() || this.elegidos().length === 0) { this.error.set('Pon un nombre y elige al menos un permiso.'); return; }
    this.ocupado.set(true); this.error.set('');
    try {
      const r = await firstValueFrom(this.http.post<{ token: string }>('/api/atajos-tokens', { nombre: this.nombre().trim(), alcances: this.elegidos() }));
      this.tokenNuevo.set(r.token); this.nombre.set('');
      await this.leer();
    } catch (x) { this.error.set((x as HttpErrorResponse).error?.detail ?? 'No se pudo crear el token.'); }
    finally { this.ocupado.set(false); }
  }

  protected async revocar(id: number): Promise<void> {
    this.borrando.set(null);
    try { await firstValueFrom(this.http.delete(`/api/atajos-tokens/${id}`)); await this.leer(); }
    catch { this.error.set('No se pudo revocar el token.'); }
  }
}
