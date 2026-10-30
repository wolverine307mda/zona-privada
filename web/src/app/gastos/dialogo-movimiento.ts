import { Component, ElementRef, computed, effect, inject, signal, untracked, viewChild } from '@angular/core';
import { Errores, GastosService, erroresDe } from './gastos.service';
import { CATEGORIAS, Tipo, euros, hoyIso, numero } from './modelo';

// Diálogo para apuntar o editar un gasto / ingreso. Con «Gasolina» pide litros, €/litro y kilómetros,
// y el importe se calcula solo (litros × precio) mientras no lo cambies a mano.
@Component({
  selector: 'app-dialogo-movimiento',
  templateUrl: './dialogo-movimiento.html',
  styleUrls: ['./dialogos.css'],
})
export class DialogoMovimiento {
  private readonly gastos = inject(GastosService);
  private readonly dialogo = viewChild.required<ElementRef<HTMLDialogElement>>('dialogo');

  protected readonly id = signal<number | null>(null);
  protected readonly tipo = signal<Tipo>('Gasto');
  protected readonly categoria = signal('supermercado');
  protected readonly importe = signal('');
  protected readonly concepto = signal('');
  protected readonly fecha = signal(hoyIso());
  protected readonly notas = signal('');
  protected readonly litros = signal('');
  protected readonly precio = signal('');
  protected readonly km = signal('');
  protected readonly importeAMano = signal(false);
  protected readonly errores = signal<Errores>({});
  protected readonly guardando = signal(false);
  protected readonly confirmarBorrado = signal(false);

  protected readonly categorias = computed(() => CATEGORIAS.filter((c) => c.tipo === this.tipo()));
  protected readonly esGasolina = computed(() => this.categoria() === 'gasolina');
  protected readonly calculado = computed(() => {
    const l = this.num(this.litros()), p = this.num(this.precio());
    return l && p ? Math.round(l * p * 100) / 100 : null;
  });
  protected readonly euros = euros;
  protected readonly numero = numero;

  constructor() {
    effect(() => {
      const e = this.gastos.editandoMovimiento();
      if (!e) return;
      untracked(() => this.abrir(e));
    });
    // gasolina: el importe sigue a litros × precio hasta que lo toques
    effect(() => {
      const c = this.calculado();
      if (c != null && this.esGasolina() && !untracked(this.importeAMano)) this.importe.set(this.coma(c));
    });
  }

  private abrir(e: NonNullable<ReturnType<GastosService['editandoMovimiento']>>): void {
    const m = e.movimiento;
    this.id.set(m?.id ?? null);
    this.tipo.set(m?.tipo ?? e.tipo ?? 'Gasto');
    this.categoria.set(m?.categoria ?? e.categoria ?? (this.tipo() === 'Gasto' ? 'supermercado' : 'nomina'));
    this.importe.set(m ? this.coma(m.importe) : '');
    this.concepto.set(m?.concepto ?? '');
    this.fecha.set(m?.fecha ?? (this.gastos.esMesActual() ? hoyIso() : `${this.gastos.anio()}-${String(this.gastos.mes()).padStart(2, '0')}-01`));
    this.notas.set(m?.notas ?? '');
    this.litros.set(m?.litros != null ? this.coma(m.litros) : '');
    this.precio.set(m?.precioLitro != null ? this.coma(m.precioLitro) : '');
    this.km.set(m?.kilometros != null ? String(m.kilometros) : '');
    this.importeAMano.set(!!m);
    this.errores.set({});
    this.confirmarBorrado.set(false);
    this.dialogo().nativeElement.showModal();
  }

  protected valor(e: Event): string { return (e.target as HTMLInputElement).value; }
  private coma(n: number): string { return String(n).replace('.', ','); }

  // admite coma o punto decimal
  private num(s: string): number | null {
    const n = Number(String(s).replace(',', '.'));
    return s.trim() && Number.isFinite(n) ? n : null;
  }

  protected ponerTipo(t: Tipo): void {
    if (t === this.tipo()) return;
    this.tipo.set(t);
    this.categoria.set(t === 'Gasto' ? 'supermercado' : 'nomina');
  }

  protected ponerImporte(e: Event): void {
    this.importe.set(this.valor(e));
    this.importeAMano.set(true);
  }

  protected async guardar(e: Event): Promise<void> {
    e.preventDefault();
    this.guardando.set(true);
    const concepto = this.concepto().trim() || CATEGORIAS.find((c) => c.id === this.categoria())?.nombre || '';
    try {
      await this.gastos.guardarMovimiento({
        tipo: this.tipo(), fecha: this.fecha(), importe: this.num(this.importe()) ?? 0, categoria: this.categoria(), concepto,
        notas: this.notas().trim() || null,
        litros: this.esGasolina() ? this.num(this.litros()) : null,
        precioLitro: this.esGasolina() ? this.num(this.precio()) : null,
        kilometros: this.esGasolina() && this.num(this.km()) != null ? Math.round(this.num(this.km())!) : null,
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
    await this.gastos.borrarMovimiento(this.id()!);
    this.cerrar();
    this.gastos.recargar();
  }

  protected cerrar(): void { this.dialogo().nativeElement.close(); }
  protected alCerrar(): void { this.gastos.editandoMovimiento.set(null); }
  protected fuera(e: MouseEvent): void { if (e.target === this.dialogo().nativeElement) this.cerrar(); }
}
