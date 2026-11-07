import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ConfirmarService } from '../core/confirmar.service';

interface Tabla { tabla: string; bytes: number; filas: number; }
interface Crece { tabla: string; bytes: number; bytesPorDia: number; diasMedidos: number; }
interface Salud {
  baseBytes: number | null; tablas: Tabla[]; crecimiento: Crece[]; copia: { vigilada: boolean; fecha: string | null; horas: number | null; tamano: number };
  errores10min: number; memoriaMb: number; enMarcha: string; recomendaciones: { id: string; texto: string }[]; motor: string;
}
interface Accion { id: string; nombre: string; descripcion: string; afecta: number; unidad: string; disponible: boolean; }

// Salud de la propia app: lo que pesa la base, qué tablas crecen, la copia, los errores y la memoria; y acciones de mantenimiento con vista previa
// (cuántas filas se borrarían) que piden confirmar y la contraseña otra vez. Nunca se ofrece VACUUM FULL, que bloquea la base.
@Component({
  selector: 'app-salud-app',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrls: ['../gastos/comun.css'],
  template: `
    <section class="caja salud">
      <header class="caja__cab"><h3>Salud de la app</h3><button class="enlace" type="button" (click)="cargar()">Actualizar</button></header>
      @if (fallo()) { <p class="sub">No se pudo leer.</p> }
      @else if (s(); as d) {
        <div class="cifras">
          <div><span class="etq">Base de datos</span><strong>{{ d.baseBytes != null ? tamano(d.baseBytes) : '—' }}</strong></div>
          <div><span class="etq">Última copia</span><strong>{{ d.copia.horas != null ? 'hace ' + hace(d.copia.horas) : d.copia.vigilada ? 'ninguna' : 'sin vigilar' }}</strong></div>
          <div><span class="etq">Memoria de la API</span><strong>{{ d.memoriaMb }} MB</strong></div>
          <div><span class="etq">Errores (10 min)</span><strong [class.mal]="d.errores10min >= 5">{{ d.errores10min }}</strong></div>
        </div>
        @for (r of d.recomendaciones; track r.id) { <p class="reco">💡 {{ r.texto }}</p> }
        @if (d.tablas.length) {
          <h4>Tablas más grandes</h4>
          <ol class="tablas">
            @for (t of d.tablas; track t.tabla) {
              <li><span class="nombre">{{ t.tabla }}</span><span class="barra" aria-hidden="true"><i [style.width.%]="porcentaje(t.bytes)"></i></span>
                <span class="valor">{{ tamano(t.bytes) }}</span><small>{{ crecimiento(t.tabla) }}</small></li>
            }
          </ol>
        } @else { <p class="sub">El detalle de tablas solo está con PostgreSQL.</p> }

        <h4>Mantenimiento</h4>
        @for (a of acciones(); track a.id) {
          <div class="accion">
            <span class="accion__texto"><strong>{{ a.nombre }}</strong><small>{{ a.descripcion }}@if (a.unidad) { · {{ a.afecta }} {{ a.unidad }} }</small></span>
            <button class="button button--ghost" type="button" [disabled]="!a.disponible || ocupado()" (click)="pedir(a)">{{ pendiente() === a.id ? 'Seguro: hacerlo' : 'Hacer' }}</button>
          </div>
        }
        @if (aviso()) { <p class="aviso" role="status">{{ aviso() }}</p> }
      } @else { <p class="sub" aria-busy="true">Leyendo…</p> }
    </section>
  `,
  styles: `
    .salud { display: flex; flex-direction: column; gap: 10px; }
    .sub { margin: 0; color: var(--soft); font-size: .85rem; }
    .cifras { display: grid; grid-template-columns: repeat(auto-fit, minmax(130px, 1fr)); gap: 12px; }
    .cifras div { display: flex; flex-direction: column; gap: 2px; }
    .cifras strong { font: 600 1.25rem/1.2 var(--font-display, var(--font-text)); }
    .etq { color: var(--soft); font-size: .72rem; text-transform: uppercase; letter-spacing: .06em; }
    .mal { color: var(--bad, #ff5a5a); }
    .reco { margin: 0; padding: 8px 12px; border-radius: 10px; background: var(--tarjeta-2); font-size: .86rem; line-height: 1.45; }
    h4 { margin: 6px 0 0; font: 500 .9rem/1.2 var(--font-text); }
    .tablas { list-style: none; margin: 0; padding: 0; display: grid; gap: 6px; }
    .tablas li { display: grid; grid-template-columns: minmax(90px, 1.2fr) minmax(50px, 2fr) 70px; gap: 8px; align-items: center; font-size: .82rem; }
    .tablas small { grid-column: 1 / -1; color: var(--soft); margin-top: -4px; }
    .nombre { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-family: var(--font-code); }
    .barra { height: 7px; border-radius: 99px; background: var(--tarjeta-2); overflow: hidden; }
    .barra i { display: block; height: 100%; background: var(--brand); border-radius: 99px; }
    .valor { text-align: right; font-family: var(--font-code); }
    .accion { display: flex; align-items: center; justify-content: space-between; gap: 12px; padding: 6px 0; border-bottom: 1px solid var(--linea); }
    .accion__texto { display: grid; gap: 2px; min-width: 0; }
    .accion__texto small { color: var(--soft); font-size: .78rem; }
    .aviso { margin: 0; padding: 8px 12px; border-radius: 10px; background: var(--tarjeta-2); font-size: .88rem; }
    .enlace { background: none; border: 0; color: var(--brand); font: inherit; cursor: pointer; min-height: 40px; }
  `,
})
export class SaludApp {
  private readonly http = inject(HttpClient);
  private readonly confirmador = inject(ConfirmarService);
  protected readonly s = signal<Salud | null>(null);
  protected readonly acciones = signal<Accion[]>([]);
  protected readonly fallo = signal(false);
  protected readonly aviso = signal('');
  protected readonly ocupado = signal(false);
  protected readonly pendiente = signal<string | null>(null);

  constructor() { void this.cargar(); }

  protected async cargar(): Promise<void> {
    this.fallo.set(false);
    try {
      const [s, a] = await Promise.all([firstValueFrom(this.http.get<Salud>('/api/sistema/app')), firstValueFrom(this.http.get<Accion[]>('/api/sistema/app/mantenimiento'))]);
      this.s.set(s); this.acciones.set(a);
    } catch { this.fallo.set(true); }
  }

  protected tamano(b: number): string { return b >= 2 ** 30 ? `${(b / 2 ** 30).toLocaleString('es-ES', { maximumFractionDigits: 1 })} GB` : `${Math.round(b / 2 ** 20)} MB`; }
  protected hace(h: number): string { return h < 1 ? 'unos minutos' : h < 48 ? `${Math.round(h)} h` : `${Math.round(h / 24)} días`; }
  protected porcentaje(b: number): number { return Math.max(2, Math.round(100 * b / Math.max(1, ...(this.s()?.tablas.map((t) => t.bytes) ?? [1])))); }
  protected crecimiento(tabla: string): string {
    const c = this.s()?.crecimiento.find((x) => x.tabla === tabla);
    if (!c || c.diasMedidos < 1) return '';
    return c.bytesPorDia > 1048576 ? `crece ~${Math.round(c.bytesPorDia / 1048576)} MB al día` : c.bytesPorDia < -1048576 ? `encoge ~${Math.round(-c.bytesPorDia / 1048576)} MB al día` : 'estable';
  }

  // dos toques: el primero pide confirmar; después, con la contraseña otra vez si el servidor la pide
  protected async pedir(a: Accion): Promise<void> {
    if (this.pendiente() !== a.id) { this.pendiente.set(a.id); this.aviso.set(`${a.nombre}: ${a.descripcion}${a.unidad ? ` Afecta a ${a.afecta} ${a.unidad}.` : ''} Pulsa otra vez para hacerlo.`); return; }
    this.pendiente.set(null); this.ocupado.set(true); this.aviso.set('');
    try {
      for (let intento = 0; intento < 2; intento++) {
        try { const r = await firstValueFrom(this.http.post<{ mensaje: string }>(`/api/sistema/app/mantenimiento/${a.id}?confirmar=true`, null)); this.aviso.set(r.mensaje); break; }
        catch (err) {
          const x = err as HttpErrorResponse;
          if (x.status === 403 && x.error?.reautenticar && intento === 0) { if (!(await this.confirmador.pedir(`Confirma que eres tú para: ${a.nombre}.`))) break; continue; }
          this.aviso.set(x.error?.detalle ?? 'No se pudo hacer.'); break;
        }
      }
    } finally { this.ocupado.set(false); await this.cargar(); }
  }
}
