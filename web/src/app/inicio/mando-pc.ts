import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, ViewEncapsulation, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ConfirmarService } from '../core/confirmar.service';

const PLANES = [{ id: 'ahorro', nombre: 'Ahorro' }, { id: 'equilibrado', nombre: 'Equilibrado' }, { id: 'rendimiento', nombre: 'Rendimiento' }, { id: 'cine', nombre: 'Cine' }];
type Resultado = 'ok' | 'sin-agente' | 'cancelado' | 'error';

// El mando del PC en Inicio: volumen, brillo, plan de energía, mantener despierto y bloquear. Volumen, brillo y plan son órdenes inocuas; despierto y bloquear
// piden confirmar que eres tú (como en «Mi PC»). Todo va por la lista cerrada de órdenes del agente de Windows: aquí no hay nada nuevo en el servidor.
@Component({
  selector: 'app-mando-pc',
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  imports: [RouterLink],
  template: `
    <header class="mp-cab"><span class="mp-ico"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M3 5h18v11H3zM8 20h8M12 16v4" /></svg></span><h3>Mando del PC</h3>
      <span class="mp-estado" [class.mp-estado--off]="!enLinea()">{{ enLinea() ? 'en línea' : 'sin agente' }}</span></header>
    <div class="mp-ajuste">
      <button type="button" class="mp-ib" [class.mp-ib--mudo]="mudo()" (click)="alternarMudo()" [attr.aria-pressed]="mudo()" aria-label="Silenciar"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M4 9v6h4l5 4V5L8 9zM16 9a4 4 0 0 1 0 6M18.5 6.5a8 8 0 0 1 0 11" /></svg></button>
      <input type="range" min="0" max="100" [value]="vol()" [style.--v.%]="vol()" (input)="vol.set(+$any($event.target).value); mudo.set(false)" (change)="enviar('volumen', vol())" aria-label="Volumen">
      <output>{{ mudo() ? 'mudo' : vol() + ' %' }}</output>
    </div>
    <div class="mp-ajuste">
      <span class="mp-ib mp-ib--ico"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="12" r="4" /><path d="M12 2v2M12 20v2M2 12h2M20 12h2M5 5l1.5 1.5M17.5 17.5L19 19M5 19l1.5-1.5M17.5 6.5L19 5" /></svg></span>
      <input type="range" min="0" max="100" [value]="bri()" [style.--v.%]="bri()" (input)="bri.set(+$any($event.target).value)" (change)="enviar('brillo', bri())" aria-label="Brillo">
      <output>{{ bri() }} %</output>
    </div>
    <div class="mp-seg" role="group" aria-label="Plan de energía">
      @for (p of planes; track p.id) { <button type="button" [class.on]="plan() === p.id" [attr.aria-pressed]="plan() === p.id" (click)="elegirPlan(p.id)">{{ p.nombre }}</button> }
    </div>
    <div class="mp-acc">
      <button type="button" class="mp-sw" [class.on]="despierto()" [attr.aria-pressed]="despierto()" (click)="alternarDespierto()"><span>Mantener despierto</span><i></i></button>
      <button type="button" class="mp-bloq" (click)="bloquear()"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M6 11V8a6 6 0 0 1 12 0v3M5 11h14v10H5z" /></svg>Bloquear</button>
    </div>
    <p class="mp-aviso" role="status" aria-live="polite">{{ aviso() }}</p>
    <a class="mp-mas" routerLink="/pc">Capturas, energía, apagado y más órdenes en «Mi PC» →</a>
  `,
  styles: `
    app-mando-pc { display: flex; flex-direction: column; gap: 14px; padding: 22px; border-radius: var(--radio); border: 1px solid var(--vidrio-borde); background: var(--brillo), var(--vidrio); box-shadow: var(--sombra); color: var(--texto); }
    .mp-cab { display: flex; align-items: center; gap: 10px; } .mp-cab h3 { flex: 1; margin: 0; color: var(--tono-titulo); font: 500 1rem/1.1 var(--font-text); }
    .mp-ico { flex: none; display: grid; place-items: center; width: 38px; height: 38px; border-radius: 50%; background: var(--tono-icono-fondo); color: var(--tono-icono); } .mp-ico .icon { width: 17px; height: 17px; }
    .mp-estado { display: inline-flex; align-items: center; gap: 6px; font-size: 11px; font-weight: 600; color: var(--ok); } .mp-estado::before { content: ''; width: 7px; height: 7px; border-radius: 50%; background: currentColor; } .mp-estado--off { color: var(--soft); }
    .mp-ajuste { display: grid; grid-template-columns: 38px 1fr 52px; align-items: center; gap: 12px; }
    .mp-ib { width: 38px; height: 38px; padding: 0; display: grid; place-items: center; border-radius: 50%; border: 1px solid var(--vidrio-borde); background: var(--vidrio); color: var(--texto); cursor: pointer; } .mp-ib .icon { width: 17px; height: 17px; }
    .mp-ib--mudo { background: var(--bad); color: #fff; border-color: transparent; } .mp-ib--ico { cursor: default; }
    .mp-ajuste output { text-align: right; font: 500 13px var(--font-text); font-variant-numeric: tabular-nums; }
    .mp-ajuste input[type=range] { -webkit-appearance: none; appearance: none; width: 100%; height: 10px; border-radius: 999px; outline: none; cursor: pointer; background: linear-gradient(to right, var(--brand) var(--v, 50%), var(--tarjeta-2) var(--v, 50%)); }
    .mp-ajuste input[type=range]::-webkit-slider-thumb { -webkit-appearance: none; width: 22px; height: 22px; border-radius: 50%; background: #fff; border: 3px solid var(--brand); box-shadow: 0 3px 10px rgba(0,0,0,.25); }
    .mp-ajuste input[type=range]::-moz-range-thumb { width: 16px; height: 16px; border-radius: 50%; background: #fff; border: 3px solid var(--brand); }
    .mp-ajuste input[type=range]:focus-visible { outline: 3px solid var(--acento); outline-offset: 4px; }
    .mp-seg { display: grid; grid-auto-flow: column; grid-auto-columns: 1fr; gap: 4px; padding: 4px; border-radius: 999px; background: var(--tarjeta-2); }
    .mp-seg button { border: 0; background: none; color: var(--texto); border-radius: 999px; padding: 8px 4px; font: 500 12.5px var(--font-text); cursor: pointer; min-height: 36px; }
    .mp-seg button.on { background: var(--tarjeta); box-shadow: var(--sombra-pulsa, 0 2px 8px rgba(0,0,0,.15)); color: var(--tono-titulo); font-weight: 600; }
    .mp-acc { display: grid; grid-template-columns: 1fr auto; gap: 8px; }
    .mp-sw { display: flex; justify-content: space-between; align-items: center; gap: 10px; padding: 10px 14px; border: 0; border-radius: 14px; background: var(--tarjeta-2); color: var(--texto); font: 400 13px var(--font-text); cursor: pointer; text-align: left; }
    .mp-sw i { flex: none; width: 36px; height: 21px; border-radius: 999px; background: color-mix(in srgb, var(--soft) 40%, transparent); position: relative; transition: background .2s; }
    .mp-sw i::after { content: ''; position: absolute; left: 3px; top: 3px; width: 15px; height: 15px; border-radius: 50%; background: #fff; transition: translate .2s; box-shadow: 0 1px 3px rgba(0,0,0,.3); }
    .mp-sw.on i { background: var(--brand); } .mp-sw.on i::after { translate: 15px 0; }
    .mp-bloq { min-height: 44px; justify-content: center; display: inline-flex; align-items: center; gap: 8px; border: 0; border-radius: 14px; padding: 0 16px; background: var(--tarjeta-2); color: var(--texto); font: 500 13px var(--font-text); cursor: pointer; } .mp-bloq .icon { width: 18px; height: 18px; }
    @media (max-width: 480px) { .mp-acc { grid-template-columns: minmax(0, 1fr); } app-mando-pc { padding: 18px; } }
    .mp-aviso { margin: 0; min-height: 1.2em; font-size: 12.5px; color: var(--soft); }
    .mp-mas { font-size: 13px; color: var(--acento); text-decoration: none; } .mp-mas:hover { text-decoration: underline; }
  `,
})
export class MandoPc {
  /** lo que cuenta el agente: volumen y brillo reales (0–100) y si está en línea */
  readonly volumen = input<number | null>(null);
  readonly brillo = input<number | null>(null);
  readonly enLinea = input(false);

  private readonly http = inject(HttpClient);
  private readonly confirmador = inject(ConfirmarService);
  protected readonly planes = PLANES;
  protected readonly vol = signal(50);
  protected readonly bri = signal(70);
  protected readonly mudo = signal(false);
  protected readonly plan = signal<string | null>((() => { try { return localStorage.getItem('mda-energia'); } catch { return null; } })());
  protected readonly despierto = signal(false);
  protected readonly aviso = signal('');
  private ocupadoHasta = 0;

  constructor() {
    // los deslizadores siguen al PC, salvo mientras acabas de moverlos tú
    effect(() => { const v = this.volumen(); if (v != null && Date.now() > this.ocupadoHasta) this.vol.set(Math.round(v)); });
    effect(() => { const b = this.brillo(); if (b != null && Date.now() > this.ocupadoHasta) this.bri.set(Math.round(b)); });
  }

  protected async enviar(tipo: 'volumen' | 'brillo', valor: number): Promise<void> {
    this.ocupadoHasta = Date.now() + 6000;
    const r = await this.orden(tipo, String(valor));
    this.aviso.set(r === 'ok' ? `${tipo === 'volumen' ? 'Volumen' : 'Brillo'} al ${valor} %` : this.texto(r));
  }

  protected async alternarMudo(): Promise<void> {
    const nuevo = !this.mudo(); this.mudo.set(nuevo); this.ocupadoHasta = Date.now() + 6000;
    const r = await this.orden('volumen', nuevo ? '0' : String(this.vol()));
    this.aviso.set(r === 'ok' ? (nuevo ? 'Silenciado' : `Volumen al ${this.vol()} %`) : this.texto(r)); if (r !== 'ok') this.mudo.set(!nuevo);
  }

  protected async elegirPlan(id: string): Promise<void> {
    const r = await this.orden('energia', id);
    if (r === 'ok') { this.plan.set(id); try { localStorage.setItem('mda-energia', id); } catch { /* sin almacenamiento */ } this.aviso.set(`Plan «${PLANES.find((p) => p.id === id)?.nombre}» enviado al PC.`); } else this.aviso.set(this.texto(r));
  }

  protected async alternarDespierto(): Promise<void> {
    const nuevo = !this.despierto();
    const r = await this.orden('despierto', nuevo ? 'on' : 'off', 'Confirma que eres tú para cambiar si el PC se suspende.');
    if (r === 'ok') { this.despierto.set(nuevo); this.aviso.set(nuevo ? 'El PC se mantendrá despierto.' : 'El PC podrá suspenderse.'); } else this.aviso.set(this.texto(r));
  }

  protected async bloquear(): Promise<void> {
    const r = await this.orden('bloquear', null, 'Confirma que eres tú para bloquear la pantalla del PC.');
    this.aviso.set(r === 'ok' ? 'Pantalla bloqueada.' : this.texto(r));
  }

  private texto(r: Resultado): string { return r === 'sin-agente' ? 'El agente de Windows no está en marcha.' : r === 'cancelado' ? '' : 'No se pudo enviar.'; }

  // si el servidor pide confirmar que eres tú, se pregunta y se reintenta una vez
  private async orden(tipo: string, parametro: string | null, motivo?: string): Promise<Resultado> {
    for (let intento = 0; intento < 2; intento++) {
      try { await firstValueFrom(this.http.post('/api/pc/ordenes', { tipo, parametro })); return 'ok'; }
      catch (err) {
        const x = err as HttpErrorResponse;
        if (x.status === 403 && x.error?.reautenticar && intento === 0) { if (!(await this.confirmador.pedir(motivo ?? 'Confirma que eres tú para seguir.'))) return 'cancelado'; continue; }
        return x.status === 409 ? 'sin-agente' : 'error';
      }
    }
    return 'error';
  }
}
