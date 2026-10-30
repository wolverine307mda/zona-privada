import { Component, ElementRef, computed, effect, inject, signal, untracked, viewChild } from '@angular/core';
import { Errores, GastosService, erroresDe } from './gastos.service';
import { CATEGORIAS, FRECUENCIAS, Frecuencia, Tipo, euros, hoyIso } from './modelo';

// Diálogo de un fijo: suscripción, alquiler, cuota del coche, seguro, nómina…
@Component({
  selector: 'app-dialogo-fijo',
  templateUrl: './dialogo-fijo.html',
  styleUrls: ['./dialogos.css'],
})
export class DialogoFijo {
  private readonly gastos = inject(GastosService);
  private readonly dialogo = viewChild.required<ElementRef<HTMLDialogElement>>('dialogo');

  protected readonly id = signal<number | null>(null);
  protected readonly nombre = signal('');
  protected readonly tipo = signal<Tipo>('Gasto');
  protected readonly categoria = signal('suscripciones');
  protected readonly importe = signal('');
  protected readonly variable = signal(false);
  protected readonly dia = signal('1');
  protected readonly frecuencia = signal<Frecuencia>('Mensual');
  protected readonly inicio = signal(hoyIso());
  protected readonly fin = signal('');
  protected readonly activo = signal(true);
  protected readonly errores = signal<Errores>({});
  protected readonly guardando = signal(false);
  protected readonly confirmarBorrado = signal(false);

  protected readonly categorias = computed(() => CATEGORIAS.filter((c) => c.tipo === this.tipo() && c.id !== 'gasolina'));
  protected readonly frecuencias = FRECUENCIAS;
  protected readonly alMes = computed(() => {
    const n = Number(this.importe().replace(',', '.'));
    if (!n) return null;
    return this.frecuencia() === 'Anual' ? n / 12 : this.frecuencia() === 'Trimestral' ? n / 3 : n;
  });
  protected readonly euros = euros;

  constructor() {
    effect(() => {
      const e = this.gastos.editandoFijo();
      if (!e) return;
      untracked(() => this.abrir(e));
    });
  }

  private abrir(e: NonNullable<ReturnType<GastosService['editandoFijo']>>): void {
    const r = e.fijo;
    this.id.set(r?.id ?? null);
    this.nombre.set(r?.nombre ?? '');
    this.tipo.set(r?.tipo ?? e.tipo ?? 'Gasto');
    this.categoria.set(r?.categoria ?? e.categoria ?? (this.tipo() === 'Gasto' ? 'suscripciones' : 'nomina'));
    this.importe.set(r ? String(r.importe).replace('.', ',') : '');
    this.variable.set(r?.variable ?? false);
    this.dia.set(String(r?.dia ?? new Date().getDate()));
    this.frecuencia.set(r?.frecuencia ?? 'Mensual');
    this.inicio.set(r?.inicio ?? hoyIso());
    this.fin.set(r?.fin ?? '');
    this.activo.set(r?.activo ?? true);
    this.errores.set({});
    this.confirmarBorrado.set(false);
    this.dialogo().nativeElement.showModal();
  }

  protected valor(e: Event): string { return (e.target as HTMLInputElement).value; }

  protected ponerTipo(t: Tipo): void {
    if (t === this.tipo()) return;
    this.tipo.set(t);
    this.categoria.set(t === 'Gasto' ? 'suscripciones' : 'nomina');
  }

  protected async guardar(e: Event): Promise<void> {
    e.preventDefault();
    this.guardando.set(true);
    try {
      await this.gastos.guardarFijo({
        nombre: this.nombre().trim(), tipo: this.tipo(), categoria: this.categoria(),
        importe: Number(this.importe().replace(',', '.')) || 0, variable: this.variable(),
        dia: Number(this.dia()) || 1, frecuencia: this.frecuencia(), inicio: this.inicio(), fin: this.fin() || null, activo: this.activo(),
      }, this.id() ?? undefined);
      this.cerrar();
      this.gastos.recargar();
    } catch (err) {
      this.errores.set(erroresDe(err));
    } finally {
      this.guardando.set(false);
    }
  }

  protected async borrar(): Promise<void> {
    if (!this.confirmarBorrado()) { this.confirmarBorrado.set(true); return; }
    await this.gastos.borrarFijo(this.id()!);
    this.cerrar();
    this.gastos.recargar();
  }

  protected cerrar(): void { this.dialogo().nativeElement.close(); }
  protected alCerrar(): void { this.gastos.editandoFijo.set(null); }
  protected fuera(e: MouseEvent): void { if (e.target === this.dialogo().nativeElement) this.cerrar(); }
}
