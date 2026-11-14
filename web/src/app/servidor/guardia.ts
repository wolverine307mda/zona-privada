import { Component, OnInit, inject, signal } from '@angular/core';
import { Principal, ServidorService } from './servidor.service';

// Modo guardia (alarmas con botones en Telegram) y el PC principal que se despierta con un toque.
@Component({
  selector: 'app-guardia',
  styleUrls: ['../gastos/comun.css'],
  template: `
    <section class="caja">
      <header class="caja__cab"><h3>Modo guardia</h3><span class="sub">Cuando algo falla, el bot te propone qué hacer</span></header>
      <p class="nota">Con la guardia activada, si la API da errores o la app deja de responder desde Internet, el bot de Telegram te manda la alarma <strong>con botones</strong>:
        ejecutar un runbook de guardia, silenciarla una hora, ignorarla o el pánico. Apagada, las alarmas siguen llegando como siempre, solo que sin botones. También: <code>/guardia on</code> y <code>/guardia off</code> en Telegram.</p>
      @if (estado(); as e) {
        <div class="fila">
          <label class="interruptor"><input type="checkbox" [checked]="e.activa" [disabled]="ocupado()" (change)="cambiar($any($event.target).checked)">
            <span class="interruptor__pista" aria-hidden="true"></span><span>{{ e.activa ? 'Guardia activada' : 'Guardia apagada' }}</span></label>
          <button class="button button--ghost" type="button" [disabled]="!e.activa || !e.telegram || ocupado()" (click)="probar()">Mandarme una alarma de prueba</button>
        </div>
        @if (!e.telegram) { <p class="aviso">Telegram no está vinculado: la guardia habla por ahí (Seguridad › Avisos).</p> }
        @else if (e.runbooks === 0) { <p class="nota">Aún no hay ningún runbook marcado «de guardia»: los botones solo llevarán silenciar, ignorar y pánico. Márcalos en Runbooks.</p> }
        @else { <p class="nota">{{ e.runbooks }} {{ e.runbooks === 1 ? 'runbook sale' : 'runbooks salen' }} como botón (hasta 3).</p> }
      }
      @if (mensaje()) { <p class="estado" role="status">{{ mensaje() }}</p> }
    </section>

    <section class="caja">
      <header class="caja__cab"><h3>PC principal</h3><span class="sub">Despertarlo con un toque, aunque esté apagado</span></header>
      <p class="nota">Un PC apagado no puede despertarse a sí mismo; el mini PC, que está siempre encendido, sí (Wake-on-LAN). Elige cuál es tu PC principal entre los aparatos de Red que tienen dirección física (MAC).
        Después se despierta desde aquí, con <code>/enciende</code> en Telegram o con el atajo «Encender el PC» de Siri sin decir nombre.</p>
      <div class="fila">
        <label class="campo"><span class="campo__etiqueta">Mi PC principal</span>
          <select class="campo__input" (change)="elegir($any($event.target).value)" [disabled]="ocupado()">
            <option value="" [selected]="!principal()">— ninguno —</option>
            @for (a of aparatos(); track a.id) { <option [value]="a.id" [selected]="principal()?.id === a.id">{{ a.nombre }} ({{ a.mac }})</option> }
          </select></label>
        <button class="button" type="button" [disabled]="!principal() || ocupado()" (click)="despertar()">
          {{ principal() ? 'Despertar «' + principal()!.nombre + '»' : 'Despertar' }}</button>
      </div>
      @if (principal(); as p) { <p class="nota">Ahora mismo: <strong>{{ p.enLinea === true ? 'encendido' : p.enLinea === false ? 'apagado o sin responder' : 'sin comprobar' }}</strong>.</p> }
      @if (aparatos().length === 0) { <p class="nota">Ningún aparato de Red tiene MAC todavía: añádela en Mi PC › Red.</p> }
      @if (despertado()) { <p class="estado" role="status">{{ despertado() }}</p> }
    </section>
  `,
  styles: `
    :host { display: grid; gap: 14px; }
    .nota { margin: 8px 0; color: var(--soft); font-size: .86rem; line-height: 1.5; }
    .nota code { font-size: .85em; }
    .aviso { margin: 8px 0; padding: 8px 10px; border-radius: 10px; background: color-mix(in srgb, var(--bad) 12%, transparent); font-size: .85rem; }
    .fila { display: flex; flex-wrap: wrap; align-items: flex-end; gap: 12px; margin: 10px 0; }
    .fila .campo { min-width: 220px; flex: 1 1 220px; }
    .estado { margin: 8px 0 0; font-size: .88rem; color: var(--soft); }
    .interruptor { display: flex; align-items: center; gap: 10px; min-height: 44px; font-weight: 600; cursor: pointer; }
    .interruptor input { width: 22px; height: 22px; accent-color: var(--brand); }
    .interruptor__pista { display: none; }
  `,
})
export class Guardia implements OnInit {
  private readonly api = inject(ServidorService);
  protected readonly estado = signal<{ activa: boolean; telegram: boolean; runbooks: number } | null>(null);
  protected readonly principal = signal<Principal | null>(null);
  protected readonly aparatos = signal<{ id: number; nombre: string; mac: string }[]>([]);
  protected readonly ocupado = signal(false);
  protected readonly mensaje = signal('');
  protected readonly despertado = signal('');

  ngOnInit(): void { void this.leer(); }

  private async leer(): Promise<void> {
    try { this.estado.set(await this.api.guardia()); } catch { this.mensaje.set('No se pudo leer el estado de la guardia.'); }
    try {
      this.principal.set((await this.api.principal()).principal);
      this.aparatos.set((await this.api.aparatos()).filter((a) => a.mac));
    } catch { /* se queda como estaba */ }
  }

  protected async cambiar(activa: boolean): Promise<void> {
    this.ocupado.set(true); this.mensaje.set('');
    try { await this.api.ponerGuardia(activa); this.estado.update((e) => (e ? { ...e, activa } : e)); }
    catch { this.mensaje.set('No se pudo cambiar la guardia.'); await this.leer(); }
    finally { this.ocupado.set(false); }
  }

  protected async probar(): Promise<void> {
    this.ocupado.set(true); this.mensaje.set('');
    try { this.mensaje.set((await this.api.probarGuardia()).mensaje); }
    catch (x) { this.mensaje.set((x as { error?: { mensaje?: string } }).error?.mensaje ?? 'No se pudo mandar la prueba.'); }
    finally { this.ocupado.set(false); }
  }

  protected async elegir(valor: string): Promise<void> {
    this.ocupado.set(true); this.despertado.set('');
    try { await this.api.ponerPrincipal(valor ? Number(valor) : null); this.principal.set((await this.api.principal()).principal); }
    catch { this.despertado.set('No se pudo guardar la elección.'); await this.leer(); }
    finally { this.ocupado.set(false); }
  }

  protected async despertar(): Promise<void> {
    this.ocupado.set(true); this.despertado.set('');
    try { this.despertado.set((await this.api.despertarPrincipal()).mensaje); }
    catch (x) { this.despertado.set((x as { error?: { mensaje?: string } }).error?.mensaje ?? 'No se pudo despertar.'); }
    finally { this.ocupado.set(false); }
  }
}
