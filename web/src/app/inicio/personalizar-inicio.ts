import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, afterNextRender, computed, inject, signal, viewChild } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { BASE_TAMANO, Diseno, TARJETAS, Tamano, alternarOculta, disenoPorDefecto, esPorDefecto, idDeTarjeta, mover, normalizar, ponerTamano } from './diseno';

// «Personalizar Inicio»: el orden, qué tarjetas se ven y de qué tamaño (S · M · L). Se guarda en el servidor, una distribución para el ordenador y otra para el móvil.
// Se mueve con los botones ▲ ▼ (también con teclado, sin arrastrar). Cada tarjeta se reconoce por su etiqueta y su enlace (`idDeTarjeta`), así que las tarjetas que
// salen solo cuando hay datos (Spotify, la gasolina…) se colocan en cuanto aparecen. El modo pared usa su propia pantalla y no se toca.
@Component({
  selector: 'app-personalizar-inicio',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrls: ['../gastos/dialogos.css'],
  template: `
    <button type="button" class="abrir" (click)="abrir()">Personalizar Inicio</button>
    <dialog class="dlg" #dlg (click)="fuera($event)" aria-labelledby="dlg-per-ini">
      <div class="dlg__caja">
        <header class="dlg__cabeza"><h2 id="dlg-per-ini">Personalizar Inicio</h2>
          <button class="dlg__x" type="button" (click)="dlg.close()" aria-label="Cerrar"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6l12 12M18 6 6 18" /></svg></button></header>
        <p class="dlg__lead">Esta distribución es para {{ dispositivo === 'movil' ? 'el móvil' : 'el ordenador' }}; el otro tipo de dispositivo tiene la suya. Se guarda sola.</p>
        <ol class="lista">
          @for (t of filas(); track t.id; let i = $index; let ultimo = $last) {
            <li [class.oculta]="t.oculta">
              <span class="nombre">{{ t.titulo }}</span>
              <span class="ctl">
                <button type="button" (click)="subir(t.id)" [disabled]="i === 0" [attr.aria-label]="'Subir ' + t.titulo">▲</button>
                <button type="button" (click)="bajar(t.id)" [disabled]="ultimo" [attr.aria-label]="'Bajar ' + t.titulo">▼</button>
                <select [attr.aria-label]="'Tamaño de ' + t.titulo" (change)="tamano(t.id, $any($event.target).value)">
                  <option value="" [selected]="!t.tamano">Auto</option>
                  <option value="S" [selected]="t.tamano === 'S'">S</option><option value="M" [selected]="t.tamano === 'M'">M</option><option value="L" [selected]="t.tamano === 'L'">L</option>
                </select>
                <label class="ver"><input type="checkbox" [checked]="!t.oculta" (change)="alternar(t.id)" [attr.aria-label]="'Mostrar ' + t.titulo"> ver</label>
              </span>
            </li>
          }
        </ol>
        <footer class="dlg__pie"><button class="button button--ghost" type="button" (click)="restablecer()" [disabled]="porDefecto()">Restablecer</button><button class="button" type="button" (click)="dlg.close()">Listo</button></footer>
      </div>
    </dialog>
  `,
  styles: `
    :host { display: flex; flex: 1 1 100%; justify-content: flex-end; }
    .abrir { min-height: 36px; padding: 0 14px; border-radius: 999px; border: 1px solid var(--vidrio-borde); background: none; color: var(--soft); font: inherit; font-size: .82rem; cursor: pointer; }
    .lista { list-style: none; margin: 0; padding: 0; display: grid; gap: 4px; max-height: 56vh; overflow: auto; }
    .lista li { display: flex; align-items: center; justify-content: space-between; gap: 10px; padding: 6px 4px; border-bottom: 1px solid var(--linea); }
    .oculta .nombre { opacity: .5; text-decoration: line-through; }
    .ctl { display: flex; align-items: center; gap: 6px; flex-wrap: wrap; justify-content: flex-end; }
    .ctl button { width: 40px; height: 40px; border-radius: 10px; border: 1px solid var(--linea); background: none; color: var(--texto); cursor: pointer; } .ctl button:disabled { opacity: .35; cursor: default; }
    .ctl select { min-height: 40px; border-radius: 10px; border: 1px solid var(--linea); background: var(--tarjeta-2); color: var(--texto); font: inherit; padding: 0 6px; }
    .ver { display: flex; align-items: center; gap: 6px; min-height: 40px; font-size: .85rem; }
  `,
})
export class PersonalizarInicio {
  private readonly http = inject(HttpClient);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly dlg = viewChild.required<ElementRef<HTMLDialogElement>>('dlg');
  protected readonly dispositivo: 'pc' | 'movil' = matchMedia('(pointer: coarse)').matches || innerWidth < 760 ? 'movil' : 'pc';
  protected readonly diseno = signal<Diseno>(disenoPorDefecto());
  protected readonly porDefecto = computed(() => esPorDefecto(this.diseno()));
  protected readonly filas = computed(() => {
    const d = this.diseno();
    return d.orden.map((id) => ({ id, titulo: TARJETAS.find((t) => t.id === id)?.titulo ?? id, oculta: d.ocultas.includes(id), tamano: d.tamanos[id] as Tamano | undefined }));
  });
  private observador?: MutationObserver;
  private guardado?: ReturnType<typeof setTimeout>;

  constructor() {
    afterNextRender(() => {
      const panel = this.host.nativeElement.parentElement;
      if (!panel) return;
      this.observador = new MutationObserver(() => this.aplicar());
      this.observador.observe(panel, { childList: true });
      void this.cargar();
    });
    inject(DestroyRef).onDestroy(() => { this.observador?.disconnect(); clearTimeout(this.guardado); });
  }

  private async cargar(): Promise<void> {
    try { this.diseno.set(normalizar(await firstValueFrom(this.http.get(`/api/inicio/diseno/${this.dispositivo}`)))); } catch { this.diseno.set(disenoPorDefecto()); }
    this.aplicar();
  }

  // Pone el orden, la visibilidad y el tamaño sobre las tarjetas que haya ahora mismo en pantalla
  private aplicar(): void {
    const panel = this.host.nativeElement.parentElement;
    if (!panel) return;
    const d = this.diseno(), propia = esPorDefecto(d);
    const vistos = new Map<string, number>();
    for (const el of Array.from(panel.children) as HTMLElement[]) {
      if (el === this.host.nativeElement) continue;
      const href = el.getAttribute('href');
      const clave = `${el.tagName}|${href}`;
      const repeticion = vistos.get(clave) ?? 0; vistos.set(clave, repeticion + 1);
      const id = idDeTarjeta({ etiqueta: el.tagName, clases: Array.from(el.classList), href, repeticion });
      if (!id) continue;
      // sin personalizar no se toca nada: queda el mosaico de siempre
      el.style.order = propia ? '' : String(10 + d.orden.indexOf(id));
      el.style.display = d.ocultas.includes(id) ? 'none' : '';
      el.style.flexBasis = d.tamanos[id] ? BASE_TAMANO[d.tamanos[id]] : '';
    }
  }

  protected abrir(): void { this.dlg().nativeElement.showModal(); }
  protected fuera(e: MouseEvent): void { if (e.target === this.dlg().nativeElement) this.dlg().nativeElement.close(); }

  private cambiar(d: Diseno): void {
    this.diseno.set(d); this.aplicar();
    clearTimeout(this.guardado);
    this.guardado = setTimeout(() => void this.guardar(), 600);
  }
  private async guardar(): Promise<void> {
    try { await firstValueFrom(this.http.put(`/api/inicio/diseno/${this.dispositivo}`, this.diseno())); } catch { /* se vuelve a intentar con el siguiente cambio */ }
  }

  protected subir(id: string): void { this.cambiar(mover(this.diseno(), id, -1)); }
  protected bajar(id: string): void { this.cambiar(mover(this.diseno(), id, 1)); }
  protected alternar(id: string): void { this.cambiar(alternarOculta(this.diseno(), id)); }
  protected tamano(id: string, v: string): void { this.cambiar(ponerTamano(this.diseno(), id, v === 'S' || v === 'M' || v === 'L' ? v : null)); }
  protected async restablecer(): Promise<void> {
    clearTimeout(this.guardado);
    this.diseno.set(disenoPorDefecto()); this.aplicar();
    try { await firstValueFrom(this.http.delete(`/api/inicio/diseno/${this.dispositivo}`)); } catch { /* ya estaba así */ }
  }
}
