import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, OnDestroy, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ConfirmarService } from '../core/confirmar.service';

interface Transferencia {
  id: number; direccion: 'pc' | 'movil'; nombre: string; tamano: number; estado: string; creada: string; caduca: string; mensaje: string; descargable: boolean; pideContrasena: boolean;
}

const ESTADOS: Record<string, string> = { subido: 'esperando al PC', 'en-el-pc': 'ya está en el PC', 'para-descargar': 'listo para bajar', descargado: 'descargado', caducado: 'caducó', error: 'falló' };

// La bandeja en los dos sentidos. «Para el PC»: eliges un archivo (hasta 100 MB, sin programas) y el PC lo guarda en «Zona privada\Bandeja\Recibidos».
// «Para el móvil»: lo que dejes en «Zona privada\Bandeja\Para el móvil» aparece aquí 24 horas para bajarlo (los grandes piden la contraseña otra vez).
@Component({
  selector: 'app-transferencias-pc',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrls: ['../gastos/comun.css'],
  template: `
    <section class="caja trans">
      <header class="caja__cab"><h3>Archivos entre la app y el PC</h3>
        <div class="selector" role="group" aria-label="Sentido">
          <button type="button" [attr.aria-pressed]="pestana() === 'pc'" (click)="pestana.set('pc')">Para el PC</button>
          <button type="button" [attr.aria-pressed]="pestana() === 'movil'" (click)="pestana.set('movil')">Para el móvil</button>
        </div>
      </header>
      @if (pestana() === 'pc') {
        <label class="subir"><input type="file" (change)="elegido($event)" [disabled]="subiendo()">
          <span class="button button--ghost">{{ subiendo() ? 'Subiendo…' : 'Elegir un archivo' }}</span></label>
        <p class="sub">Hasta 100 MB. No se envían programas ni scripts (.exe, .bat, .ps1…). El PC lo deja en «Zona privada\\Bandeja\\Recibidos».</p>
      } @else {
        <p class="sub">Deja un archivo en «Zona privada\\Bandeja\\Para el móvil» del PC y saldrá aquí 24 horas (hasta 100 MB).</p>
      }
      @if (aviso()) { <p class="aviso" role="status">{{ aviso() }}</p> }
      <ul class="lista">
        @for (t of visibles(); track t.id) {
          <li>
            <span class="lista__texto"><strong>{{ t.nombre }}</strong><span>{{ tamano(t.tamano) }} · {{ estado(t) }}@if (t.mensaje) { · {{ t.mensaje }} }</span></span>
            @if (t.descargable) { <button class="enlace" type="button" (click)="bajar(t)">Bajar{{ t.pideContrasena ? ' 🔒' : '' }}</button> }
            <button class="enlace enlace--mal" type="button" (click)="quitar(t)" [attr.aria-label]="'Quitar ' + t.nombre">Quitar</button>
          </li>
        } @empty { <li class="sub">Nada por aquí todavía.</li> }
      </ul>
    </section>
  `,
  styles: `
    .trans { display: flex; flex-direction: column; gap: 10px; }
    .selector { display: inline-flex; gap: 4px; }
    .selector button { border: 1px solid var(--vidrio-borde); background: none; color: var(--soft); border-radius: 999px; padding: 6px 14px; font: 500 .8rem/1 var(--font-text); min-height: 40px; cursor: pointer; }
    .selector button[aria-pressed='true'] { background: var(--brand); border-color: var(--brand); color: var(--on-brand); }
    .subir input { position: absolute; opacity: 0; width: 1px; height: 1px; }
    .sub { margin: 0; color: var(--soft); font-size: .85rem; }
    .aviso { margin: 0; padding: 8px 12px; border-radius: 10px; background: var(--tarjeta-2); font-size: .88rem; }
    .lista { list-style: none; margin: 0; padding: 0; display: grid; gap: 4px; }
    .lista li { display: grid; grid-template-columns: minmax(0, 1fr) auto auto; gap: 12px; align-items: center; padding: 8px 4px; border-bottom: 1px solid var(--linea); }
    .lista__texto { display: grid; min-width: 0; }
    .lista__texto span { color: var(--soft); font-size: .8rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .enlace { background: none; border: 0; color: var(--brand); font: inherit; cursor: pointer; min-height: 40px; }
    .enlace--mal { color: var(--bad, #ff5a5a); }
  `,
})
export class TransferenciasPc implements OnDestroy {
  private readonly http = inject(HttpClient);
  private readonly confirmador = inject(ConfirmarService);
  protected readonly pestana = signal<'pc' | 'movil'>('pc');
  protected readonly lista = signal<Transferencia[]>([]);
  protected readonly visibles = computed(() => this.lista().filter((t) => t.direccion === this.pestana()));
  protected readonly subiendo = signal(false);
  protected readonly aviso = signal('');
  // mientras haya algo esperando al PC se mira cada 5 s; si no, cada minuto (por si el PC deja algo «para el móvil»)
  private temporizador = setInterval(() => void this.cargar(), 15_000);

  constructor() { void this.cargar(); }

  private async cargar(): Promise<void> {
    try { this.lista.set(await firstValueFrom(this.http.get<Transferencia[]>('/api/pc/bandeja/transferencias'))); } catch { /* se queda como estaba */ }
  }

  protected estado(t: Transferencia): string { return ESTADOS[t.estado] ?? t.estado; }
  protected tamano(b: number): string { return b >= 1048576 ? `${(b / 1048576).toLocaleString('es-ES', { maximumFractionDigits: 1 })} MB` : `${Math.max(1, Math.round(b / 1024))} KB`; }

  protected async elegido(e: Event): Promise<void> {
    const input = e.target as HTMLInputElement, f = input.files?.[0];
    input.value = '';
    if (!f) return;
    if (f.size > 100 * 1024 * 1024) { this.aviso.set('Pasa de 100 MB.'); return; }
    this.subiendo.set(true); this.aviso.set('');
    try {
      for (let intento = 0; intento < 2; intento++) {
        try {
          const datos = new FormData(); datos.append('archivo', f, f.name);
          await firstValueFrom(this.http.post('/api/pc/bandeja/transferencias/enviar', datos));
          this.aviso.set(`«${f.name}» enviado: el PC lo recoge en unos segundos.`);
          break;
        } catch (err) {
          const x = err as HttpErrorResponse;
          if (x.status === 403 && x.error?.reautenticar && intento === 0) { if (!(await this.confirmador.pedir('Confirma que eres tú para mandar un archivo al PC.'))) break; continue; }
          this.aviso.set(x.error?.detalle ?? 'No se pudo enviar.');
          break;
        }
      }
    } finally { this.subiendo.set(false); await this.cargar(); }
  }

  protected async bajar(t: Transferencia): Promise<void> {
    this.aviso.set('');
    for (let intento = 0; intento < 2; intento++) {
      try {
        const blob = await firstValueFrom(this.http.get(`/api/pc/bandeja/transferencias/${t.id}/archivo`, { responseType: 'blob' }));
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a'); a.href = url; a.download = t.nombre; document.body.appendChild(a); a.click(); a.remove();
        setTimeout(() => URL.revokeObjectURL(url), 30_000);
        break;
      } catch (err) {
        const x = err as HttpErrorResponse;
        const reauth = x.status === 403 && (x.error instanceof Blob ? true : !!x.error?.reautenticar);
        if (reauth && intento === 0) { if (!(await this.confirmador.pedir('Confirma que eres tú para bajar este archivo.'))) break; continue; }
        this.aviso.set(x.status === 404 ? 'Ese archivo ya no está (se borra al bajarlo o a las 24 h).' : 'No se pudo bajar.');
        break;
      }
    }
    await this.cargar();
  }

  protected async quitar(t: Transferencia): Promise<void> {
    try { await firstValueFrom(this.http.delete(`/api/pc/bandeja/transferencias/${t.id}`)); } catch { /* ya no estaba */ }
    await this.cargar();
  }

  ngOnDestroy(): void { clearInterval(this.temporizador); }
}
