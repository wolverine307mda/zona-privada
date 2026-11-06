import { HttpClient } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { EstadoError, Esqueleto } from '../ui';

interface Paso { nombre: string; ultimaVez: string; ultimoOk: boolean; ultimoError: string | null; ultimoFallo: string | null; fallos: number; }
interface Vigilancia { pasos: Paso[]; errores10min: number; umbral: number; }

// Cada comprobación de fondo (copias, presupuestos, tareas…) con la hora de la última vez y si falló. Solo vive en memoria: se vacía al reiniciar la API.
// Si la API da muchos errores 5xx seguidos, te llega un aviso por Telegram.
@Component({
  selector: 'app-vigilancia',
  imports: [Esqueleto, EstadoError],
  styleUrls: ['../gastos/comun.css'],
  template: `
    <section class="caja">
      <header class="caja__cab"><h3>Vigilancia en segundo plano</h3><span class="sub">{{ cabecera() }}</span></header>
      @if (fallo()) { <app-estado-error titulo="No pude leer la vigilancia" (reintentar)="cargar()" /> }
      @else if (datos() === null) { <app-esqueleto forma="linea" /><app-esqueleto forma="linea" /> }
      @else {
        <ul class="lista">
          @for (p of datos()!.pasos; track p.nombre) {
            <li>
              <span class="punto" [class.punto--mal]="!p.ultimoOk" aria-hidden="true"></span>
              <span class="lista__texto">
                <strong>{{ p.nombre }}</strong>
                <span>{{ p.ultimoOk ? 'bien' : 'falló: ' + p.ultimoError }} · {{ hora(p.ultimaVez) }}@if (p.fallos) { · {{ p.fallos }} {{ p.fallos === 1 ? 'fallo' : 'fallos' }} desde el arranque }</span>
              </span>
            </li>
          } @empty { <p class="nada">Aún no ha corrido ninguna (empiezan un minuto después de arrancar y solo de 9 a 23 h).</p> }
        </ul>
      }
    </section>
  `,
  styles: `
    .lista { display: flex; flex-direction: column; gap: 4px; list-style: none; margin: 0; padding: 0; }
    .lista li { display: grid; grid-template-columns: auto minmax(0, 1fr); align-items: center; gap: 12px; padding: 8px 4px; border-bottom: 1px solid var(--linea); }
    .lista li:last-child { border-bottom: 0; }
    .lista__texto { display: grid; min-width: 0; }
    .lista__texto span { color: var(--soft); font-size: .8rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .punto { width: 10px; height: 10px; border-radius: 50%; background: var(--ok); }
    .punto--mal { background: var(--bad); }
  `,
})
export class VigilanciaFondo implements OnInit {
  private readonly http = inject(HttpClient);
  protected readonly datos = signal<Vigilancia | null>(null);
  protected readonly fallo = signal(false);

  ngOnInit(): void { void this.cargar(); }

  protected async cargar(): Promise<void> {
    this.fallo.set(false);
    try { this.datos.set(await firstValueFrom(this.http.get<Vigilancia>('/api/vigilancia'))); } catch { this.fallo.set(true); }
  }

  protected cabecera(): string {
    const d = this.datos();
    return d ? `${d.errores10min} errores 5xx en 10 min (avisa a partir de ${d.umbral})` : 'Qué hizo cada comprobación por última vez';
  }

  protected hora(iso: string): string { return new Date(iso).toLocaleString('es-ES', { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' }); }
}
