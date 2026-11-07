import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, ElementRef, OnDestroy, inject, signal, viewChild } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ConfirmarService } from '../core/confirmar.service';

interface AppCatalogo { clave: string; nombre: string; appId: string; delicada: boolean; }
interface AppDisponible { nombre: string; appId: string; }
interface Proceso { pid: number; inicioUnix: number; nombre: string; titulo: string; memoriaMb: number; protegido: boolean; }
interface Elegida { appId: string; nombre: string; delicada: boolean; marcada: boolean; }

// Aplicaciones del PC: abrir las que tú eliges (Claude, Chrome, Código…) y cerrar las que tienen ventana abierta. El PC manda la lista del menú Inicio,
// tú marcas cuáles se pueden abrir desde aquí; las «delicadas» piden Face ID o contraseña. Cerrar es siempre amable primero («Forzar» después) y los
// programas del sistema, la zona privada y la red están protegidos.
@Component({
  selector: 'app-apps-pc',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrls: ['../gastos/comun.css', '../gastos/dialogos.css'],
  template: `
    <section class="caja apps">
      <header class="caja__cab"><h3>Aplicaciones</h3><button class="enlace" type="button" (click)="elegir()">Elegir cuáles →</button></header>
      @if (catalogo().length === 0) {
        <p class="sub">Aún no has elegido ninguna. «Elegir cuáles» lee las aplicaciones del PC y te deja marcar las que quieres poder abrir desde aquí.</p>
      } @else {
        <div class="botones">
          @for (a of catalogo(); track a.clave) {
            <button class="button button--ghost" type="button" (click)="abrir(a)" [disabled]="ocupado()">Abrir {{ a.nombre }}@if (a.delicada) { <small> 🔒</small> }</button>
          }
        </div>
      }
      @if (aviso()) { <p class="aviso" role="status">{{ aviso() }}</p> }

      <header class="caja__cab sub-cab"><h3>Abiertas ahora</h3><button class="enlace" type="button" (click)="actualizar()" [disabled]="buscando()">{{ buscando() ? 'Mirando…' : 'Actualizar' }}</button></header>
      @if (procesos().length === 0) { <p class="sub">Pulsa «Actualizar» para ver qué aplicaciones tienen ventana abierta en el PC.</p> }
      <ul class="lista">
        @for (p of procesos(); track p.pid) {
          <li>
            <span class="lista__texto"><strong>{{ p.nombre }}</strong><span>{{ p.titulo }} · {{ p.memoriaMb }} MB</span></span>
            @if (p.protegido) { <span class="sub">protegido</span> }
            @else {
              <span class="acciones">
                <button class="enlace" type="button" (click)="cerrar(p, false)" [disabled]="ocupado()">Cerrar</button>
                <button class="enlace enlace--mal" type="button" (click)="cerrar(p, true)" [disabled]="ocupado()">Forzar</button>
              </span>
            }
          </li>
        }
      </ul>
    </section>

    <dialog class="dlg" #dlg (click)="fuera($event)" aria-labelledby="dlg-apps-t">
      <div class="dlg__caja">
        <header class="dlg__cabeza"><h2 id="dlg-apps-t">Qué se puede abrir</h2>
          <button class="dlg__x" type="button" (click)="dlg.close()" aria-label="Cerrar"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6l12 12M18 6 6 18" /></svg></button></header>
        <p class="dlg__lead">Marca las aplicaciones que quieres abrir desde la app. Las «delicadas» piden Face ID o contraseña cada vez.</p>
        <button class="button button--ghost" type="button" (click)="releer()" [disabled]="buscando()">{{ buscando() ? 'Leyendo el menú Inicio…' : 'Volver a leer el PC' }}</button>
        <ul class="elegir">
          @for (e of elegidas(); track e.appId) {
            <li><label><input type="checkbox" [checked]="e.marcada" (change)="marcar(e, $any($event.target).checked)"> <span>{{ e.nombre }}</span></label>
              @if (e.marcada) { <label class="delicada"><input type="checkbox" [checked]="e.delicada" (change)="delicada(e, $any($event.target).checked)"> delicada</label> }</li>
          } @empty { <li class="sub">Aún no ha llegado la lista del PC. Pulsa «Volver a leer el PC».</li> }
        </ul>
        @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
        <footer class="dlg__pie"><button class="button" type="button" (click)="guardar()">Guardar</button></footer>
      </div>
    </dialog>
  `,
  styles: `
    :host { display: block; }
    .apps { display: flex; flex-direction: column; gap: 12px; }
    .sub { color: var(--soft); font-size: .85rem; margin: 0; }
    .sub-cab { margin-top: 8px; }
    .botones { display: flex; flex-wrap: wrap; gap: 8px; }
    .aviso { margin: 0; padding: 8px 12px; border-radius: 10px; background: var(--tarjeta-2); font-size: .88rem; }
    .lista, .elegir { list-style: none; margin: 0; padding: 0; display: grid; gap: 4px; }
    .lista li { display: grid; grid-template-columns: minmax(0, 1fr) auto; gap: 10px; align-items: center; padding: 8px 4px; border-bottom: 1px solid var(--linea); }
    .lista__texto { display: grid; min-width: 0; }
    .lista__texto span { color: var(--soft); font-size: .8rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .acciones { display: flex; gap: 14px; }
    .enlace { background: none; border: 0; color: var(--brand); font: inherit; cursor: pointer; min-height: 40px; }
    .enlace--mal { color: var(--bad, #ff5a5a); }
    .elegir { max-height: 46vh; overflow: auto; }
    .elegir li { display: flex; justify-content: space-between; align-items: center; gap: 10px; min-height: 40px; }
    .elegir label { display: flex; align-items: center; gap: 8px; }
    .delicada { color: var(--soft); font-size: .8rem; }
  `,
})
export class AppsPc implements OnDestroy {
  private readonly http = inject(HttpClient);
  private readonly confirmador = inject(ConfirmarService);
  protected readonly dlg = viewChild.required<ElementRef<HTMLDialogElement>>('dlg');
  protected readonly catalogo = signal<AppCatalogo[]>([]);
  protected readonly disponibles = signal<AppDisponible[]>([]);
  protected readonly elegidas = signal<Elegida[]>([]);
  protected readonly procesos = signal<Proceso[]>([]);
  protected readonly aviso = signal('');
  protected readonly error = signal('');
  protected readonly ocupado = signal(false);
  protected readonly buscando = signal(false);
  private sondeo?: ReturnType<typeof setInterval>;

  constructor() { void this.cargar(); }

  private async cargar(): Promise<void> {
    try {
      const r = await firstValueFrom(this.http.get<{ catalogo: AppCatalogo[]; disponibles: AppDisponible[] }>('/api/pc/apps'));
      this.catalogo.set(r.catalogo); this.disponibles.set(r.disponibles);
    } catch { /* sin catálogo todavía */ }
  }

  // una orden que pide Face ID o contraseña: si el servidor lo pide, se confirma y se repite una vez
  private async conConfirmacion(accion: () => Promise<unknown>, motivo: string): Promise<boolean> {
    for (let intento = 0; intento < 2; intento++) {
      try { await accion(); return true; }
      catch (err) {
        const x = err as HttpErrorResponse;
        if (x.status === 403 && x.error?.reautenticar && intento === 0) { if (!(await this.confirmador.pedir(motivo))) return false; continue; }
        this.aviso.set(x.status === 409 ? (x.error?.detalle ?? 'El agente de Windows no está en marcha.') : (x.error?.errors ? (Object.values(x.error.errors)[0] as string[])[0] : 'No se pudo enviar.'));
        return false;
      }
    }
    return false;
  }

  protected async abrir(a: AppCatalogo): Promise<void> {
    this.ocupado.set(true); this.aviso.set('');
    if (await this.conConfirmacion(() => firstValueFrom(this.http.post(`/api/pc/apps/${a.clave}/abrir`, null)), `Confirma que eres tú para abrir ${a.nombre} en el PC.`)) this.aviso.set(`Enviado al PC: abrir ${a.nombre}.`);
    this.ocupado.set(false);
  }

  protected async cerrar(p: Proceso, forzar: boolean): Promise<void> {
    this.ocupado.set(true); this.aviso.set('');
    if (await this.conConfirmacion(() => firstValueFrom(this.http.post('/api/pc/procesos/cerrar', { pid: p.pid, inicio: p.inicioUnix, forzar })), `Confirma que eres tú para ${forzar ? 'forzar el cierre de' : 'cerrar'} ${p.nombre}.`))
      this.aviso.set(`Enviado al PC: ${forzar ? 'forzar el cierre de' : 'cerrar'} ${p.nombre}. Pulsa «Actualizar» en unos segundos.`);
    this.ocupado.set(false);
  }

  // pide al agente la lista y la va mirando hasta que llegue algo distinto (90 s como mucho)
  protected async actualizar(): Promise<void> {
    this.aviso.set(''); this.buscando.set(true);
    const antes = JSON.stringify(this.procesos());
    try { await firstValueFrom(this.http.post('/api/pc/procesos/actualizar', null)); } catch { this.aviso.set('El agente de Windows no está en marcha.'); this.buscando.set(false); return; }
    let intentos = 0;
    clearInterval(this.sondeo);
    this.sondeo = setInterval(async () => {
      intentos++;
      try {
        const l = await firstValueFrom(this.http.get<Proceso[]>('/api/pc/procesos'));
        if (JSON.stringify(l) !== antes || intentos >= 12) { this.procesos.set(l); clearInterval(this.sondeo); this.buscando.set(false); }
      } catch { /* se reintenta */ }
    }, 2500);
  }

  // ---- elegir cuáles se pueden abrir ----
  protected elegir(): void { this.prepararElegidas(); this.error.set(''); this.dlg().nativeElement.showModal(); }

  private prepararElegidas(): void {
    const marcadas = new Map(this.catalogo().map((a) => [a.appId, a]));
    this.elegidas.set(this.disponibles().map((d) => ({ appId: d.appId, nombre: marcadas.get(d.appId)?.nombre ?? d.nombre, delicada: marcadas.get(d.appId)?.delicada ?? false, marcada: marcadas.has(d.appId) })));
  }

  protected marcar(e: Elegida, v: boolean): void { this.elegidas.update((l) => l.map((x) => (x.appId === e.appId ? { ...x, marcada: v } : x))); }
  protected delicada(e: Elegida, v: boolean): void { this.elegidas.update((l) => l.map((x) => (x.appId === e.appId ? { ...x, delicada: v } : x))); }

  protected async releer(): Promise<void> {
    this.buscando.set(true);
    try { await firstValueFrom(this.http.post('/api/pc/apps/actualizar', null)); } catch { this.error.set('El agente de Windows no está en marcha.'); this.buscando.set(false); return; }
    let intentos = 0;
    clearInterval(this.sondeo);
    const antes = this.disponibles().length;
    this.sondeo = setInterval(async () => {
      intentos++;
      await this.cargar();
      if (this.disponibles().length !== antes || intentos >= 12) { clearInterval(this.sondeo); this.buscando.set(false); this.prepararElegidas(); }
    }, 2500);
  }

  protected async guardar(): Promise<void> {
    this.error.set('');
    try {
      await firstValueFrom(this.http.put('/api/pc/apps', this.elegidas().filter((e) => e.marcada).map((e) => ({ appId: e.appId, nombre: e.nombre, delicada: e.delicada }))));
      await this.cargar();
      this.dlg().nativeElement.close();
    } catch { this.error.set('No se pudo guardar (como mucho 40 aplicaciones).'); }
  }

  protected fuera(e: MouseEvent): void { const d = e.target as HTMLElement; if (d.tagName === 'DIALOG') (d as HTMLDialogElement).close(); }
  ngOnDestroy(): void { clearInterval(this.sondeo); }
}
