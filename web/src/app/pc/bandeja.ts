import { HttpClient } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

interface Elemento { id: number; texto: string; fecha: string; }

// La bandeja de entrada del PC: lo que cae en la carpeta «Zona privada\Bandeja» de tu usuario de Windows (o la que pongas en AGENTE_BANDEJA) sale aquí y te llega un aviso.
// Sirve para pasar cosas al PC desde otro sitio (la nube, otro equipo, el móvil con «Guardar en archivos»). Se ven los 50 últimos; los archivos están en el PC.
@Component({
  selector: 'app-bandeja-pc',
  styleUrls: ['../gastos/comun.css', './modulos.css'],
  template: `
    <section class="caja">
      <header class="caja__cab"><h3>Bandeja de entrada</h3><span class="sub">lo que cae en «Zona privada\\Bandeja» del PC</span></header>
      <ul class="clips">
        @for (e of elementos(); track e.id) {
          <li class="clip">
            <p class="clip__texto">{{ e.texto }}</p>
            <small>{{ hora(e.fecha) }}</small>
            <div class="clip__acciones"><button class="enlace enlace--peligro" type="button" (click)="quitar(e)" [attr.aria-label]="'Quitar de la lista ' + e.texto">Quitar de la lista</button></div>
          </li>
        } @empty { <li class="nada">Nada ha llegado desde que arrancó la app. Deja un archivo en la carpeta del PC y saldrá aquí.</li> }
      </ul>
      @if (elementos().length > 1) { <button class="enlace enlace--peligro" type="button" (click)="vaciar()">Vaciar la lista</button> }
    </section>
  `,
})
export class BandejaPc implements OnInit {
  private readonly http = inject(HttpClient);
  protected readonly elementos = signal<Elemento[]>([]);

  ngOnInit(): void { void this.cargar(); }

  protected async cargar(): Promise<void> {
    try { this.elementos.set(await firstValueFrom(this.http.get<Elemento[]>('/api/pc/bandeja'))); } catch { /* sin lista: se queda como estaba */ }
  }

  protected hora(iso: string): string { return new Date(iso).toLocaleString('es-ES', { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' }); }

  protected async quitar(e: Elemento): Promise<void> {
    try { await firstValueFrom(this.http.delete(`/api/pc/bandeja/${e.id}`)); } catch { /* ya no estaba */ }
    await this.cargar();
  }

  protected async vaciar(): Promise<void> {
    try { await firstValueFrom(this.http.delete('/api/pc/bandeja')); } catch { /* nada que vaciar */ }
    await this.cargar();
  }
}
