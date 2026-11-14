import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { Suceso, TIPOS_SUCESO, ServidorService, hace, porDia } from './servidor.service';

const RANGOS = [{ dias: 1, texto: '24 h' }, { dias: 7, texto: '7 días' }, { dias: 30, texto: '30 días' }];

// La línea de tiempo del servidor: accesos, entradas con el iPhone, alertas de la guardia, runbooks y órdenes al PC, juntos, por orden y filtrables.
@Component({
  selector: 'app-linea-servidor',
  styleUrls: ['../gastos/comun.css'],
  template: `
    <section class="caja">
      <header class="caja__cab"><h3>Línea de tiempo</h3><span class="sub">Lo que ha pasado en el servidor</span></header>
      <div class="filtros" role="group" aria-label="Rango">
        @for (r of rangos; track r.dias) { <button type="button" class="chip" [attr.aria-pressed]="dias() === r.dias" (click)="elegirRango(r.dias)">{{ r.texto }}</button> }
      </div>
      <div class="filtros" role="group" aria-label="Qué mostrar">
        @for (t of tipos; track t.id) {
          <button type="button" class="chip" [attr.aria-pressed]="activos().includes(t.id)" (click)="alternar(t.id)">
            <svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path [attr.d]="t.icono" /></svg>{{ t.nombre }}</button>
        }
      </div>
      <input class="campo__input buscar" type="search" placeholder="Buscar (una IP, un runbook, «denegada»…)" [value]="texto()" (input)="buscar($any($event.target).value)" aria-label="Buscar en la línea de tiempo">
    </section>

    @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
    @for (g of grupos(); track g.dia) {
      <section class="caja">
        <h4 class="dia">{{ g.dia }}</h4>
        <ul class="lista">
          @for (s of g.sucesos; track $index) {
            <li class="suceso" [attr.data-gravedad]="s.gravedad">
              <span class="suceso__icono" aria-hidden="true"><svg class="icon" viewBox="0 0 24 24"><path [attr.d]="icono(s.tipo)" /></svg></span>
              <div class="suceso__cuerpo">
                <strong>{{ s.titulo }}</strong>
                @if (s.detalle) { <small>{{ s.detalle }}</small> }
              </div>
              <time class="suceso__hora" [attr.datetime]="s.hora" [title]="hace(s.hora)">{{ hora(s.hora) }}</time>
            </li>
          }
        </ul>
      </section>
    } @empty {
      @if (!cargando()) { <p class="vacio">No hay nada con esos filtros en este rango.</p> }
    }
    @if (llenos()) { <p class="vacio">Se enseñan los {{ max }} más recientes: acota el rango o busca para ver más.</p> }
  `,
  styles: `
    :host { display: grid; gap: 14px; }
    .filtros { display: flex; flex-wrap: wrap; gap: 6px; margin: 10px 0; }
    .chip { display: inline-flex; align-items: center; gap: 6px; min-height: 36px; padding: 0 12px; border-radius: 999px; border: 1px solid var(--vidrio-borde); background: none; color: var(--texto); font: 500 .82rem/1 var(--font-text); cursor: pointer; }
    .chip[aria-pressed='true'] { background: color-mix(in srgb, var(--brand) 20%, transparent); border-color: var(--brand); }
    .icon { width: 16px; height: 16px; fill: none; stroke: currentColor; stroke-width: 1.8; stroke-linecap: round; stroke-linejoin: round; }
    .buscar { width: 100%; }
    .dia { margin: 0 0 8px; font: 600 .95rem/1.2 var(--font-text); text-transform: capitalize; }
    .lista { list-style: none; margin: 0; padding: 0; display: grid; gap: 8px; }
    .suceso { display: grid; grid-template-columns: 36px minmax(0, 1fr) auto; gap: 12px; align-items: center; padding: 10px 12px; border-radius: 14px; border: 1px solid var(--vidrio-borde); background: color-mix(in srgb, var(--texto) 3%, transparent); }
    .suceso[data-gravedad='aviso'] { border-color: color-mix(in srgb, #ffc93c 60%, transparent); }
    .suceso[data-gravedad='grave'] { border-color: color-mix(in srgb, var(--bad) 70%, transparent); background: color-mix(in srgb, var(--bad) 8%, transparent); }
    .suceso__icono { display: grid; place-items: center; width: 36px; height: 36px; border-radius: 12px; background: color-mix(in srgb, var(--brand) 16%, transparent); color: var(--acento); }
    .suceso__icono .icon { width: 18px; height: 18px; }
    .suceso__cuerpo { display: grid; gap: 2px; min-width: 0; }
    .suceso__cuerpo strong { font-size: .92rem; overflow-wrap: anywhere; }
    .suceso__cuerpo small { color: var(--soft); font-size: .8rem; overflow-wrap: anywhere; }
    .suceso__hora { color: var(--soft); font: 500 .78rem/1 var(--font-code); }
    .vacio { color: var(--soft); text-align: center; margin: 12px 0; }
    @media (max-width: 560px) { .suceso { grid-template-columns: 32px minmax(0, 1fr); } .suceso__hora { grid-column: 2; } }
  `,
})
export class LineaServidor implements OnInit {
  private readonly api = inject(ServidorService);
  protected readonly rangos = RANGOS;
  protected readonly tipos = TIPOS_SUCESO;
  protected readonly max = 300;
  protected readonly dias = signal(7);
  protected readonly activos = signal<string[]>([]);   // vacío = todos
  protected readonly texto = signal('');
  protected readonly sucesos = signal<Suceso[]>([]);
  protected readonly cargando = signal(true);
  protected readonly error = signal('');
  protected readonly grupos = computed(() => porDia(this.sucesos()));
  protected readonly llenos = computed(() => this.sucesos().length >= this.max);
  protected readonly hace = hace;
  private espera: ReturnType<typeof setTimeout> | undefined;

  ngOnInit(): void { void this.cargar(); }

  protected icono(tipo: string): string { return TIPOS_SUCESO.find((t) => t.id === tipo)?.icono ?? TIPOS_SUCESO[TIPOS_SUCESO.length - 1].icono; }
  protected hora(iso: string): string { return new Date(iso).toLocaleTimeString('es-ES', { hour: '2-digit', minute: '2-digit' }); }

  protected elegirRango(d: number): void { this.dias.set(d); void this.cargar(); }
  protected alternar(id: string): void { this.activos.update((a) => (a.includes(id) ? a.filter((x) => x !== id) : [...a, id])); void this.cargar(); }
  protected buscar(t: string): void { this.texto.set(t); clearTimeout(this.espera); this.espera = setTimeout(() => void this.cargar(), 300); }

  private async cargar(): Promise<void> {
    this.cargando.set(true); this.error.set('');
    try {
      const hasta = new Date();
      const r = await this.api.linea({ desde: new Date(hasta.getTime() - this.dias() * 86400000).toISOString(), hasta: hasta.toISOString(), tipos: this.activos().join(','), q: this.texto().trim(), max: this.max });
      this.sucesos.set(r.sucesos);
    } catch { this.error.set('No se pudo leer la línea de tiempo.'); }
    finally { this.cargando.set(false); }
  }
}
