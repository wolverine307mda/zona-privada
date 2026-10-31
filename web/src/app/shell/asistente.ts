import { HttpClient } from '@angular/common/http';
import { Component, ElementRef, computed, effect, inject, signal, untracked, viewChild } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AgendaService, EventoEntrada } from '../agenda/agenda.service';
import { AsistenteService } from '../core/asistente.service';
import { GastosService, MovimientoEntrada, erroresDe } from '../gastos/gastos.service';
import { seguirTeclado } from '../gastos/teclado';
import { CATEGORIAS, FRECUENCIAS, Movimiento, categoria, euros } from '../gastos/modelo';

type Clase = 'movimiento' | 'agenda' | 'hucha' | 'pregunta' | 'nada';
interface AporteHucha { id: number; nombre: string; emoji: string; importe: number; }
interface Propuesta {
  clase: Clase; resumen: string; movimiento: MovimientoEntrada | null; evento: EventoEntrada | null; hucha: AporteHucha | null;
  dudas: string[] | null; respuesta: string | null;
}
interface Hecho { texto: string; deshacer: () => Promise<unknown>; ver?: () => void; deshecho?: boolean; }

interface Reconocedor {
  lang: string; interimResults: boolean; continuous: boolean; start(): void; stop(): void;
  onresult: ((e: { results: ArrayLike<{ isFinal: boolean; 0: { transcript: string } }> }) => void) | null;
  onend: (() => void) | null; onerror: ((e: { error: string }) => void) | null;
}
type ConstructorReconocedor = new () => Reconocedor;
const Reconocimiento = (globalThis as unknown as { SpeechRecognition?: ConstructorReconocedor; webkitSpeechRecognition?: ConstructorReconocedor });

const EJEMPLOS = [
  'ayer 12 € en el Mercadona', 'cobré la nómina 1.450 €', 'recuérdame mañana a las 9 llamar al taller', 'luz unos 60 € cada mes',
  'dentista el jueves a las 16:00', '50 € a la hucha', '¿cuánto llevo en restaurantes?', '¿cuánto tengo?',
];

// Dos dígitos: para pasar fechas a los campos de fecha y hora del navegador
const dd = (n: number) => String(n).padStart(2, '0');
const aLocal = (iso: string) => { const d = new Date(iso); return `${d.getFullYear()}-${dd(d.getMonth() + 1)}-${dd(d.getDate())}T${dd(d.getHours())}:${dd(d.getMinutes())}`; };

// El asistente: escribes o dices una frase («ayer 12 € en el Mercadona») y lo apunta donde toca.
// Antes de guardar enseña lo que ha entendido y deja retocarlo; después deja deshacerlo.
@Component({
  selector: 'app-asistente',
  host: { '(document:keydown)': 'atajo($event)' },
  template: `
    <dialog class="asis" #dlg (click)="fuera($event)" (close)="alCerrar()" aria-labelledby="asis-titulo">
      <div class="asis__caja">
        <header class="asis__cabeza">
          <span class="orbe" [class.vivo]="pensando() || escuchando()" aria-hidden="true"></span>
          <div>
            <h2 id="asis-titulo">{{ escuchando() ? 'Te escucho…' : '¿Qué apunto?' }}</h2>
            <p>Gastos, ingresos, recordatorios, huchas o preguntas, como lo dirías.</p>
          </div>
          <button class="asis__cerrar" type="button" (click)="cerrar()" aria-label="Cerrar">
            <svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6l12 12M18 6L6 18" /></svg>
          </button>
        </header>

        <form class="asis__campo" [class.escuchando]="escuchando()" (submit)="$event.preventDefault(); hacer()">
          <label class="sr" for="asis-texto">Frase</label>
          <input #campo id="asis-texto" type="text" autocomplete="off" enterkeyhint="done" maxlength="300"
                 placeholder="ayer 12 € en el Mercadona…" [value]="texto()" (input)="escribir($any($event.target).value)">
          @if (puedeHablar) {
            <button class="asis__mic" type="button" (click)="hablar()" [attr.aria-pressed]="escuchando()" [attr.aria-label]="escuchando() ? 'Dejar de escuchar' : 'Hablar'">
              <svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M12 3a3 3 0 0 0-3 3v6a3 3 0 0 0 6 0V6a3 3 0 0 0-3-3zM5 11a7 7 0 0 0 14 0M12 18v3" /></svg>
            </button>
          }
        </form>

        @if (hecho(); as h) {
          <div class="asis__hecho" [class.deshecho]="h.deshecho" role="status">
            <svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path [attr.d]="h.deshecho ? 'M9 14L4 9l5-5M4 9h11a5 5 0 0 1 0 10h-3' : 'M5 12.5l4.5 4.5L19 7.5'" /></svg>
            <span>{{ h.deshecho ? 'Deshecho.' : h.texto }}</span>
            @if (!h.deshecho) {
              @if (h.ver) { <button type="button" class="mini" (click)="h.ver()">Ver</button> }
              <button type="button" class="mini" (click)="deshacer(h)">Deshacer</button>
            }
          </div>
        }

        @if (propuesta(); as p) {
          <section class="tarjeta-asis" [attr.data-clase]="p.clase" aria-live="polite">
            @switch (p.clase) {
              @case ('movimiento') {
                @if (mov(); as m) {
                  <div class="fila-tipo">
                    <div class="segmento" role="group" aria-label="Tipo">
                      <button type="button" [class.activo]="m.tipo === 'Gasto'" (click)="ponerTipo('Gasto')">Gasto</button>
                      <button type="button" [class.activo]="m.tipo === 'Ingreso'" (click)="ponerTipo('Ingreso')">Ingreso</button>
                    </div>
                    @if (m.repite) {
                      <button type="button" class="chip" (click)="cambiar({ repite: null, variable: false })" title="Quitar la repetición">
                        ↻ {{ cada(m.repite) }}{{ m.variable ? ' · puede variar' : '' }} <span aria-hidden="true">×</span>
                      </button>
                    }
                  </div>
                  <div class="importe" [class.ingreso]="m.tipo === 'Ingreso'">
                    <span>{{ m.tipo === 'Ingreso' ? '+' : '−' }}</span>
                    <input type="number" inputmode="decimal" step="0.01" min="0.01" [value]="importeTexto()" [style.width.ch]="ancho()" (input)="teclearImporte($any($event.target).value)" aria-label="Importe">
                    <span>€</span>
                  </div>
                  <div class="campos">
                    <label class="campo campo--ancho"><span>Concepto</span>
                      <input type="text" [value]="m.concepto" maxlength="120" (input)="cambiar({ concepto: $any($event.target).value })"></label>
                    <label class="campo"><span>Categoría</span>
                      <span class="selector" [style.--c]="categoria(m.categoria).color">
                        <select [value]="m.categoria" (change)="cambiar({ categoria: $any($event.target).value })">
                          @for (c of categorias(); track c.id) { <option [value]="c.id" [selected]="c.id === m.categoria">{{ c.nombre }}</option> }
                        </select>
                      </span></label>
                    <label class="campo"><span>Fecha</span>
                      <input type="date" [value]="m.fecha" (input)="cambiar({ fecha: $any($event.target).value })"></label>
                  </div>
                  @if (m.litros || m.kilometros) {
                    <p class="extra">⛽ {{ m.litros ? m.litros + ' l' : '' }}{{ m.litros && m.kilometros ? ' · ' : '' }}{{ m.kilometros ? m.kilometros.toLocaleString('es-ES') + ' km' : '' }}{{ m.precioLitro ? ' · ' + m.precioLitro + ' €/l' : '' }}</p>
                  }
                }
              }
              @case ('agenda') {
                @if (ev(); as e) {
                  <div class="fila-tipo">
                    <span class="pastilla">{{ e.tipo === 'Recordatorio' ? '⏰ Recordatorio' : '📅 Evento' }}</span>
                    @if (e.repetir !== 'No') { <span class="chip">↻ {{ repeticion(e.repetir) }}</span> }
                  </div>
                  <div class="campos">
                    <label class="campo campo--ancho"><span>Qué</span>
                      <input type="text" [value]="e.titulo" maxlength="200" (input)="cambiarEv({ titulo: $any($event.target).value })"></label>
                    @if (e.todoElDia) {
                      <label class="campo"><span>Día (todo el día)</span>
                        <input type="date" [value]="aLocal(e.inicio).slice(0, 10)" (input)="ponerDia($any($event.target).value)"></label>
                    } @else {
                      <label class="campo"><span>Cuándo</span>
                        <input type="datetime-local" [value]="aLocal(e.inicio)" (input)="ponerMomento($any($event.target).value)"></label>
                    }
                  </div>
                  <p class="extra">🔔 Te aviso {{ e.todoElDia ? 'ese día a las 9' : e.avisoMinutos ? e.avisoMinutos + ' min antes' : 'a esa hora' }} con notificación{{ e.porCorreo ? ' y correo' : '' }}.</p>
                }
              }
              @case ('hucha') {
                @if (hu(); as h) {
                  <div class="hucha">
                    <span class="hucha__emoji" aria-hidden="true">{{ h.emoji }}</span>
                    <div><small>{{ h.importe < 0 ? 'Sacar de' : 'Meter en' }}</small><strong>{{ h.nombre }}</strong></div>
                  </div>
                  <div class="importe">
                    <span>{{ h.importe < 0 ? '−' : '+' }}</span>
                    <input type="number" inputmode="decimal" step="0.01" min="0.01" [value]="importeTexto()" [style.width.ch]="ancho()" (input)="teclearImporte($any($event.target).value)" aria-label="Importe">
                    <span>€</span>
                  </div>
                }
              }
              @case ('pregunta') { <p class="respuesta">{{ p.respuesta }}</p> }
              @default { <p class="pista">{{ p.resumen }}</p> }
            }
            @for (d of p.dudas ?? []; track d) { <p class="duda">{{ d }}</p> }
            @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
            @if (accion(); as a) {
              <div class="acciones">
                <button class="button" type="button" (click)="hacer()" [disabled]="guardando()">{{ guardando() ? 'Guardando…' : a }}</button>
              </div>
            }
          </section>
        } @else if (!texto()) {
          <div class="ejemplos">
            <p>Prueba con:</p>
            <ul>
              @for (e of ejemplos; track e) { <li><button type="button" (click)="usar(e)">{{ e }}</button></li> }
            </ul>
          </div>
        }

        @if (historial().length) {
          <footer class="asis__historial">
            <p>Hace un momento</p>
            <ul>@for (h of historial(); track $index) { <li [class.deshecho]="h.deshecho">{{ h.texto }}</li> }</ul>
          </footer>
        }
      </div>
    </dialog>
  `,
  styles: `
    .sr { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); }
    .asis { width: min(560px, calc(100vw - 24px)); max-height: calc(100svh - 24px); margin: 9vh auto auto; padding: 0; border: 1px solid var(--vidrio-borde); border-radius: 26px; color: var(--texto); overflow: auto;
      background: var(--brillo), color-mix(in srgb, var(--tarjeta) 90%, transparent); backdrop-filter: blur(30px) saturate(1.6); -webkit-backdrop-filter: blur(30px) saturate(1.6); box-shadow: 0 40px 110px rgba(0, 0, 0, 0.5); }
    .asis[open] { animation: sube 420ms var(--ease-expo); }
    @keyframes sube { from { opacity: 0; transform: translateY(24px) scale(0.97); } }
    .asis::backdrop { background: rgba(3, 8, 11, 0.5); backdrop-filter: blur(4px); }
    .asis__caja { display: flex; flex-direction: column; gap: 14px; padding: 18px; }

    .asis__cabeza { display: grid; grid-template-columns: auto minmax(0, 1fr) auto; align-items: center; gap: 14px; }
    .asis__cabeza h2 { margin: 0; font: 700 1.25rem/1.1 var(--font-display); letter-spacing: -0.02em; }
    .asis__cabeza p { margin: 3px 0 0; font-size: 13px; color: var(--soft); }
    .asis__cerrar { display: grid; place-items: center; width: 36px; height: 36px; border: 0; border-radius: 12px; background: var(--tarjeta-2); color: var(--soft); cursor: pointer; }
    .orbe { position: relative; width: 44px; height: 44px; border-radius: 50%; flex: none;
      background: conic-gradient(from var(--giro, 0deg), var(--brand), #7a5cff, #2ee6c5, #ff5fa2, var(--brand));
      box-shadow: 0 0 24px color-mix(in srgb, var(--brand) 45%, transparent), inset 0 0 12px rgba(255, 255, 255, 0.35); animation: gira 9s linear infinite; }
    .orbe::after { content: ''; position: absolute; inset: 7px; border-radius: 50%; background: radial-gradient(circle at 35% 30%, rgba(255, 255, 255, 0.9), rgba(255, 255, 255, 0.15) 45%, transparent 70%); }
    .orbe.vivo { animation: gira 1.4s linear infinite, late 0.9s ease-in-out infinite; }
    @property --giro { syntax: '<angle>'; inherits: false; initial-value: 0deg; }
    @keyframes gira { to { --giro: 360deg; } }
    @keyframes late { 50% { transform: scale(1.12); box-shadow: 0 0 40px color-mix(in srgb, var(--brand) 70%, transparent); } }

    .asis__campo { display: flex; align-items: center; gap: 8px; padding: 6px 6px 6px 16px; border-radius: 18px; border: 1px solid var(--linea); background: var(--tarjeta-2); transition: border-color 160ms ease, box-shadow 160ms ease; }
    .asis__campo:focus-within { border-color: var(--brand); box-shadow: 0 0 0 4px color-mix(in srgb, var(--brand) 18%, transparent); }
    .asis__campo input { flex: 1; min-width: 0; height: 44px; border: 0; background: none; color: var(--texto); font: 500 17px/1 var(--font-text); }
    .asis__campo input:focus { outline: none; }
    .asis__mic { display: grid; place-items: center; width: 44px; height: 44px; border: 0; border-radius: 14px; background: var(--brand); color: var(--on-brand); cursor: pointer; flex: none; }
    .escuchando .asis__mic { background: var(--bad); color: #fff; animation: late 0.9s ease-in-out infinite; }

    .asis__hecho { display: flex; align-items: center; gap: 10px; padding: 10px 12px; border-radius: 14px; background: color-mix(in srgb, var(--ok) 14%, var(--tarjeta-2)); font-size: 14px; animation: sube 300ms var(--ease-expo); }
    .asis__hecho .icon { color: var(--ok); width: 18px; height: 18px; flex: none; }
    .asis__hecho span { flex: 1; min-width: 0; overflow-wrap: anywhere; }
    .asis__hecho.deshecho { background: var(--tarjeta-2); }
    .asis__hecho.deshecho .icon { color: var(--soft); }
    .mini { border: 1px solid var(--linea); border-radius: 10px; padding: 6px 10px; background: var(--tarjeta); color: var(--texto); font: 600 12px/1 var(--font-text); cursor: pointer; flex: none; }

    .tarjeta-asis { display: flex; flex-direction: column; gap: 12px; padding: 16px; border-radius: 20px; border: 1px solid var(--linea); background: var(--tarjeta); animation: sube 260ms var(--ease-expo); }
    .fila-tipo { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 8px; }
    .segmento { display: inline-flex; padding: 3px; border-radius: 12px; background: var(--tarjeta-2); }
    .segmento button { border: 0; background: none; color: var(--soft); padding: 7px 14px; border-radius: 9px; font: 600 13px/1 var(--font-text); cursor: pointer; }
    .segmento button.activo { background: var(--brand); color: var(--on-brand); }
    .chip, .pastilla { display: inline-flex; align-items: center; gap: 6px; padding: 6px 10px; border-radius: 999px; border: 1px solid var(--linea); background: var(--tarjeta-2); color: var(--texto); font: 600 12px/1 var(--font-text); }
    button.chip { cursor: pointer; }
    .importe { display: flex; align-items: baseline; justify-content: center; gap: 4px; font: 700 clamp(2rem, 9vw, 2.8rem)/1 var(--font-display); letter-spacing: -0.03em; color: var(--texto); }
    .importe.ingreso { color: var(--ok); }
    .importe span { opacity: 0.7; }
    .importe input { width: 5ch; max-width: 9ch; min-width: 0; border: 0; border-bottom: 2px dashed var(--linea); background: none; color: inherit; font: inherit; text-align: center; -moz-appearance: textfield; }
    .importe input::-webkit-outer-spin-button, .importe input::-webkit-inner-spin-button { -webkit-appearance: none; margin: 0; }
    .importe input:focus { outline: none; border-bottom-color: var(--brand); }
    .campos { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 10px; }
    .campo { display: flex; flex-direction: column; gap: 5px; min-width: 0; }
    .campo--ancho { grid-column: 1 / -1; }
    .campo > span { font: 600 11px/1 var(--font-text); text-transform: uppercase; letter-spacing: 0.06em; color: var(--soft); }
    .campo input, .campo select { width: 100%; min-width: 0; height: 42px; padding: 0 12px; border-radius: 12px; border: 1px solid var(--linea); background: var(--tarjeta-2); color: var(--texto); font: 500 15px/1 var(--font-text); }
    .campo input:focus, .campo select:focus { outline: none; border-color: var(--brand); }
    .selector { position: relative; display: block; }
    .selector::before { content: ''; position: absolute; left: 12px; top: 50%; width: 10px; height: 10px; margin-top: -5px; border-radius: 50%; background: var(--c); pointer-events: none; }
    .selector select { padding-left: 30px; }
    .extra { margin: 0; font-size: 13px; color: var(--soft); }
    .hucha { display: flex; align-items: center; gap: 12px; }
    .hucha__emoji { display: grid; place-items: center; width: 48px; height: 48px; border-radius: 16px; background: var(--tarjeta-2); font-size: 26px; }
    .hucha small { display: block; font-size: 12px; color: var(--soft); }
    .hucha strong { font: 700 1.1rem/1.2 var(--font-display); }
    .respuesta { margin: 0; font: 500 1.08rem/1.5 var(--font-text); }
    .pista { margin: 0; color: var(--soft); }
    .duda { margin: 0; padding: 8px 10px; border-radius: 10px; background: color-mix(in srgb, var(--warn) 13%, transparent); font-size: 13px; }
    .error { margin: 0; color: var(--bad); font-size: 13px; }
    .acciones { display: flex; justify-content: flex-end; }
    .acciones .button { min-width: 140px; }

    .ejemplos p, .asis__historial p { margin: 0 0 8px; font: 600 11px/1 var(--font-text); text-transform: uppercase; letter-spacing: 0.08em; color: var(--soft); }
    .ejemplos ul { display: flex; flex-wrap: wrap; gap: 8px; margin: 0; padding: 0; list-style: none; }
    .ejemplos button { border: 1px solid var(--linea); border-radius: 999px; padding: 8px 12px; background: var(--tarjeta-2); color: var(--texto); font: 500 13px/1.2 var(--font-text); cursor: pointer; text-align: left; transition: border-color 150ms ease, transform 200ms var(--ease-out); }
    @media (hover: hover) { .ejemplos button:hover { border-color: var(--brand); transform: translateY(-1px); } }
    .asis__historial ul { margin: 0; padding: 0; list-style: none; display: flex; flex-direction: column; gap: 4px; font-size: 13px; color: var(--soft); }
    .asis__historial li::before { content: '✓ '; color: var(--ok); }
    .asis__historial li.deshecho { text-decoration: line-through; }
    .asis__historial li.deshecho::before { content: '↶ '; color: var(--soft); }

    @media (max-width: 640px) {
      .asis { position: fixed; inset: auto 0 var(--teclado, 0px) 0; margin: 0; width: 100%; max-width: 100%; max-height: calc(var(--alto, 100dvh) - 10px); border-radius: 26px 26px 0 0; border-bottom: 0; }
      .asis[open] { animation: sube-movil 420ms var(--ease-expo); }
      .asis__caja { padding-bottom: max(18px, env(safe-area-inset-bottom)); }
      .campos { grid-template-columns: minmax(0, 1fr); }
      @keyframes sube-movil { from { transform: translateY(100%); } }
    }
  `,
})
export class Asistente {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly gastos = inject(GastosService);
  private readonly agenda = inject(AgendaService);
  private readonly servicio = inject(AsistenteService);
  private readonly dlg = viewChild.required<ElementRef<HTMLDialogElement>>('dlg');
  private readonly campo = viewChild.required<ElementRef<HTMLInputElement>>('campo');

  protected readonly ejemplos = EJEMPLOS;
  protected readonly categoria = categoria;
  protected readonly aLocal = aLocal;
  protected readonly puedeHablar = !!(Reconocimiento.SpeechRecognition ?? Reconocimiento.webkitSpeechRecognition);

  protected readonly texto = signal('');
  protected readonly propuesta = signal<Propuesta | null>(null);
  protected readonly mov = signal<MovimientoEntrada | null>(null);
  protected readonly ev = signal<EventoEntrada | null>(null);
  protected readonly hu = signal<AporteHucha | null>(null);
  protected readonly pensando = signal(false);
  protected readonly escuchando = signal(false);
  protected readonly guardando = signal(false);
  protected readonly error = signal('');
  protected readonly hecho = signal<Hecho | null>(null);
  protected readonly historial = signal<Hecho[]>([]);
  // el importe se pinta una vez al llegar la propuesta (para no mover el cursor mientras escribes) y el campo crece con lo escrito
  protected readonly importeTexto = signal('');
  protected readonly ancho = signal(4);

  protected readonly categorias = computed(() => CATEGORIAS.filter((c) => c.tipo === (this.mov()?.tipo ?? 'Gasto')));
  protected readonly accion = computed(() => {
    switch (this.propuesta()?.clase) {
      case 'movimiento': return this.mov()?.tipo === 'Ingreso' ? 'Apuntar ingreso' : 'Apuntar gasto';
      case 'agenda': return this.ev()?.tipo === 'Recordatorio' ? 'Guardar recordatorio' : 'Guardar evento';
      case 'hucha': return (this.hu()?.importe ?? 0) < 0 ? 'Sacar de la hucha' : 'Meter en la hucha';
      default: return null;
    }
  });

  private temporizador?: ReturnType<typeof setTimeout>;
  private turno = 0;
  private reconocedor?: Reconocedor;
  private soltarTeclado?: () => void;

  constructor() {
    effect(() => {
      const p = this.servicio.peticion();
      if (!p) return;
      untracked(() => {
        this.abrir();
        if (p.texto) this.usar(p.texto);
        if (p.voz) this.hablar();
      });
    });
  }

  abrir(): void {
    const d = this.dlg().nativeElement;
    if (!d.open) { d.showModal(); this.soltarTeclado = seguirTeclado(d); }
    setTimeout(() => this.campo().nativeElement.focus(), 40);
  }
  protected cerrar(): void { this.dlg().nativeElement.close(); }
  protected fuera(e: MouseEvent): void { if (e.target === this.dlg().nativeElement) this.cerrar(); }
  protected alCerrar(): void {
    this.reconocedor?.stop();
    this.soltarTeclado?.();
    this.soltarTeclado = undefined;
    this.limpiar();
    this.hecho.set(null);
  }

  protected atajo(e: KeyboardEvent): void {
    if (e.key.toLowerCase() === 'j' && (e.ctrlKey || e.metaKey)) { e.preventDefault(); this.abrir(); }
  }

  private limpiar(): void {
    this.texto.set('');
    this.propuesta.set(null);
    this.mov.set(null); this.ev.set(null); this.hu.set(null);
    this.error.set('');
  }

  protected usar(t: string): void { this.texto.set(t); this.entender(t, 0); this.campo().nativeElement.focus(); }

  protected escribir(t: string): void {
    this.texto.set(t);
    if (!t.trim()) { this.limpiar(); return; }
    this.entender(t, 320);
  }

  private entender(t: string, espera: number): void {
    clearTimeout(this.temporizador);
    const turno = ++this.turno;
    this.temporizador = setTimeout(async () => {
      this.pensando.set(true);
      try {
        const p = await firstValueFrom(this.http.post<Propuesta>('/api/asistente/entender', { texto: t.trim() }));
        if (turno !== this.turno) return; // ya has seguido escribiendo
        this.error.set('');
        this.propuesta.set(p);
        this.mov.set(p.movimiento ? { ...p.movimiento } : null);
        this.ev.set(p.evento ? { ...p.evento } : null);
        this.hu.set(p.hucha ? { ...p.hucha } : null);
        const imp = p.movimiento?.importe ?? Math.abs(p.hucha?.importe ?? 0);
        this.importeTexto.set(imp.toFixed(2));
        this.ancho.set(Math.max(3, imp.toFixed(2).length - 0.2));
      } catch {
        if (turno === this.turno) this.error.set('No he podido pensarlo: ¿hay conexión?');
      } finally {
        if (turno === this.turno) this.pensando.set(false);
      }
    }, espera);
  }

  // ---- retoques antes de guardar ----
  protected cambiar(c: Partial<MovimientoEntrada>): void { this.mov.update((m) => (m ? { ...m, ...c } : m)); }
  protected ponerTipo(tipo: 'Gasto' | 'Ingreso'): void {
    const m = this.mov();
    if (!m || m.tipo === tipo) return;
    const vale = categoria(m.categoria).tipo === tipo;
    this.cambiar({ tipo, categoria: vale ? m.categoria : tipo === 'Gasto' ? 'otros' : 'otros-ingresos' });
  }
  protected cambiarEv(c: Partial<EventoEntrada>): void { this.ev.update((e) => (e ? { ...e, ...c } : e)); }
  protected ponerMomento(v: string): void {
    if (!v) return;
    const ini = new Date(v);
    this.ev.update((e) => (e ? { ...e, inicio: ini.toISOString(), fin: e.fin ? new Date(ini.getTime() + 3600_000).toISOString() : null } : e));
  }
  protected ponerDia(v: string): void {
    if (!v) return;
    const [a, m, d] = v.split('-').map(Number);
    this.cambiarEv({ inicio: new Date(a, m - 1, d).toISOString() });
  }
  protected teclearImporte(t: string): void {
    this.ancho.set(Math.max(3, t.length - 0.2));
    const v = Math.abs(+t.replace(',', '.'));
    if (this.mov()) this.cambiar({ importe: v });
    else this.ponerHucha(v);
  }
  protected ponerHucha(v: number): void { const sacar = (this.propuesta()?.hucha?.importe ?? 0) < 0; this.hu.update((h) => (h ? { ...h, importe: (sacar ? -1 : 1) * Math.abs(v) } : h)); }
  protected cada(f: string): string { return FRECUENCIAS.find((x) => x.id === f)?.cada ?? f; }
  protected repeticion(r: string): string { return ({ Diaria: 'cada día', Semanal: 'cada semana', Mensual: 'cada mes', Anual: 'cada año' } as Record<string, string>)[r] ?? r; }

  // ---- guardar ----
  protected async hacer(): Promise<void> {
    const p = this.propuesta();
    if (!p || !this.accion() || this.guardando()) return;
    const importe = p.clase === 'movimiento' ? this.mov()?.importe : p.clase === 'hucha' ? this.hu()?.importe : 1;
    if (!importe || !Number.isFinite(importe)) { this.error.set('Pon un importe mayor que cero.'); return; }
    this.guardando.set(true);
    this.error.set('');
    try {
      let h: Hecho | null = null;
      if (p.clase === 'movimiento' && this.mov()) h = await this.guardarMovimiento(this.mov()!);
      else if (p.clase === 'agenda' && this.ev()) h = await this.guardarEvento(this.ev()!);
      else if (p.clase === 'hucha' && this.hu()) h = await this.guardarHucha(this.hu()!);
      if (h) {
        this.hecho.set(h);
        this.historial.update((l) => [h!, ...l].slice(0, 4));
        this.limpiar();
        this.campo().nativeElement.focus();
      }
    } catch (e) {
      this.error.set(Object.values(erroresDe(e)).flat()[0] ?? 'No se ha podido guardar.');
    } finally {
      this.guardando.set(false);
    }
  }

  private async guardarMovimiento(m: MovimientoEntrada): Promise<Hecho> {
    const creado: Movimiento = await this.gastos.guardarMovimiento(m);
    this.gastos.recargar();
    return {
      texto: `${m.tipo === 'Ingreso' ? 'Ingreso' : 'Gasto'} apuntado: ${creado.concepto} · ${euros(creado.importe)}`,
      deshacer: async () => {
        await this.gastos.borrarMovimiento(creado.id);
        if (creado.recurrenteId) await firstValueFrom(this.http.delete(`/api/gastos/recurrentes/${creado.recurrenteId}`)).catch(() => {});
        this.gastos.recargar();
      },
      ver: () => { this.cerrar(); this.router.navigate(['/gastos']).then(() => this.gastos.editandoMovimiento.set({ movimiento: creado })); },
    };
  }

  private async guardarEvento(e: EventoEntrada): Promise<Hecho> {
    const r = await this.agenda.guardar(e);
    window.dispatchEvent(new Event('mda:agenda'));
    const cuando = new Date(e.inicio).toLocaleString('es-ES', e.todoElDia ? { weekday: 'short', day: 'numeric', month: 'short' } : { weekday: 'short', day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' });
    return {
      texto: `${e.tipo === 'Recordatorio' ? 'Te lo recuerdo' : 'En la agenda'}: ${e.titulo} · ${cuando}`,
      deshacer: async () => { await firstValueFrom(this.http.delete(`/api/agenda/${r.id}`)); window.dispatchEvent(new Event('mda:agenda')); },
      ver: () => { this.cerrar(); this.router.navigate(['/agenda']); },
    };
  }

  private async guardarHucha(h: AporteHucha): Promise<Hecho> {
    await this.gastos.aportar(h.id, h.importe);
    this.gastos.recargar();
    return {
      texto: `${h.emoji} ${h.importe < 0 ? 'Sacado de' : 'Metido en'} ${h.nombre}: ${euros(Math.abs(h.importe))}`,
      deshacer: async () => { await this.gastos.aportar(h.id, -h.importe); this.gastos.recargar(); },
      ver: () => { this.cerrar(); this.router.navigate(['/gastos/huchas']); },
    };
  }

  protected async deshacer(h: Hecho): Promise<void> {
    try {
      await h.deshacer();
      h.deshecho = true;
      this.hecho.set({ ...h });
      this.historial.update((l) => l.map((x) => (x === h ? h : x)));
      this.campo().nativeElement.focus();
    } catch {
      this.error.set('No se ha podido deshacer.');
    }
  }

  // ---- voz (el dictado del navegador; en el iPhone también vale el micrófono del teclado) ----
  protected hablar(): void {
    if (this.escuchando()) { this.reconocedor?.stop(); return; }
    const R = Reconocimiento.SpeechRecognition ?? Reconocimiento.webkitSpeechRecognition;
    if (!R) return;
    const r = new R();
    r.lang = 'es-ES';
    r.interimResults = true;
    r.continuous = false;
    r.onresult = (e) => {
      const lista = Array.from(e.results);
      const t = lista.map((x) => x[0].transcript).join('');
      this.texto.set(t);
      if (lista.at(-1)?.isFinal) this.entender(t, 0);
    };
    r.onend = () => this.escuchando.set(false);
    r.onerror = (e) => {
      this.escuchando.set(false);
      if (e.error === 'not-allowed' || e.error === 'service-not-allowed') this.error.set('Dale permiso al micrófono para poder hablarme.');
    };
    this.reconocedor = r;
    this.error.set('');
    this.escuchando.set(true);
    try { r.start(); } catch { this.escuchando.set(false); }
  }
}
