import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, OnDestroy, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

interface EstadoRopa { tendida: boolean; desde: string | null; lluvia: { minutos: number; mm: number } | null; aviso: string | null; }

// Ropa tendida: un botón para marcarla y, mientras esté tendida, la hora desde la que está y si viene lluvia.
// La marca caduca sola a las 10 horas; el aviso de lluvia llega por la app y por Telegram unos 30 minutos antes.
@Component({
  selector: 'app-ropa-chip',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (e(); as r) {
      @if (r.tendida) {
        <div class="ropa" [class.ropa--lluvia]="!!r.lluvia" role="status">
          <span class="ropa__texto">
            <strong>🧺 Ropa tendida desde las {{ hora(r.desde!) }}</strong>
            <small>{{ r.aviso ?? 'De momento no se espera lluvia.' }}</small>
          </span>
          <button type="button" class="ropa__boton" (click)="recoger()" [disabled]="ocupado()">Recogida</button>
        </div>
      } @else {
        <button type="button" class="ropa__tender" (click)="tender()" [disabled]="ocupado()">🧺 Tender la ropa</button>
      }
    }
  `,
  styles: `
    :host { display: block; }
    .ropa { display: flex; align-items: center; justify-content: space-between; gap: 12px; padding: 10px 14px; border-radius: var(--radio-l, 16px); border: 1px solid var(--vidrio-borde); background: var(--vidrio); }
    .ropa--lluvia { border-color: color-mix(in srgb, #4aa3ff 60%, transparent); background: color-mix(in srgb, #4aa3ff 12%, var(--vidrio)); }
    .ropa__texto { display: flex; flex-direction: column; gap: 2px; min-width: 0; }
    .ropa__texto small { color: var(--soft); font-size: .8rem; }
    .ropa__boton, .ropa__tender { min-height: 44px; padding: 0 16px; border-radius: 999px; border: 1px solid var(--vidrio-borde); background: none; color: var(--texto); font: inherit; cursor: pointer; }
    .ropa__boton { background: var(--brand); border-color: var(--brand); color: var(--on-brand); }
    .ropa__tender { color: var(--soft); font-size: .85rem; min-height: 40px; }
  `,
})
export class RopaChip implements OnDestroy {
  private readonly http = inject(HttpClient);
  protected readonly e = signal<EstadoRopa | null>(null);
  protected readonly ocupado = signal(false);
  // mientras esté tendida se mira cada 5 minutos (el servidor guarda la previsión de lluvia ese tiempo)
  private readonly temporizador = setInterval(() => void this.cargar(), 5 * 60_000);

  constructor() { void this.cargar(); }

  private async cargar(): Promise<void> {
    try { this.e.set(await firstValueFrom(this.http.get<EstadoRopa>('/api/tiempo/ropa'))); } catch { this.e.set(null); }
  }

  protected hora(iso: string): string { return new Date(iso).toLocaleTimeString('es-ES', { hour: '2-digit', minute: '2-digit' }); }

  protected async tender(): Promise<void> { await this.cambiar('tender'); }
  protected async recoger(): Promise<void> { await this.cambiar('recoger'); }

  private async cambiar(accion: 'tender' | 'recoger'): Promise<void> {
    this.ocupado.set(true);
    try { await firstValueFrom(this.http.post(`/api/tiempo/ropa/${accion}`, null)); await this.cargar(); } finally { this.ocupado.set(false); }
  }

  ngOnDestroy(): void { clearInterval(this.temporizador); }
}
