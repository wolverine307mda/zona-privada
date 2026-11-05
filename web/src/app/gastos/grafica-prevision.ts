import { ChangeDetectionStrategy, Component, ElementRef, OnDestroy, afterNextRender, effect, inject, input, untracked, viewChild } from '@angular/core';
import { TemaService } from '../core/tema.service';
import { echarts, opcionesPrevision } from './graficas-gastos';
import { PrevisionCierre } from './modelo';

// La curva del saldo que se prevé día a día hasta fin de mes: la línea central y, en discontinuo, el peor y el mejor caso.
@Component({
  selector: 'app-grafica-prevision',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<div class="g" #g role="img" [attr.aria-label]="'Saldo previsto cada día hasta fin de mes: de ' + p().saldoActual + ' euros hoy a ' + p().central + ' euros'"></div>`,
  styles: `:host { display: block; } .g { width: 100%; height: 180px; }`,
})
export class GraficaPrevision implements OnDestroy {
  readonly p = input.required<PrevisionCierre>();
  private readonly tema = inject(TemaService);
  private readonly el = viewChild<ElementRef<HTMLDivElement>>('g');
  private grafica?: echarts.ECharts;
  private observador?: ResizeObserver;

  constructor() {
    effect(() => {
      this.tema.cambio();
      this.p();
      untracked(() => setTimeout(() => this.pintar()));
    });
    afterNextRender(() => this.pintar());
  }

  private pintar(): void {
    const el = this.el()?.nativeElement, p = this.p();
    if (!el || !p.serie?.length) return;
    if (!this.grafica) {
      this.grafica = echarts.init(el);
      this.observador = new ResizeObserver(() => this.grafica?.resize());
      this.observador.observe(el);
    }
    this.grafica.setOption(opcionesPrevision(p.serie), true);
  }

  ngOnDestroy(): void {
    this.observador?.disconnect();
    this.grafica?.dispose();
  }
}
