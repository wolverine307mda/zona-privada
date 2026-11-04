import { DecimalPipe } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

interface Medida { id: number; hora: string; bajadaMbps: number; subidaMbps: number; pingMs: number | null; }
interface Datos { actual: Medida | null; historial: Medida[]; resumen: { medidas: number; bajadaMedia: number | null; subidaMedia: number | null; bajadaMinima: number | null }; }

// La velocidad de tu conexión a Internet: la última medida y la curva del último mes. Se mide sola cada noche (entre las 3 y las 5)
// y a mano con «Medir ahora» (tarda unos 30 segundos y gasta unos 35 MB).
@Component({
  selector: 'app-velocidad',
  styleUrls: ['../gastos/comun.css', './modulos.css'],
  template: `
    <section class="caja">
      <header class="caja__cab"><h3>Velocidad de Internet</h3><span class="sub">la mide el PC contra Cloudflare, cada noche y cuando quieras</span></header>
      @if (datos()?.actual; as m) {
        <div class="veloz">
          <div class="veloz__cifra"><b>{{ m.bajadaMbps | number: '1.0-0' }}</b><small>Mbps de bajada</small></div>
          <div class="veloz__cifra veloz__cifra--sube"><b>{{ m.subidaMbps | number: '1.0-0' }}</b><small>Mbps de subida</small></div>
          <div class="veloz__cifra veloz__cifra--ping"><b>{{ m.pingMs ?? '–' }}</b><small>ms de latencia</small></div>
        </div>
        <p class="nada">Medido {{ cuando(m.hora) }}.{{ datos()!.resumen.bajadaMedia ? ' Media del mes: ↓ ' + datos()!.resumen.bajadaMedia + ' · ↑ ' + datos()!.resumen.subidaMedia + ' Mbps.' : '' }}</p>
        @if (lineas(); as l) {
          <svg class="curva" viewBox="0 0 300 80" preserveAspectRatio="none" role="img" aria-label="Velocidad de bajada y de subida en las últimas medidas">
            <polyline class="curva__baja" [attr.points]="l.baja" fill="none" vector-effect="non-scaling-stroke"/>
            <polyline class="curva__sube" [attr.points]="l.sube" fill="none" vector-effect="non-scaling-stroke"/>
          </svg>
          <div class="leyenda"><span class="baja">↓ bajada</span><span class="sube">↑ subida</span></div>
        }
      } @else { <p class="nada">{{ cargado() ? 'Aún no hay ninguna medida: pulsa «Medir ahora» (hace falta el agente en marcha).' : 'Cargando…' }}</p> }
      <div class="acciones"><button class="button" type="button" (click)="medir()" [disabled]="midiendo()">{{ midiendo() ? 'Midiendo… (≈30 s)' : 'Medir ahora' }}</button></div>
      @if (aviso()) { <p class="msg" role="status">{{ aviso() }}</p> }
    </section>
  `,
  styles: `
    .veloz { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 10px; margin-bottom: 8px; }
    .veloz__cifra { display: grid; gap: 2px; padding: 10px 12px; border-radius: 14px; background: color-mix(in srgb, var(--texto) 4%, transparent); }
    .veloz__cifra b { font: 700 1.8rem/1 var(--font-display); font-variant-numeric: tabular-nums; color: var(--acento); }
    .veloz__cifra--sube b { color: #3fb96a; } .veloz__cifra--ping b { color: var(--texto); font-size: 1.4rem; }
    .veloz__cifra small { color: var(--soft); font-size: .72rem; }
    .curva { width: 100%; height: 84px; margin-top: 6px; border-radius: 12px; background: color-mix(in srgb, var(--texto) 4%, transparent); }
    .curva polyline { stroke-width: 2; stroke-linejoin: round; stroke-linecap: round; }
    .curva__baja { stroke: var(--acento); } .curva__sube { stroke: #3fb96a; }
    .leyenda { display: flex; gap: 14px; font-size: .74rem; color: var(--soft); }
    .leyenda .baja { color: var(--acento); } .leyenda .sube { color: #3fb96a; }
    .acciones { margin-top: 10px; display: flex; justify-content: flex-end; }
    @media (max-width: 420px) { .veloz__cifra b { font-size: 1.4rem; } }
  `,
  imports: [DecimalPipe],
})
export class Velocidad implements OnInit {
  private readonly http = inject(HttpClient);
  protected readonly datos = signal<Datos | null>(null);
  protected readonly cargado = signal(false);
  protected readonly midiendo = signal(false);
  protected readonly aviso = signal('');
  // dos líneas con las medidas del mes, a la misma escala para poder compararlas
  protected readonly lineas = computed(() => {
    const h = this.datos()?.historial ?? [];
    if (h.length < 2) return null;
    const techo = Math.max(10, ...h.map((m) => Math.max(m.bajadaMbps, m.subidaMbps)));
    const y = (v: number) => 76 - (v / techo) * 72;
    const x = (i: number) => (i / (h.length - 1)) * 300;
    return { baja: h.map((m, i) => `${x(i)},${y(m.bajadaMbps)}`).join(' '), sube: h.map((m, i) => `${x(i)},${y(m.subidaMbps)}`).join(' ') };
  });

  ngOnInit(): void { void this.leer(); }

  protected cuando(iso: string): string {
    const m = Math.round((Date.now() - new Date(iso).getTime()) / 60000);
    return m < 1 ? 'ahora mismo' : m < 60 ? `hace ${m} min` : m < 1440 ? `hace ${Math.round(m / 60)} h` : `hace ${Math.round(m / 1440)} d`;
  }

  private async leer(): Promise<void> {
    try { this.datos.set(await firstValueFrom(this.http.get<Datos>('/api/red/velocidad?dias=30'))); } catch { /* sin datos */ }
    this.cargado.set(true);
  }

  protected async medir(): Promise<void> {
    this.aviso.set(''); this.midiendo.set(true);
    const antes = this.datos()?.resumen.medidas ?? 0;
    try {
      await firstValueFrom(this.http.post('/api/pc/ordenes', { tipo: 'velocidad' }));
      // se mira cada 8 s, hasta 2 minutos, a ver si ya ha llegado la medida nueva
      for (let i = 0; i < 15; i++) {
        await new Promise((r) => setTimeout(r, 8000));
        await this.leer();
        if ((this.datos()?.resumen.medidas ?? 0) > antes) { this.aviso.set('Medida nueva guardada.'); return; }
      }
      this.aviso.set('No ha llegado la medida: mira el estado del agente.');
    } catch (e) {
      this.aviso.set((e as HttpErrorResponse).status === 409 ? 'El agente de Windows no está en marcha.' : 'No se pudo pedir el test.');
    } finally { this.midiendo.set(false); }
  }
}
