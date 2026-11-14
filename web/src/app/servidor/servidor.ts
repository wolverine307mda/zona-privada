import { Component, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { inject } from '@angular/core';
import { Guardia } from './guardia';
import { LineaServidor } from './linea-servidor';
import { Runbooks } from './runbooks';

type Pestana = 'runbooks' | 'guardia' | 'linea';

// «Servidor»: lo que hace la zona privada cuando tú no miras. Runbooks (secuencias con pasos que piden tu permiso), la guardia (alarmas con botones)
// con el despertar del PC principal, y la línea de tiempo (accesos, alertas, órdenes y runbooks en una sola vista).
@Component({
  selector: 'app-servidor',
  imports: [Runbooks, Guardia, LineaServidor],
  styleUrls: ['../gastos/comun.css'],
  template: `
    <nav class="pestanas" role="tablist" aria-label="Servidor">
      @for (p of pestanas; track p.id) {
        <button type="button" role="tab" [attr.aria-selected]="pestana() === p.id" [class.on]="pestana() === p.id" (click)="pestana.set(p.id)">{{ p.texto }}</button>
      }
    </nav>
    @switch (pestana()) {
      @case ('runbooks') { <app-runbooks /> }
      @case ('guardia') { <app-guardia /> }
      @case ('linea') { <app-linea-servidor /> }
    }
  `,
  styles: `
    :host { display: grid; gap: 14px; }
    .pestanas { display: flex; gap: 6px; flex-wrap: wrap; }
    .pestanas button { min-height: 40px; padding: 0 16px; border-radius: 999px; border: 1px solid var(--vidrio-borde); background: none; color: var(--texto); font: 600 .9rem/1 var(--font-text); cursor: pointer; }
    .pestanas button.on { background: var(--brand); border-color: var(--brand); color: var(--on-brand); }
  `,
})
export class Servidor {
  protected readonly pestanas: { id: Pestana; texto: string }[] = [{ id: 'runbooks', texto: 'Runbooks' }, { id: 'guardia', texto: 'Guardia y PC principal' }, { id: 'linea', texto: 'Línea de tiempo' }];
  protected readonly pestana = signal<Pestana>('runbooks');

  constructor() {
    // /servidor?ver=linea (los avisos y botones de Telegram abren la pestaña que toca)
    const ver = inject(ActivatedRoute).snapshot.queryParamMap.get('ver');
    if (ver === 'guardia' || ver === 'linea' || ver === 'runbooks') this.pestana.set(ver);
  }
}
