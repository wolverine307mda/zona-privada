import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { CasaService } from './casa.service';
import { AparatoCasa, PlantillaRegla, ReglaCasa, ReglaEntrada, RegistroRegla, hace } from './modelo';

const VACIA: ReglaEntrada = {
  nombre: '', activa: true, sensorId: 0, sensorClave: 'temperatura', operador: '>', umbral: 26, minutos: 5, destinoId: 0, accion: 'encender',
  objetivo: null, modo: null, revertir: true, histeresis: 2, esperaMinutos: 15, soloAvisar: false,
};

// Automatizaciones: «cuando el termómetro pase de 26 ° durante 5 min, enciende el aire a 24 °». Se parte de una plantilla o desde cero.
@Component({
  selector: 'app-reglas-casa',
  styleUrls: ['./reglas-casa.css'],
  template: `
    <section class="bloque">
      <h3>Automatizaciones</h3>
      @if (cargando()) { <div class="esq"><i></i><i></i></div> }
      @else {
        <ul class="reglas">
          @for (r of reglas(); track r.id) {
            <li class="regla" [class.regla--on]="r.disparada">
              <div class="regla__texto"><strong>{{ r.nombre }}</strong><small>{{ r.resumen }}{{ r.disparada ? ' · en marcha' : '' }}{{ r.ultima ? ' · ' + hace(r.ultima) : '' }}</small></div>
              <button class="interruptor" type="button" role="switch" [attr.aria-checked]="r.activa" [attr.aria-label]="'Activar ' + r.nombre" (click)="alternar(r)"><i></i></button>
              <button class="borrar" type="button" (click)="borrar(r)" [attr.aria-label]="'Quitar ' + r.nombre">{{ quitando() === r.id ? '¿Seguro?' : '🗑' }}</button>
            </li>
          } @empty { <li class="ayuda">Aún no hay automatizaciones. Elige una de las plantillas de abajo.</li> }
        </ul>
      }

      <h4>Crear una</h4>
      <div class="plantillas">
        @for (p of plantillas(); track p.id) { <button type="button" class="chip" (click)="usar(p)" [title]="p.descripcion">{{ p.nombre }}</button> }
        <button type="button" class="chip" (click)="usar(null)">Desde cero</button>
      </div>

      @if (editando()) {
        <form class="form" (submit)="guardar($event)" novalidate>
          <label class="campo"><span>Nombre</span><input type="text" maxlength="40" [value]="f().nombre" (input)="poner('nombre', $any($event.target).value)" placeholder="Aire si hace calor"></label>
          <div class="fila">
            <label class="campo"><span>Cuando…</span>
              <select (change)="poner('sensorId', +$any($event.target).value)">
                <option value="0" [selected]="f().sensorId === 0">Elige un sensor</option>
                @for (s of sensores(); track s.id) { <option [value]="s.id" [selected]="f().sensorId === s.id">{{ s.nombre }}</option> }
              </select></label>
            <label class="campo"><span>Dato</span>
              <select (change)="poner('sensorClave', $any($event.target).value)">
                <option value="temperatura" [selected]="f().sensorClave === 'temperatura'">Temperatura</option>
                <option value="humedad" [selected]="f().sensorClave === 'humedad'">Humedad</option>
              </select></label>
          </div>
          <div class="fila">
            <label class="campo"><span>Esté</span>
              <select (change)="poner('operador', $any($event.target).value)">
                <option value=">" [selected]="f().operador === '>'">por encima de</option>
                <option value="<" [selected]="f().operador === '<'">por debajo de</option>
              </select></label>
            <label class="campo"><span>Valor</span><input type="number" step="0.5" [value]="f().umbral" (input)="poner('umbral', +$any($event.target).value)"></label>
            <label class="campo"><span>Durante (min)</span><input type="number" min="0" max="1440" [value]="f().minutos" (input)="poner('minutos', +$any($event.target).value)"></label>
          </div>
          <label class="marca"><input type="checkbox" [checked]="f().soloAvisar" (change)="poner('soloAvisar', $any($event.target).checked)"> Solo avisarme por Telegram (no toca nada)</label>
          @if (!f().soloAvisar) {
            <div class="fila">
              <label class="campo"><span>Entonces</span>
                <select (change)="poner('accion', $any($event.target).value)">
                  <option value="encender" [selected]="f().accion === 'encender'">enciende</option>
                  <option value="apagar" [selected]="f().accion === 'apagar'">apaga</option>
                </select></label>
              <label class="campo"><span>Aparato</span>
                <select (change)="poner('destinoId', +$any($event.target).value)">
                  <option value="0" [selected]="f().destinoId === 0">Elige un aparato</option>
                  @for (d of destinos(); track d.id) { <option [value]="d.id" [selected]="f().destinoId === d.id">{{ d.nombre }}</option> }
                </select></label>
            </div>
            @if (esAire()) {
              <div class="fila">
                <label class="campo"><span>Temperatura del aire</span><input type="number" min="16" max="30" step="0.5" [value]="f().objetivo ?? 24" (input)="poner('objetivo', +$any($event.target).value)"></label>
                <label class="campo"><span>Modo</span>
                  <select (change)="poner('modo', $any($event.target).value)">
                    @for (m of modos; track m[0]) { <option [value]="m[0]" [selected]="(f().modo ?? 'frio') === m[0]">{{ m[1] }}</option> }
                  </select></label>
              </div>
            }
          }
          <label class="marca"><input type="checkbox" [checked]="f().revertir" (change)="poner('revertir', $any($event.target).checked)"> Volver atrás cuando se normalice (con margen de {{ f().histeresis }} °)</label>
          <div class="fila">
            <label class="campo"><span>Margen para volver (°)</span><input type="number" min="0" max="20" step="0.5" [value]="f().histeresis" (input)="poner('histeresis', +$any($event.target).value)"></label>
            <label class="campo"><span>Esperar entre acciones (min)</span><input type="number" min="1" max="1440" [value]="f().esperaMinutos" (input)="poner('esperaMinutos', +$any($event.target).value)"></label>
          </div>
          @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
          <div class="acciones">
            <button class="button button--ghost" type="button" (click)="editando.set(false)">Cancelar</button>
            <button class="button" type="submit" [disabled]="ocupado()">{{ ocupado() ? 'Guardando…' : 'Guardar' }}</button>
          </div>
        </form>
      }

      <h4>Lo último que han hecho</h4>
      <ul class="registro">
        @for (x of registro(); track x.id) { <li [class.mal]="!x.ok"><time>{{ hace(x.fecha) }}</time><span>{{ x.texto }}</span></li> } @empty { <li class="ayuda">Todavía nada.</li> }
      </ul>
    </section>
  `,
})
export class ReglasCasa implements OnInit {
  private readonly casa = inject(CasaService);
  readonly aparatos = input<AparatoCasa[]>([]);
  protected readonly reglas = signal<ReglaCasa[]>([]);
  protected readonly plantillas = signal<PlantillaRegla[]>([]);
  protected readonly registro = signal<RegistroRegla[]>([]);
  protected readonly cargando = signal(true);
  protected readonly editando = signal(false);
  protected readonly ocupado = signal(false);
  protected readonly error = signal('');
  protected readonly quitando = signal<number | null>(null);
  protected readonly f = signal<ReglaEntrada>({ ...VACIA });
  protected readonly hace = hace;
  protected readonly modos = [['auto', 'Auto'], ['frio', 'Frío'], ['calor', 'Calor'], ['eco', 'Eco']];

  protected readonly sensores = computed(() => this.aparatos().filter((a) => a.tipo === 'sensor'));
  protected readonly destinos = computed(() => this.aparatos().filter((a) => a.entidades.some((e) => e.clave === 'encendido')));
  protected readonly esAire = computed(() => this.aparatos().find((a) => a.id === this.f().destinoId)?.tipo === 'clima');

  async ngOnInit(): Promise<void> {
    await Promise.all([this.recargar(), this.casa.plantillas().then((p) => this.plantillas.set(p)).catch(() => {})]);
    this.cargando.set(false);
  }

  private async recargar(): Promise<void> {
    try {
      const [r, g] = await Promise.all([this.casa.reglas(), this.casa.registroReglas()]);
      this.reglas.set(r); this.registro.set(g);
    } catch { this.error.set('No se pudieron leer las automatizaciones.'); }
  }

  protected poner<K extends keyof ReglaEntrada>(k: K, v: ReglaEntrada[K]): void { this.f.update((x) => ({ ...x, [k]: v })); }

  protected usar(p: PlantillaRegla | null): void {
    const s = this.sensores()[0], d = this.destinos().find((x) => x.tipo === 'clima') ?? this.destinos()[0];
    this.f.set({ ...VACIA, ...(p?.valores ?? {}), nombre: p?.nombre ?? '', sensorId: s?.id ?? 0, destinoId: d?.id ?? 0 });
    this.error.set('');
    this.editando.set(true);
  }

  protected async guardar(e: Event): Promise<void> {
    e.preventDefault();
    this.ocupado.set(true);
    this.error.set('');
    try {
      await this.casa.guardarRegla(this.f());
      this.editando.set(false);
      await this.recargar();
    } catch (x) {
      const errores = x instanceof HttpErrorResponse ? (x.error?.errors as Record<string, string[]> | undefined) : undefined;
      this.error.set(errores ? Object.values(errores)[0]?.[0] ?? 'Revisa los datos.' : 'No se pudo guardar.');
    } finally { this.ocupado.set(false); }
  }

  protected async alternar(r: ReglaCasa): Promise<void> {
    const { id, disparada, ultima, resumen, ...entrada } = r;
    void disparada; void ultima; void resumen;
    try { await this.casa.guardarRegla({ ...entrada, activa: !r.activa }, id); await this.recargar(); } catch { this.error.set('No se pudo cambiar.'); }
  }

  protected async borrar(r: ReglaCasa): Promise<void> {
    if (this.quitando() !== r.id) { this.quitando.set(r.id); return; }
    try { await this.casa.borrarRegla(r.id); this.quitando.set(null); await this.recargar(); } catch { this.error.set('No se pudo quitar.'); }
  }
}
