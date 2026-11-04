import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

interface Area { id: string; nombre: string; estado: 'verde' | 'ambar' | 'rojo'; motivo: string; }
interface Semaforo { estado: 'verde' | 'ambar' | 'rojo'; areas: Area[]; }

const TITULAR = { verde: 'Todo en orden', ambar: 'Hay algo que mirar', rojo: 'Hay algo que arreglar' };

// El semáforo de tu vida digital: copias, seguridad, dinero, PC y red, cada una en verde, ámbar o rojo con su motivo.
@Component({
  selector: 'app-semaforo',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (datos(); as s) {
      <section class="semaforo tarjeta" [attr.data-estado]="s.estado" aria-label="Estado general">
        <header>
          <span class="luz" aria-hidden="true"></span>
          <h3>{{ titular(s.estado) }}</h3>
        </header>
        <ul>
          @for (a of s.areas; track a.id) {
            <li [attr.data-estado]="a.estado">
              <span class="punto" aria-hidden="true"></span>
              <span class="texto"><strong>{{ a.nombre }}</strong><small>{{ a.motivo }}</small></span>
              <span class="sr">{{ a.estado === 'verde' ? 'bien' : a.estado === 'ambar' ? 'atención' : 'problema' }}</span>
            </li>
          }
        </ul>
      </section>
    }
  `,
  styles: `
    :host { display: block; height: 100%; }
    .semaforo { --c: #3fb96a; height: 100%; padding: 18px 20px; border-left: 4px solid var(--c); }
    .semaforo[data-estado='ambar'] { --c: #f0b53a; }
    .semaforo[data-estado='rojo'] { --c: #e5484d; }
    header { display: flex; align-items: center; gap: 10px; margin-bottom: 10px; }
    header h3 { margin: 0; font: 500 var(--step-lead)/1.2 var(--font-text); }
    .luz { width: 14px; height: 14px; border-radius: 50%; background: var(--c); box-shadow: 0 0 0 5px color-mix(in srgb, var(--c) 25%, transparent); }
    ul { list-style: none; margin: 0; padding: 0; display: grid; gap: 8px; grid-template-columns: repeat(auto-fit, minmax(210px, 1fr)); }
    li { --p: #3fb96a; display: grid; grid-template-columns: 12px minmax(0, 1fr); gap: 10px; align-items: start; }
    li[data-estado='ambar'] { --p: #f0b53a; }
    li[data-estado='rojo'] { --p: #e5484d; }
    .punto { width: 10px; height: 10px; margin-top: 5px; border-radius: 50%; background: var(--p); }
    .texto { display: grid; gap: 2px; min-width: 0; }
    .texto small { color: var(--soft); font-size: .78rem; }
    .sr { position: absolute; width: 1px; height: 1px; overflow: hidden; clip-path: inset(50%); }
    @media (prefers-reduced-motion: no-preference) { .semaforo[data-estado='rojo'] .luz { animation: pulso 1.8s ease-in-out infinite; } }
    @keyframes pulso { 50% { box-shadow: 0 0 0 9px color-mix(in srgb, var(--c) 8%, transparent); } }
  `,
})
export class SemaforoVida implements OnInit {
  private readonly http = inject(HttpClient);
  protected readonly datos = signal<Semaforo | null>(null);

  constructor() {
    const t = setInterval(() => { if (!document.hidden) void this.leer(); }, 120_000);
    inject(DestroyRef).onDestroy(() => clearInterval(t));
  }

  ngOnInit(): void { void this.leer(); }
  protected titular(e: Semaforo['estado']): string { return TITULAR[e]; }

  private async leer(): Promise<void> {
    try { this.datos.set(await firstValueFrom(this.http.get<Semaforo>('/api/inicio/resumen'))); } catch { /* se queda como estaba */ }
  }
}
