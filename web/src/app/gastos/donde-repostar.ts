import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, ElementRef, effect, inject, signal, untracked, viewChild } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { GastosService } from './gastos.service';
import { euros, numero } from './modelo';

interface Estacion { rotulo: string; direccion: string; localidad: string; km: number; precio: number; lat: number; lon: number; }
interface Punto { nombre: string; lat: number; lon: number; }
interface Ajustes { combustible: string; radio: number; punto: Punto | null; aviso: boolean; }
interface Respuesta {
  ajustes: Ajustes; combustibles: { id: string; nombre: string }[];
  sinPunto?: boolean; sinDatos?: boolean; punto?: Punto; origen?: string; nombreCombustible?: string;
  estaciones?: Estacion[]; mediaProvincia?: number | null; provincia?: string;
  valoracion?: { nivel: string; titulo: string; razones: string[] };
  tuUltimo?: { precio: number; fecha: string } | null; litrosTipicos?: number; ahorroDeposito?: number; datosDe?: string; antiguo?: boolean;
}

// «Dónde repostar»: las gasolineras más baratas a tu alrededor (datos públicos del Ministerio) y si es buen momento según tu último repostaje y la semana
@Component({
  selector: 'app-donde-repostar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrls: ['./comun.css', './dialogos.css'],
  template: `
    <section class="caja repostar">
      <header class="caja__cab"><h3>Dónde repostar</h3><button class="enlace" type="button" (click)="abrir()">Ajustes →</button></header>
      @if (r(); as d) {
        @if (d.sinPunto) {
          <p class="sub">Dime desde dónde buscar (tu casa, por ejemplo) y te enseño las gasolineras más baratas cerca.</p>
          <div><button class="button" type="button" (click)="abrir()">Elegir punto</button></div>
        } @else if (d.sinDatos) {
          <p class="sub">Ahora mismo no puedo consultar los precios del Ministerio. Lo reintento en un rato.</p>
        } @else {
          @if (d.valoracion; as v) {
            <div class="veredicto veredicto--{{ v.nivel }}">
              <strong>{{ v.titulo }}</strong>
              <ul>@for (x of v.razones; track x) { <li>{{ x }}</li> }</ul>
            </div>
          }
          @if ((d.ahorroDeposito ?? 0) >= 0.5) {
            <p class="sub">Llenando unos {{ numero(d.litrosTipicos ?? 0, 0) }} L ahorras unos <b>{{ euros(d.ahorroDeposito ?? 0) }}</b> en la más barata.</p>
          }
          <ol class="lista">
            @for (e of d.estaciones ?? []; track e.lat + ',' + e.lon) {
              <li>
                <a class="est" target="_blank" rel="noopener" [href]="mapa(e)">
                  <span class="est__precio">{{ numero(e.precio, 3) }}<small> €/L</small></span>
                  <span class="est__nombre">{{ e.rotulo }}<small>{{ e.direccion }}, {{ e.localidad }}</small></span>
                  <span class="est__km">{{ numero(e.km, 1) }} km</span>
                </a>
              </li>
            } @empty { <li class="sub">Ninguna gasolinera con {{ d.nombreCombustible }} en {{ d.ajustes.radio }} km.</li> }
          </ol>
          <p class="sub pie">{{ d.nombreCombustible }} · cerca de {{ d.punto?.nombre }}
            @if (d.mediaProvincia) { · mediana en {{ d.provincia }}: {{ numero(d.mediaProvincia, 3) }} €/L }
            @if (d.tuUltimo) { · tu último repostaje: {{ numero(d.tuUltimo.precio, 3) }} €/L }
            · datos de {{ hace(d.datosDe) }}@if (d.antiguo) { (antiguos) }</p>
        }
      } @else if (fallo()) { <p class="sub">No se pudo consultar.</p> }
      @else { <p class="sub" aria-busy="true">Buscando…</p> }
    </section>

    <dialog class="dlg" #dlg (click)="fuera($event)" aria-labelledby="dlg-gas-t">
      <form class="dlg__caja" (submit)="guardar($event)" novalidate>
        <header class="dlg__cabeza"><h2 id="dlg-gas-t">Dónde repostar</h2>
          <button class="dlg__x" type="button" (click)="dlg.close()" aria-label="Cerrar"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6l12 12M18 6 6 18" /></svg></button></header>
        <label class="ajuste"><span>Combustible</span>
          <select class="campo__input" (change)="combustible.set($any($event.target).value)">
            @for (c of r()?.combustibles ?? []; track c.id) { <option [value]="c.id" [selected]="c.id === combustible()">{{ c.nombre }}</option> }
          </select></label>
        <label class="ajuste"><span>Radio: {{ radio() }} km</span>
          <input type="range" min="1" max="50" [value]="radio()" (input)="radio.set(+$any($event.target).value)"></label>
        <div class="ajuste"><span>Punto de búsqueda</span>
          <p class="dlg__lead">{{ punto() ? punto()!.nombre : 'El lugar de tu tiempo' }}</p>
          <div class="botones">
            <button class="button button--ghost" type="button" (click)="ubicar()">Usar mi ubicación</button>
            @if (punto()) { <button class="button button--ghost" type="button" (click)="punto.set(null)">Usar el del tiempo</button> }
          </div></div>
        <label class="ajuste ajuste--check"><input type="checkbox" [checked]="aviso()" (change)="aviso.set($any($event.target).checked)"> <span>Avisarme si baja 4 céntimos o más respecto a la semana pasada</span></label>
        @if (error()) { <p class="error">{{ error() }}</p> }
        <footer class="dlg__pie">
          <button class="button button--ghost" type="button" (click)="dlg.close()">Cancelar</button>
          <button class="button" type="submit">Guardar</button>
        </footer>
      </form>
    </dialog>
  `,
  styles: `
    .repostar { display: flex; flex-direction: column; gap: 12px; }
    .sub { color: var(--soft); font-size: var(--step-small); margin: 0; }
    .sub b { color: var(--texto); }
    .veredicto { padding: 12px 14px; border-radius: 14px; background: var(--tarjeta-2); border-left: 4px solid var(--soft); }
    .veredicto strong { font-size: 1.05rem; }
    .veredicto ul { margin: 6px 0 0; padding-left: 18px; color: var(--soft); font-size: var(--step-small); }
    .veredicto--bueno { border-left-color: var(--ok); }
    .veredicto--sube { border-left-color: #ff7a1f; }
    .veredicto--caro { border-left-color: var(--bad); }
    .lista { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 6px; }
    .est { display: grid; grid-template-columns: 92px minmax(0, 1fr) auto; gap: 12px; align-items: center; padding: 10px 12px; border-radius: 12px; background: var(--tarjeta-2); color: var(--texto); text-decoration: none; min-height: 48px; }
    .est__precio { font: 600 1.1rem/1 var(--font-code); }
    .est__precio small, .est__nombre small { color: var(--soft); font: 400 .75rem/1.3 var(--font-text); }
    .est__nombre { display: flex; flex-direction: column; min-width: 0; }
    .est__nombre small { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .est__km { color: var(--soft); font-size: var(--step-small); }
    .pie { line-height: 1.5; }
    .botones { display: flex; gap: 8px; flex-wrap: wrap; }
    .ajuste { display: flex; flex-direction: column; gap: 6px; font-size: var(--step-small); }
    .ajuste--check { flex-direction: row; align-items: center; gap: 10px; }
    @media (max-width: 560px) { .est { grid-template-columns: 78px minmax(0, 1fr) auto; gap: 8px; } }
  `,
})
export class DondeRepostar {
  private readonly http = inject(HttpClient);
  private readonly g = inject(GastosService);
  protected readonly dlg = viewChild.required<ElementRef<HTMLDialogElement>>('dlg');
  protected readonly r = signal<Respuesta | null>(null);
  protected readonly fallo = signal(false);
  protected readonly error = signal('');
  protected readonly combustible = signal('gasolina95');
  protected readonly radio = signal(10);
  protected readonly aviso = signal(false);
  protected readonly punto = signal<Punto | null>(null);
  protected readonly euros = euros;
  protected readonly numero = numero;

  constructor() {
    effect(() => { this.g.version(); untracked(() => void this.cargar()); });
  }

  private async cargar(): Promise<void> {
    try { this.r.set(await firstValueFrom(this.http.get<Respuesta>('/api/gastos/combustible'))); this.fallo.set(false); }
    catch { this.fallo.set(true); }
  }

  protected mapa(e: Estacion): string { return `https://www.google.com/maps/search/?api=1&query=${e.lat},${e.lon}`; }

  protected hace(iso?: string): string {
    if (!iso) return '—';
    const min = Math.max(0, Math.round((Date.now() - new Date(iso).getTime()) / 60000));
    return min < 60 ? `hace ${min} min` : min < 1440 ? `hace ${Math.round(min / 60)} h` : `hace ${Math.round(min / 1440)} d`;
  }

  protected abrir(): void {
    const a = this.r()?.ajustes;
    if (a) { this.combustible.set(a.combustible); this.radio.set(a.radio); this.aviso.set(a.aviso); this.punto.set(a.punto); }
    this.error.set('');
    this.dlg().nativeElement.showModal();
  }

  protected ubicar(): void {
    if (!navigator.geolocation) { this.error.set('Este navegador no da la ubicación.'); return; }
    navigator.geolocation.getCurrentPosition(
      (p) => { this.punto.set({ nombre: 'Mi ubicación', lat: +p.coords.latitude.toFixed(4), lon: +p.coords.longitude.toFixed(4) }); this.error.set(''); },
      () => this.error.set('No he podido obtener tu ubicación.'));
  }

  protected async guardar(e: Event): Promise<void> {
    e.preventDefault();
    try {
      await firstValueFrom(this.http.put('/api/gastos/combustible', { combustible: this.combustible(), radio: this.radio(), punto: this.punto(), aviso: this.aviso() }));
      this.dlg().nativeElement.close();
      await this.cargar();
    } catch { this.error.set('No se pudo guardar.'); }
  }

  protected fuera(e: MouseEvent): void { const d = e.target as HTMLElement; if (d.tagName === 'DIALOG') (d as HTMLDialogElement).close(); }
}
