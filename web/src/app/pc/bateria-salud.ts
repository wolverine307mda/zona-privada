import { DecimalPipe } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

interface Medida { fecha: string; diseno: number; completa: number; ciclos: number | null; desgastePct: number; }

// El desgaste de la batería del portátil: lo que carga ahora frente a lo que cargaba nueva, y cómo va con los días.
// «Medir ahora» le pide al agente el informe de Windows (tarda unos segundos).
@Component({
  selector: 'app-bateria-salud',
  styleUrls: ['../gastos/comun.css', './modulos.css'],
  template: `
    <section class="caja">
      <header class="caja__cab"><h3>Salud de la batería</h3><span class="sub">cuánto aguanta frente a cuando era nueva</span></header>
      @if (actual(); as m) {
        <div class="salud">
          <div class="salud__numero" [class.salud__numero--mal]="m.desgastePct >= 40" [class.salud__numero--regular]="m.desgastePct >= 20 && m.desgastePct < 40">
            <b>{{ 100 - m.desgastePct | number: '1.0-0' }}%</b><small>de su capacidad original</small>
          </div>
          <dl>
            <div><dt>Carga ahora</dt><dd>{{ wh(m.completa) }} Wh</dd></div>
            <div><dt>De nueva</dt><dd>{{ wh(m.diseno) }} Wh</dd></div>
            <div><dt>Desgaste</dt><dd>{{ m.desgastePct }} %</dd></div>
            @if (m.ciclos) { <div><dt>Ciclos de carga</dt><dd>{{ m.ciclos }}</dd></div> }
            <div><dt>Medida</dt><dd>{{ m.fecha }}</dd></div>
          </dl>
        </div>
        @if (linea(); as p) {
          <svg class="tendencia" viewBox="0 0 200 48" preserveAspectRatio="none" role="img" [attr.aria-label]="'Desgaste de la batería en los últimos ' + historial().length + ' días medidos'">
            <polyline [attr.points]="p" fill="none" stroke="currentColor" stroke-width="2" vector-effect="non-scaling-stroke" stroke-linejoin="round" stroke-linecap="round"/>
          </svg>
        }
      } @else {
        <p class="nada">{{ cargado() ? 'Aún no hay ninguna medida: pulsa «Medir ahora» (hay que tener el agente en marcha).' : 'Cargando…' }}</p>
      }
      <div class="acciones"><button class="button" type="button" (click)="medir()" [disabled]="midiendo()">{{ midiendo() ? 'Midiendo…' : 'Medir ahora' }}</button></div>
      @if (aviso()) { <p class="msg" role="status">{{ aviso() }}</p> }
    </section>
  `,
  styles: `
    .salud { display: grid; grid-template-columns: auto minmax(0, 1fr); gap: 18px 26px; align-items: center; }
    .salud__numero { display: grid; justify-items: center; padding: 6px 14px; color: #3fb96a; }
    .salud__numero--regular { color: #f0b53a; } .salud__numero--mal { color: var(--bad); }
    .salud__numero b { font: 700 2.4rem/1 var(--font-display); font-variant-numeric: tabular-nums; }
    .salud__numero small { color: var(--soft); font-size: .72rem; max-width: 12ch; text-align: center; }
    dl { display: grid; gap: 8px; margin: 0; }
    dl div { display: flex; justify-content: space-between; gap: 12px; font-size: .88rem; }
    dt { color: var(--soft); } dd { margin: 0; font: 600 .86rem/1.2 var(--font-code); }
    .tendencia { width: 100%; height: 56px; margin-top: 12px; color: var(--acento); }
    .acciones { margin-top: 12px; display: flex; justify-content: flex-end; }
    @media (max-width: 480px) { .salud { grid-template-columns: minmax(0, 1fr); } }
  `,
  imports: [DecimalPipe],
})
export class BateriaSalud implements OnInit {
  private readonly http = inject(HttpClient);
  protected readonly historial = signal<Medida[]>([]);
  protected readonly cargado = signal(false);
  protected readonly midiendo = signal(false);
  protected readonly aviso = signal('');
  protected readonly actual = computed(() => this.historial().at(-1) ?? null);
  // la línea del desgaste con los días medidos (dos puntos como mínimo para que sea una línea)
  protected readonly linea = computed(() => {
    const h = this.historial();
    if (h.length < 2) return '';
    const max = Math.max(5, ...h.map((m) => m.desgastePct));
    return h.map((m, i) => `${(i / (h.length - 1)) * 200},${46 - (m.desgastePct / max) * 42}`).join(' ');
  });

  ngOnInit(): void { void this.leer(); }

  protected wh(mwh: number): string { return (mwh / 1000).toFixed(1); }

  private async leer(): Promise<void> {
    try { this.historial.set((await firstValueFrom(this.http.get<{ historial: Medida[] }>('/api/pc/bateria'))).historial); } catch { /* sin datos */ }
    this.cargado.set(true);
  }

  protected async medir(): Promise<void> {
    this.aviso.set(''); this.midiendo.set(true);
    try {
      await firstValueFrom(this.http.post('/api/pc/ordenes', { tipo: 'bateria-informe' }));
      // el informe de Windows tarda unos segundos: se mira un par de veces
      for (const espera of [6000, 6000, 8000]) { await new Promise((r) => setTimeout(r, espera)); await this.leer(); }
      this.aviso.set('Medida actualizada.');
    } catch (e) {
      this.aviso.set((e as HttpErrorResponse).status === 409 ? 'El agente de Windows no está en marcha.' : 'No se pudo pedir el informe.');
    } finally { this.midiendo.set(false); }
  }
}
