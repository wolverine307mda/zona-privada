import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

interface Candidato { id: number; nombre: string; tipo: string; }
interface Salida { fecha: string; abiertos: string[]; encendidos: string[]; apagados: string[]; }
interface Respuesta {
  ajustes: { apagar: boolean; vigilar: number[] }; candidatos: Candidato[]; puertas: { id: number; nombre: string }[]; historial: Salida[];
}

// Al salir de casa (el iPhone avisa con «Me voy»): se comprueba qué puertas o ventanas se quedan abiertas y qué aparatos de tu lista siguen encendidos.
// El atajo contesta «Hasta luego. Ojo: ventana del salón abierta; plancha encendida» y Telegram te deja un botón «Apagar» por cada aparato encendido.
// Solo se apaga solo lo que marques aquí, y solo si activas «Apagarlos yo».
@Component({
  selector: 'app-salida-casa',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrls: ['../gastos/comun.css'],
  template: `
    <section class="caja salida" aria-labelledby="salida-t">
      <header class="caja__cab"><h3 id="salida-t">Al salir de casa</h3>
        <button class="enlace" type="button" (click)="probar()">Probar ahora</button></header>
      @if (fallo()) { <p class="sub">No se pudo leer. <button class="enlace" type="button" (click)="cargar()">Reintentar</button></p> }
      @else if (d(); as d) {
        <p class="sub">Las puertas y ventanas abiertas se avisan siempre. Marca lo que no debería quedarse encendido al salir:</p>
        @if (d.candidatos.length === 0) { <p class="sub">Aún no hay aparatos que se enciendan y apaguen (enchufes, luces…). Añádelos en Casa.</p> }
        @else {
          <ul class="lista">
            @for (c of d.candidatos; track c.id) {
              <li><label><input type="checkbox" [checked]="vigilado(c.id)" (change)="alternar(c.id, $any($event.target).checked)"> <span>{{ c.nombre }}</span> <small>{{ c.tipo }}</small></label></li>
            }
          </ul>
          <label class="apagar"><input type="checkbox" [checked]="d.ajustes.apagar" (change)="apagar($any($event.target).checked)"> <span>Apagarlos yo al salir <small>(solo los marcados)</small></span></label>
        }
        @if (prueba()) { <p class="prueba" role="status">{{ prueba() }}</p> }
        @if (d.historial.length > 0) {
          <h4>Últimas salidas</h4>
          <ul class="historial">
            @for (h of d.historial; track h.fecha) {
              <li><time>{{ fecha(h.fecha) }}</time>
                <span>{{ resumen(h) }}</span></li>
            }
          </ul>
        }
      } @else { <p class="sub" aria-busy="true">Cargando…</p> }
    </section>
  `,
  styles: `
    :host { display: block; margin-top: var(--esp-6, 32px); }
    .salida { display: flex; flex-direction: column; gap: 10px; }
    .sub { margin: 0; color: var(--soft); font-size: .85rem; line-height: 1.5; }
    .sub small, .lista small, .apagar small { color: var(--soft); }
    .enlace { background: none; border: 0; color: var(--brand); font: inherit; cursor: pointer; }
    .lista, .historial { list-style: none; margin: 0; padding: 0; display: grid; gap: 4px; }
    .lista label, .apagar { display: flex; align-items: center; gap: 10px; min-height: 40px; }
    .apagar { margin-top: 4px; }
    .prueba { margin: 0; padding: 10px 12px; border-radius: 12px; background: var(--tarjeta-2); font-size: .9rem; }
    h4 { margin: 8px 0 0; font: 500 .9rem/1.2 var(--font-text); }
    .historial li { display: grid; grid-template-columns: 88px minmax(0, 1fr); gap: 10px; font-size: .82rem; }
    .historial time { color: var(--soft); }
  `,
})
export class SalidaCasa {
  private readonly http = inject(HttpClient);
  protected readonly d = signal<Respuesta | null>(null);
  protected readonly fallo = signal(false);
  protected readonly prueba = signal('');

  constructor() { void this.cargar(); }

  protected async cargar(): Promise<void> {
    this.fallo.set(false);
    try { this.d.set(await firstValueFrom(this.http.get<Respuesta>('/api/domotica/salida'))); } catch { this.fallo.set(true); }
  }

  protected vigilado(id: number): boolean { return this.d()?.ajustes.vigilar.includes(id) ?? false; }

  protected async alternar(id: number, marcado: boolean): Promise<void> {
    const a = this.d()!.ajustes;
    await this.guardar({ apagar: a.apagar, vigilar: marcado ? [...new Set([...a.vigilar, id])] : a.vigilar.filter((x) => x !== id) });
  }

  protected async apagar(v: boolean): Promise<void> { await this.guardar({ apagar: v, vigilar: this.d()!.ajustes.vigilar }); }

  private async guardar(a: { apagar: boolean; vigilar: number[] }): Promise<void> {
    try {
      const nuevo = await firstValueFrom(this.http.put<{ apagar: boolean; vigilar: number[] }>('/api/domotica/salida', a));
      this.d.update((x) => x && { ...x, ajustes: nuevo });
    } catch { this.fallo.set(true); }
  }

  protected async probar(): Promise<void> {
    try { this.prueba.set((await firstValueFrom(this.http.get<{ frase: string }>('/api/domotica/salida/probar'))).frase); } catch { this.prueba.set('No se pudo comprobar.'); }
  }

  protected fecha(iso: string): string { return new Date(iso).toLocaleString('es-ES', { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' }); }

  protected resumen(h: Salida): string {
    const p: string[] = [];
    if (h.abiertos.length) p.push(`abierto: ${h.abiertos.join(', ')}`);
    if (h.encendidos.length) p.push(`encendido: ${h.encendidos.join(', ')}`);
    if (h.apagados.length) p.push(`apagado: ${h.apagados.join(', ')}`);
    return p.length ? p.join(' · ') : 'todo cerrado';
  }
}
