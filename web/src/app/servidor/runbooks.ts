import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { ConfirmarService } from '../core/confirmar.service';
import { erroresDe } from '../gastos/gastos.service';
import {
  COMPROBACIONES, Ejecucion, EntradaRunbook, ORDENES, ORDENES_DELICADAS, PasoRunbook, Runbook, ServidorService, TIPOS_PASO, TipoPaso, conPermisoSiHaceFalta, hace, pasoNuevo,
} from './servidor.service';

const ICONO_ESTADO: Record<string, string> = { pendiente: '·', 'en-curso': '…', esperando: '⏸', ok: '✓', error: '✕', omitido: '–' };

// Runbooks: secuencias de pasos con nombre («Comprobar todo», «Mantenimiento de noche»). Se lanzan desde aquí, desde Telegram (/ejecuta) o desde un botón
// de la guardia. Se ven en vivo, y los pasos que cortan lo que haces en el PC se paran hasta que los permitas con Face ID.
@Component({
  selector: 'app-runbooks',
  styleUrls: ['../gastos/comun.css'],
  template: `
    @if (borrador(); as b) {
      <form class="caja editor" (submit)="guardar($event)" novalidate>
        <header class="caja__cab"><h3>{{ editandoId() ? 'Editar runbook' : 'Nuevo runbook' }}</h3></header>
        <label class="campo"><span class="campo__etiqueta">Nombre</span>
          <input class="campo__input" maxlength="60" [value]="b.nombre" (input)="poner('nombre', $any($event.target).value)" placeholder="Mantenimiento de noche"></label>
        <label class="campo"><span class="campo__etiqueta">Para qué sirve (opcional)</span>
          <input class="campo__input" maxlength="200" [value]="b.descripcion" (input)="poner('descripcion', $any($event.target).value)"></label>
        <label class="check"><input type="checkbox" [checked]="b.guardia" (change)="poner('guardia', $any($event.target).checked)"> Ofrecerlo como botón cuando la guardia dé una alarma</label>

        <ol class="pasos-edit">
          @for (p of b.pasos; track $index; let i = $index) {
            <li class="paso-edit">
              <div class="paso-edit__cab">
                <span class="n">{{ i + 1 }}</span>
                <select class="campo__input" (change)="cambiarTipo(i, $any($event.target).value)" [attr.aria-label]="'Tipo del paso ' + (i + 1)">
                  @for (t of tipos; track t.id) { <option [value]="t.id" [selected]="p.tipo === t.id">{{ t.nombre }}</option> }
                </select>
                <button type="button" class="mini" (click)="mover(i, -1)" [disabled]="i === 0" aria-label="Subir el paso">↑</button>
                <button type="button" class="mini" (click)="mover(i, 1)" [disabled]="i === b.pasos.length - 1" aria-label="Bajar el paso">↓</button>
                <button type="button" class="mini mini--peligro" (click)="quitar(i)" aria-label="Quitar el paso">✕</button>
              </div>
              @switch (p.tipo) {
                @case ('aviso') { <input class="campo__input" maxlength="300" placeholder="Qué te digo" [value]="p.texto ?? ''" (input)="paso(i, { texto: $any($event.target).value })"> }
                @case ('espera') { <label class="campo en-linea"><span class="campo__etiqueta">Segundos</span><input class="campo__input corto" type="number" min="1" max="300" [value]="p.segundos ?? 5" (input)="paso(i, { segundos: +$any($event.target).value })"></label> }
                @case ('comprobar') {
                  <select class="campo__input" (change)="paso(i, { valor: $any($event.target).value })" aria-label="Qué comprobar">
                    @for (c of comprobaciones; track c.id) { <option [value]="c.id" [selected]="p.valor === c.id">{{ c.nombre }}</option> }
                  </select>
                }
                @case ('despertar') { <input class="campo__input" maxlength="60" placeholder="Nombre del aparato (vacío = el PC principal)" [value]="p.valor ?? ''" (input)="paso(i, { valor: $any($event.target).value })"> }
                @case ('escena') { <input class="campo__input" maxlength="60" placeholder="Nombre de la escena" [value]="p.valor ?? ''" (input)="paso(i, { valor: $any($event.target).value })"> }
                @case ('casa') {
                  <select class="campo__input" (change)="paso(i, { valor: $any($event.target).value })" aria-label="Llegar o salir">
                    <option value="llegada" [selected]="p.valor === 'llegada'">Llegué a casa</option><option value="salida" [selected]="p.valor === 'salida'">Me voy de casa</option>
                  </select>
                }
                @case ('orden') {
                  <select class="campo__input" (change)="paso(i, { valor: $any($event.target).value })" aria-label="Orden al PC">
                    @for (o of ordenes; track o.id) { <option [value]="o.id" [selected]="p.valor === o.id">{{ o.nombre }}</option> }
                  </select>
                }
              }
              <div class="opciones">
                <label class="check"><input type="checkbox" [checked]="p.aprobar" [disabled]="obligaPermiso(p)" (change)="paso(i, { aprobar: $any($event.target).checked })"> Pedir permiso con Face ID{{ obligaPermiso(p) ? ' (obligatorio)' : '' }}</label>
                <label class="check"><input type="checkbox" [checked]="p.continuar" (change)="paso(i, { continuar: $any($event.target).checked })"> Seguir si falla</label>
              </div>
            </li>
          }
        </ol>
        <div class="anadir">
          <select class="campo__input" #nuevoTipo aria-label="Tipo de paso nuevo">@for (t of tipos; track t.id) { <option [value]="t.id">{{ t.nombre }} — {{ t.ayuda }}</option> }</select>
          <button type="button" class="button button--ghost" (click)="anadir($any(nuevoTipo).value)">Añadir paso</button>
        </div>
        @for (e of errores(); track e) { <p class="error" role="alert">{{ e }}</p> }
        <footer class="acciones">
          <button class="button" type="submit" [disabled]="ocupado()">{{ ocupado() ? 'Guardando…' : 'Guardar' }}</button>
          <button class="button button--ghost" type="button" (click)="cerrar()">Cancelar</button>
        </footer>
      </form>
    } @else {
      <section class="caja">
        <header class="caja__cab"><h3>Runbooks</h3><span class="sub">Secuencias que se ejecutan solas, con tu permiso donde importa</span></header>
        <button class="button" type="button" (click)="nuevo()">Nuevo runbook</button>
      </section>
    }

    @if (aviso()) { <p class="aviso" role="status">{{ aviso() }}</p> }

    @for (r of lista(); track r.id) {
      <article class="caja runbook">
        <header class="runbook__cab">
          <div><strong>{{ r.nombre }}</strong> @if (r.guardia) { <span class="etiqueta">de guardia</span> }
            @if (r.descripcion) { <p class="desc">{{ r.descripcion }}</p> }</div>
          <div class="acciones">
            <button class="button" type="button" (click)="ejecutar(r)" [disabled]="enCurso(r.id)">{{ enCurso(r.id) ? 'En marcha…' : 'Ejecutar' }}</button>
            <button class="button button--ghost" type="button" (click)="editar(r)">Editar</button>
            @if (borrando() === r.id) { <button class="button button--ghost peligro" type="button" (click)="borrar(r)">¿Borrar?</button> }
            @else { <button class="button button--ghost" type="button" (click)="borrando.set(r.id)" [attr.aria-label]="'Borrar ' + r.nombre">Borrar</button> }
          </div>
        </header>
        <ol class="resumen">@for (p of r.pasos; track $index) { <li>{{ p.resumen }}@if (p.aprobar) { <em> · pide permiso</em> }@if (p.continuar) { <em> · sigue si falla</em> }</li> }</ol>
        @if (r.ultima; as u) { <small class="ultima">Última vez: {{ u.estado }} · {{ hace(u.inicio) }}</small> }
      </article>
    }

    @if (ejecuciones().length) {
      <section class="caja">
        <header class="caja__cab"><h3>Ejecuciones</h3><span class="sub">En vivo y las últimas</span></header>
        @for (x of ejecuciones(); track x.id) {
          <div class="ejec" [attr.data-estado]="x.estado">
            <div class="ejec__cab">
              <strong>{{ x.nombre }}</strong>
              <span class="estado-tag" [attr.data-estado]="x.estado">{{ x.estado }}</span>
              <small>{{ x.origen }} · {{ hace(x.inicio) }}</small>
            </div>
            <ol class="ejec__pasos">
              @for (p of x.pasos; track $index) {
                <li [attr.data-estado]="p.estado"><span class="marca" aria-hidden="true">{{ marca(p.estado) }}</span>
                  <div><span>{{ p.resumen }}</span>@if (p.salida) { <small>{{ p.salida }}</small> }</div></li>
              }
            </ol>
            @if (x.estado === 'esperando') {
              <div class="acciones">
                <button class="button" type="button" (click)="permitir(x)">Permitir con Face ID</button>
                <button class="button button--ghost" type="button" (click)="cancelar(x)">Cancelar</button>
              </div>
            } @else if (x.estado === 'en-curso') {
              <div class="acciones"><button class="button button--ghost" type="button" (click)="cancelar(x)">Cancelar</button></div>
            }
          </div>
        }
      </section>
    }
  `,
  styles: `
    :host { display: grid; gap: 14px; }
    .editor { display: grid; gap: 12px; }
    .check { display: flex; align-items: center; gap: 8px; font-size: .88rem; min-height: 32px; }
    .pasos-edit, .resumen, .ejec__pasos { list-style: none; margin: 0; padding: 0; display: grid; gap: 8px; }
    .paso-edit { display: grid; gap: 8px; padding: 12px; border-radius: 14px; border: 1px solid var(--vidrio-borde); background: color-mix(in srgb, var(--texto) 3%, transparent); }
    .paso-edit__cab { display: flex; align-items: center; gap: 8px; }
    .paso-edit__cab select { flex: 1; min-width: 0; }
    .n { display: grid; place-items: center; width: 26px; height: 26px; border-radius: 50%; background: var(--brand); color: var(--on-brand); font: 700 .78rem/1 var(--font-code); flex: none; }
    .mini { min-width: 36px; min-height: 36px; border-radius: 10px; border: 1px solid var(--vidrio-borde); background: none; color: var(--texto); cursor: pointer; }
    .mini:disabled { opacity: .35; cursor: default; } .mini--peligro { color: var(--bad); }
    .opciones { display: flex; flex-wrap: wrap; gap: 4px 16px; }
    .en-linea { display: flex; align-items: center; gap: 10px; } .corto { width: 90px; }
    .anadir { display: flex; gap: 8px; flex-wrap: wrap; } .anadir select { flex: 1 1 220px; min-width: 0; }
    .runbook__cab { display: flex; flex-wrap: wrap; justify-content: space-between; gap: 10px; }
    .desc { margin: 4px 0 0; color: var(--soft); font-size: .85rem; }
    .etiqueta { margin-left: 6px; padding: 2px 8px; border-radius: 999px; border: 1px solid var(--vidrio-borde); font-size: .7rem; color: var(--soft); vertical-align: 1px; }
    .resumen { margin: 10px 0 0 18px; list-style: decimal; display: block; font-size: .86rem; color: var(--soft); } .resumen li { padding: 2px 0; } .resumen em { font-style: normal; opacity: .75; }
    .ultima { display: block; margin-top: 8px; color: var(--soft); font-size: .78rem; }
    .peligro { color: var(--bad); }
    .aviso { margin: 0; padding: 8px 10px; border-radius: 10px; background: color-mix(in srgb, var(--brand) 12%, transparent); font-size: .88rem; }
    .ejec { padding: 12px 0; border-top: 1px solid var(--vidrio-borde); display: grid; gap: 8px; } .ejec:first-of-type { border-top: 0; }
    .ejec__cab { display: flex; flex-wrap: wrap; align-items: center; gap: 8px 12px; } .ejec__cab small { color: var(--soft); }
    .estado-tag { padding: 2px 10px; border-radius: 999px; font: 600 .74rem/1.5 var(--font-text); background: color-mix(in srgb, var(--soft) 20%, transparent); }
    .estado-tag[data-estado='ok'] { background: color-mix(in srgb, #3ddc84 25%, transparent); } .estado-tag[data-estado='error'] { background: color-mix(in srgb, var(--bad) 25%, transparent); }
    .estado-tag[data-estado='esperando'], .estado-tag[data-estado='en-curso'] { background: color-mix(in srgb, var(--brand) 25%, transparent); }
    .ejec__pasos li { display: grid; grid-template-columns: 22px minmax(0, 1fr); gap: 8px; font-size: .88rem; }
    .ejec__pasos small { display: block; color: var(--soft); overflow-wrap: anywhere; }
    .marca { text-align: center; font-weight: 700; }
    li[data-estado='ok'] .marca { color: #3ddc84; } li[data-estado='error'] .marca { color: var(--bad); } li[data-estado='esperando'] .marca, li[data-estado='en-curso'] .marca { color: var(--brand); }
  `,
})
export class Runbooks implements OnInit {
  private readonly api = inject(ServidorService);
  private readonly confirmador = inject(ConfirmarService);
  protected readonly tipos = TIPOS_PASO;
  protected readonly comprobaciones = COMPROBACIONES;
  protected readonly ordenes = ORDENES;
  protected readonly hace = hace;
  protected readonly lista = signal<Runbook[]>([]);
  protected readonly ejecuciones = signal<Ejecucion[]>([]);
  protected readonly borrador = signal<EntradaRunbook | null>(null);
  protected readonly editandoId = signal<number | null>(null);
  protected readonly borrando = signal<number | null>(null);
  protected readonly ocupado = signal(false);
  protected readonly aviso = signal('');
  protected readonly errores = signal<string[]>([]);
  private readonly activas = computed(() => this.ejecuciones().filter((x) => x.estado === 'en-curso' || x.estado === 'esperando'));
  private temporizador: ReturnType<typeof setTimeout> | undefined;
  private vivo = true;

  constructor() { inject(DestroyRef).onDestroy(() => { this.vivo = false; clearTimeout(this.temporizador); }); }

  ngOnInit(): void { void this.leer(); void this.sondear(); }

  protected marca(estado: string): string { return ICONO_ESTADO[estado] ?? '·'; }
  protected enCurso(runbookId: number): boolean { return this.activas().some((x) => x.runbookId === runbookId); }
  protected obligaPermiso(p: PasoRunbook): boolean { return p.tipo === 'orden' && ORDENES_DELICADAS.includes(p.valor ?? ''); }

  private async leer(): Promise<void> {
    try { this.lista.set(await this.api.runbooks()); } catch { this.aviso.set('No se pudieron leer los runbooks.'); }
  }

  // en vivo: cada 1,5 s mientras algo se ejecuta; si no, cada 10 s (por si lo lanzas desde Telegram)
  private async sondear(): Promise<void> {
    if (!this.vivo) return;
    try { this.ejecuciones.set(await this.api.ejecuciones()); const antes = this.activas().length; if (antes === 0) void this.leer(); } catch { /* sin red: se reintenta */ }
    this.temporizador = setTimeout(() => void this.sondear(), this.activas().length ? 1500 : 10000);
  }

  protected async ejecutar(r: Runbook): Promise<void> {
    this.aviso.set('');
    try { await this.api.ejecutar(r.id); await this.refrescarYa(); }
    catch (x) { this.aviso.set((x as HttpErrorResponse).error?.mensaje ?? 'No se pudo lanzar.'); }
  }

  private async refrescarYa(): Promise<void> { clearTimeout(this.temporizador); await this.sondear(); }

  protected async permitir(x: Ejecucion): Promise<void> {
    for (let intento = 0; intento < 2; intento++) {
      try { await this.api.aprobarPaso(x.id); await this.refrescarYa(); return; }
      catch (e) {
        const h = e as HttpErrorResponse;
        if (h.status === 403 && h.error?.reautenticar && intento === 0) { if (!(await this.confirmador.pedir('Confirma que eres tú para dejar seguir el runbook.'))) return; continue; }
        this.aviso.set(h.error?.mensaje ?? 'No se pudo dar el permiso.'); return;
      }
    }
  }

  protected async cancelar(x: Ejecucion): Promise<void> {
    try { await this.api.cancelar(x.id); await this.refrescarYa(); } catch { this.aviso.set('Ya había terminado.'); await this.refrescarYa(); }
  }

  // ---- editor ----
  protected nuevo(): void { this.editandoId.set(null); this.errores.set([]); this.borrador.set({ nombre: '', descripcion: '', guardia: false, pasos: [pasoNuevo('comprobar')] }); }
  protected editar(r: Runbook): void {
    this.editandoId.set(r.id); this.errores.set([]);
    this.borrador.set({ nombre: r.nombre, descripcion: r.descripcion, guardia: r.guardia, pasos: r.pasos.map((p) => ({ tipo: p.tipo, texto: p.texto ?? null, valor: p.valor ?? null, segundos: p.segundos ?? null, aprobar: p.aprobar, continuar: p.continuar })) });
  }
  protected cerrar(): void { this.borrador.set(null); this.editandoId.set(null); this.errores.set([]); }
  protected poner<K extends keyof EntradaRunbook>(campo: K, valor: EntradaRunbook[K]): void { this.borrador.update((b) => (b ? { ...b, [campo]: valor } : b)); }
  protected anadir(tipo: string): void { this.borrador.update((b) => (b ? { ...b, pasos: [...b.pasos, pasoNuevo(tipo as TipoPaso)] } : b)); }
  protected quitar(i: number): void { this.borrador.update((b) => (b ? { ...b, pasos: b.pasos.filter((_, k) => k !== i) } : b)); }
  protected cambiarTipo(i: number, tipo: string): void { this.cambiarPaso(i, () => pasoNuevo(tipo as TipoPaso)); }
  protected paso(i: number, cambio: Partial<PasoRunbook>): void { this.cambiarPaso(i, (p) => conPermisoSiHaceFalta({ ...p, ...cambio })); }
  protected mover(i: number, d: number): void {
    this.borrador.update((b) => {
      if (!b || i + d < 0 || i + d >= b.pasos.length) return b;
      const pasos = [...b.pasos]; [pasos[i], pasos[i + d]] = [pasos[i + d], pasos[i]];
      return { ...b, pasos };
    });
  }
  private cambiarPaso(i: number, f: (p: PasoRunbook) => PasoRunbook): void { this.borrador.update((b) => (b ? { ...b, pasos: b.pasos.map((p, k) => (k === i ? f(p) : p)) } : b)); }

  protected async guardar(e: Event): Promise<void> {
    e.preventDefault();
    const b = this.borrador();
    if (!b || this.ocupado()) return;
    this.ocupado.set(true); this.errores.set([]);
    try {
      const id = this.editandoId();
      if (id) await this.api.guardar(id, b); else await this.api.crear(b);
      this.cerrar(); await this.leer();
    } catch (x) { this.errores.set(Object.values(erroresDe(x)).flat()); }
    finally { this.ocupado.set(false); }
  }

  protected async borrar(r: Runbook): Promise<void> {
    this.borrando.set(null);
    try { await this.api.borrar(r.id); await this.leer(); } catch { this.aviso.set('No se pudo borrar.'); }
  }
}
