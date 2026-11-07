import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { componer } from './bienvenida';

const CLAVE = 'mda-bienvenida-sencilla';
const leer = (): boolean => { try { return localStorage.getItem(CLAVE) === '1'; } catch { return false; } };

// La frase de debajo del saludo: la compone `componer` con lo de hoy (cumples, festivo, estación, luna…). Con «sencilla» vuelve al texto de siempre.
@Component({
  selector: 'app-bienvenida-frase',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p class="hola__lead">
      @if (sencilla()) { Tu día de un vistazo y <strong>{{ equipo() }}</strong> en directo. } @else { {{ frase().texto }} }
      <button type="button" class="bienvenida__ajuste" (click)="alternar()" [attr.aria-label]="sencilla() ? 'Frase personal' : 'Bienvenida sencilla'" [title]="sencilla() ? 'Frase personal' : 'Bienvenida sencilla'">{{ sencilla() ? '✎' : '✕' }}</button>
    </p>
  `,
  styles: `
    :host { display: contents; }
    .bienvenida__ajuste { margin-left: 6px; border: 0; background: none; color: inherit; opacity: .35; cursor: pointer; font-size: .8em; }
    .bienvenida__ajuste:hover, .bienvenida__ajuste:focus-visible { opacity: .8; }
  `,
})
export class BienvenidaFrase {
  readonly nombre = input.required<string>();
  readonly equipo = input<string>('tu equipo');
  private readonly http = inject(HttpClient);
  protected readonly sencilla = signal(leer());
  private readonly cumples = signal<string[]>([]);
  protected readonly frase = computed(() => componer({ fecha: new Date(), nombre: this.nombre(), cumples: this.cumples() }));

  constructor() { void this.cargar(); }

  private async cargar(): Promise<void> {
    try {
      const l = await firstValueFrom(this.http.get<{ nombre: string; apodo: string; dias: number }[]>('/api/personas/proximos?dias=1'));
      this.cumples.set(l.filter((p) => p.dias === 0).map((p) => p.apodo || p.nombre));
    } catch { this.cumples.set([]); }
  }

  protected alternar(): void {
    const v = !this.sencilla(); this.sencilla.set(v);
    try { localStorage.setItem(CLAVE, v ? '1' : '0'); } catch { /* sin almacenamiento */ }
  }
}
