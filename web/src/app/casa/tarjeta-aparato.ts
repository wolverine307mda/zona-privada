import { Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { ControlRadial, Interruptor } from '../ui';
import { CasaService } from './casa.service';
import { AparatoCasa, PuntoHistorial, campo, encendido, hace, numero, texto, trazo, valor } from './modelo';

const ICONOS: Record<string, string> = { sensor: '🌡️', puerta: '🚪', luz: '💡', enchufe: '🔌', clima: '❄️', tv: '📺', altavoz: '🔊', camara: '📷', ir: '📡' };
const MODOS: Record<string, string> = { auto: 'Auto', frio: 'Frío', calor: 'Calor', eco: 'Eco' };

// La tarjeta de un aparato: cada tipo enseña sus datos y sus mandos. Al editar el panel salen los botones de orden, ocultar y ajustes.
@Component({
  selector: 'app-tarjeta-aparato',
  imports: [Interruptor, ControlRadial],
  styleUrls: ['./tarjeta-aparato.css'],
  template: `
    <article class="t" [class.t--on]="activo()" [class.t--oculto]="oculto()" [attr.data-tipo]="a().tipo">
      <header class="t__cab">
        <span class="t__icono" aria-hidden="true">{{ icono() }}</span>
        <div class="t__nombre"><strong>{{ a().nombre }}</strong><small>{{ a().tipoNombre }}@if (a().driver === 'demo') { · ejemplo }</small></div>
        @if (editando()) {
          <div class="t__edicion">
            <button type="button" (click)="subir.emit()" [disabled]="primero()" aria-label="Subir">↑</button>
            <button type="button" (click)="bajar.emit()" [disabled]="ultimo()" aria-label="Bajar">↓</button>
            <button type="button" (click)="ocultar.emit()" [attr.aria-label]="oculto() ? 'Mostrar' : 'Ocultar'">{{ oculto() ? '👁' : '🚫' }}</button>
            <button type="button" (click)="ajustes.emit()" aria-label="Ajustes del aparato">⚙︎</button>
          </div>
        } @else if (interruptor(); as k) {
          <app-interruptor [activo]="activo()" [etiqueta]="'Encender o apagar ' + a().nombre" [ocupado]="enviando()" (activoChange)="alternar(k)" />
        }
      </header>

      @switch (a().tipo) {
        @case ('sensor') {
          <div class="t__grande">{{ texto(numero(a(), 'temperatura'), '°C') }}</div>
          <div class="t__fila"><span class="chip">💧 {{ texto(numero(a(), 'humedad'), '%', 0) }}</span><span class="t__hace">{{ hace(campo(a(), 'temperatura')?.actualizado ?? null) }}</span></div>
          @if (linea()) { <svg class="t__linea" viewBox="0 0 120 36" preserveAspectRatio="none" aria-hidden="true"><path [attr.d]="linea()" /></svg> } @else { <div class="t__linea t__linea--vacia">La gráfica se dibuja con las lecturas del día</div> }
        }
        @case ('puerta') {
          <div class="t__grande" [class.t__alerta]="valor(a(), 'abierto') === 'true'">{{ valor(a(), 'abierto') === 'true' ? 'Abierta' : valor(a(), 'abierto') === null ? '—' : 'Cerrada' }}</div>
          <div class="t__fila"><span class="t__hace">{{ hace(campo(a(), 'abierto')?.actualizado ?? null) }}</span></div>
        }
        @case ('luz') {
          <label class="mando"><span>Brillo <b>{{ texto(numero(a(), 'brillo'), '%', 0) }}</b></span>
            <input type="range" min="1" max="100" [value]="numero(a(), 'brillo') ?? 50" [disabled]="editando()" (change)="enviar('brillo', $any($event.target).value)" aria-label="Brillo"></label>
          <label class="mando mando--color"><span>Color</span>
            <input type="color" [value]="valor(a(), 'color') ?? '#ffb347'" [disabled]="editando()" (change)="enviar('color', $any($event.target).value)" aria-label="Color de la luz"></label>
        }
        @case ('enchufe') {
          <div class="t__grande">{{ activo() ? 'Encendido' : 'Apagado' }}</div>
          <div class="t__fila"><span class="chip">⚡ {{ texto(numero(a(), 'potencia'), 'W', 0) }}</span></div>
        }
        @case ('clima') {
          <app-control-radial [etiqueta]="'Temperatura de ' + a().nombre" [valor]="numero(a(), 'objetivo') ?? 24" [min]="campo(a(), 'objetivo')?.min ?? 16" [max]="campo(a(), 'objetivo')?.max ?? 30"
            [paso]="0.5" unidad="°C" [desactivado]="editando() || !activo()" (confirmado)="enviar('objetivo', $event)" />
          <div class="modos" role="group" aria-label="Modo">
            @for (m of modos; track m[0]) { <button type="button" class="chip" [class.chip--on]="valor(a(), 'modo') === m[0]" [disabled]="editando()" (click)="enviar('modo', m[0])">{{ m[1] }}</button> }
          </div>
        }
        @case ('tv') {
          <label class="mando"><span>Volumen <b>{{ texto(numero(a(), 'volumen'), '%', 0) }}</b></span>
            <input type="range" min="0" max="100" [value]="numero(a(), 'volumen') ?? 20" [disabled]="editando()" (change)="enviar('volumen', $any($event.target).value)" aria-label="Volumen"></label>
        }
        @case ('altavoz') {
          <div class="t__fila"><button type="button" class="chip chip--grande" [class.chip--on]="activo()" [disabled]="editando()" (click)="alternar('reproduciendo')">{{ activo() ? '⏸ Pausar' : '▶ Reproducir' }}</button></div>
          <label class="mando"><span>Volumen <b>{{ texto(numero(a(), 'volumen'), '%', 0) }}</b></span>
            <input type="range" min="0" max="100" [value]="numero(a(), 'volumen') ?? 30" [disabled]="editando()" (change)="enviar('volumen', $any($event.target).value)" aria-label="Volumen"></label>
        }
        @default { <p class="t__nada">Este aparato se activará cuando esté listo su conector.</p> }
      }
      @if (a().driver !== 'demo' && a().entidades.length && a().entidades.every((e) => e.valor === null)) { <p class="t__nada">Sin conexión con el aparato.</p> }
    </article>
  `,
})
export class TarjetaAparato implements OnInit {
  private readonly casa = inject(CasaService);
  readonly a = input.required<AparatoCasa>();
  readonly editando = input(false);
  readonly oculto = input(false);
  readonly primero = input(false);
  readonly ultimo = input(false);
  readonly subir = output<void>();
  readonly bajar = output<void>();
  readonly ocultar = output<void>();
  readonly ajustes = output<void>();

  protected readonly texto = texto; protected readonly numero = numero; protected readonly valor = valor; protected readonly campo = campo; protected readonly hace = hace;
  protected readonly modos = Object.entries(MODOS);
  private readonly puntos = signal<PuntoHistorial[]>([]);
  protected readonly linea = computed(() => trazo(this.puntos()));
  protected readonly icono = computed(() => ICONOS[this.a().tipo] ?? '🏠');
  // la clave del interruptor de la cabecera: encender (luz, enchufe, clima, tv) o reproducir (altavoz va en el cuerpo)
  protected readonly interruptor = computed(() => (['luz', 'enchufe', 'clima', 'tv'].includes(this.a().tipo) ? 'encendido' : null));
  protected readonly activo = computed(() => (this.a().tipo === 'altavoz' ? encendido(this.a(), 'reproduciendo') : encendido(this.a())));

  ngOnInit(): void {
    if (this.a().tipo === 'sensor') void this.casa.historial(this.a().id, 'temperatura', 24).then((p) => this.puntos.set(p)).catch(() => {});
  }

  // mientras la orden va de camino el interruptor no se puede volver a pulsar
  protected readonly enviando = signal(false);
  protected async alternar(clave: string): Promise<void> {
    this.enviando.set(true);
    try { await this.casa.ejecutar(this.a(), clave, !encendido(this.a(), clave)); } finally { this.enviando.set(false); }
  }
  protected enviar(clave: string, v: string | number): void { void this.casa.ejecutar(this.a(), clave, v); }
}
