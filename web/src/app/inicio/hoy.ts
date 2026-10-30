import { HttpClient } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { Aparicion, colorEvento, hora, mismoDia } from '../agenda/agenda.service';
import { Deudas } from '../gastos/modelo';
import { euros } from '../gastos/modelo';
import { progresoTareas } from '../notas/markdown';
import { Nota, colorNota } from '../notas/notas.service';

interface Sonando { sonando: boolean; cancion: { nombre: string; artistas: string; imagen: string | null } | null; }

// «Tu día» en el inicio: lo próximo de la agenda, las notas fijadas, lo que suena y cómo va el coche.
// Cada tarjeta se carga por su cuenta y, si su sección no tiene datos, no sale.
@Component({
  selector: 'app-hoy',
  imports: [RouterLink],
  template: `
    @if (proximos().length || recordatorios() || notas().length || sonando()?.cancion || deuda()) {
      <section class="hoy" aria-label="Tu día">
        @if (proximos().length || recordatorios()) {
          <a class="tarjeta hoy__caja" routerLink="/agenda" data-inclina>
            <header><span class="hoy__icono"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M4 5h16v15H4zM4 10h16M8 3v4M16 3v4" /></svg></span><h3>Lo próximo</h3>
              @if (recordatorios()) { <em class="hoy__chip">{{ recordatorios() }} recordatorios</em> }</header>
            <ul class="agenda">
              @for (a of proximos(); track a.clave) {
                <li [style.--c]="colorEvento(a.color)"><time>{{ cuando(a) }}</time><span>{{ a.titulo }}</span></li>
              } @empty { <li class="vacio">Nada en los próximos días</li> }
            </ul>
          </a>
        }
        @if (notas().length) {
          <a class="tarjeta hoy__caja" routerLink="/notas" data-inclina>
            <header><span class="hoy__icono"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M9 4h6l-1 5 4 3v2H6v-2l4-3zM12 14v6" /></svg></span><h3>Notas fijadas</h3></header>
            <ul class="notas">
              @for (n of notas(); track n.id) {
                <li [style.--c]="colorNota(n.color)">
                  <span>{{ n.titulo || n.contenido.split('\\n')[0] }}</span>
                  @if (progresoTareas(n.contenido); as p) { @if (p.total) { <small>{{ p.hechas }}/{{ p.total }}</small> } }
                </li>
              }
            </ul>
          </a>
        }
        @if (sonando()?.cancion; as c) {
          <a class="tarjeta hoy__caja musica" routerLink="/musica" data-inclina>
            @if (c.imagen) { <img class="musica__fondo" [src]="c.imagen" alt=""> }
            <header><span class="hoy__icono"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M9 18V5l12-2v13M9 18a3 3 0 1 1-6 0 3 3 0 0 1 6 0zM21 16a3 3 0 1 1-6 0 3 3 0 0 1 6 0z" /></svg></span><h3>{{ sonando()!.sonando ? 'Sonando' : 'En pausa' }}</h3></header>
            <div class="musica__cancion">
              @if (c.imagen) { <img [src]="c.imagen" alt="" width="64" height="64"> }
              <span><strong>{{ c.nombre }}</strong><small>{{ c.artistas }}</small></span>
            </div>
          </a>
        }
        @if (deuda(); as d) {
          <a class="tarjeta hoy__caja" routerLink="/gastos/deudas" data-inclina>
            <header><span class="hoy__icono"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M5 17h14v-5l-2-5H7l-2 5zM5 12h14M7 17v2M17 17v2" /></svg></span><h3>{{ d.nombre }}</h3></header>
            <div class="deuda">
              <strong>{{ d.porcentaje.toFixed(0) }}<small>%</small></strong>
              <span>quedan {{ euros(d.restante) }}</span>
            </div>
            <span class="barra"><span [style.width.%]="d.porcentaje"></span></span>
          </a>
        }
      </section>
    }
  `,
  styles: `
    .hoy { display: grid; grid-template-columns: repeat(auto-fit, minmax(240px, 1fr)); gap: 20px; }
    .hoy__caja { position: relative; display: flex; flex-direction: column; gap: 14px; padding: 18px; color: var(--texto); text-decoration: none; overflow: hidden; min-width: 0; }
    .hoy__caja header { position: relative; display: flex; align-items: center; gap: 10px; }
    .hoy__caja h3 { flex: 1; font: 700 var(--step-body)/1 var(--font-display); text-transform: uppercase; letter-spacing: -0.02em; }
    .hoy__icono { display: grid; place-items: center; width: 34px; height: 34px; border-radius: 10px; background: color-mix(in srgb, var(--brand) 16%, transparent); color: var(--acento); }
    .hoy__icono .icon { width: 17px; height: 17px; }
    .hoy__chip { font-style: normal; font-size: 11px; padding: 3px 8px; border-radius: 999px; background: var(--brand); color: var(--on-brand); font-weight: 600; }
    ul { display: flex; flex-direction: column; gap: 8px; list-style: none; margin: 0; padding: 0; }
    .agenda li { display: grid; grid-template-columns: max-content minmax(0, 1fr); gap: 10px; padding-left: 10px; border-left: 3px solid var(--c); font-size: 14px; }
    .agenda time { color: var(--soft); font: 500 12px/1.6 var(--font-code); white-space: nowrap; }
    .agenda span, .notas span { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .vacio { color: var(--soft); border: 0 !important; }
    .notas li { display: flex; justify-content: space-between; gap: 10px; padding: 8px 10px; border-radius: 10px; background: color-mix(in srgb, var(--c) 14%, var(--tarjeta-2)); font-size: 14px; }
    .notas small { font: 600 11px/1.6 var(--font-code); color: var(--soft); }
    .musica__fondo { position: absolute; inset: -30%; width: 160%; height: 160%; object-fit: cover; filter: blur(40px) saturate(1.6); opacity: 0.35; pointer-events: none; }
    .musica__cancion { position: relative; display: flex; align-items: center; gap: 12px; min-width: 0; }
    .musica__cancion img { border-radius: 8px; box-shadow: 0 10px 24px -10px rgba(0, 0, 0, 0.6); }
    .musica__cancion span { display: flex; flex-direction: column; min-width: 0; }
    .musica__cancion strong, .musica__cancion small { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .musica__cancion small { color: var(--soft); }
    .deuda { display: flex; align-items: baseline; justify-content: space-between; gap: 10px; }
    .deuda strong { font: 700 2.2rem/1 var(--font-display); letter-spacing: -0.05em; }
    .deuda strong small { font-size: 0.5em; color: var(--soft); }
    .deuda span { color: var(--soft); font-size: 13px; }
    .barra { height: 8px; border-radius: 99px; background: var(--tarjeta-2); overflow: hidden; }
    .barra span { display: block; height: 100%; border-radius: inherit; background: linear-gradient(90deg, color-mix(in srgb, var(--brand) 60%, transparent), var(--brand)); }
  `,
})
export class Hoy {
  private readonly http = inject(HttpClient);
  protected readonly euros = euros;
  protected readonly colorEvento = colorEvento;
  protected readonly colorNota = colorNota;
  protected readonly progresoTareas = progresoTareas;

  private readonly agenda = signal<Aparicion[]>([]);
  protected readonly notas = signal<Nota[]>([]);
  protected readonly sonando = signal<Sonando | null>(null);
  protected readonly deudas = signal<Deudas | null>(null);
  protected readonly proximos = computed(() => this.agenda().filter((a) => !a.hecho).slice(0, 4));
  protected readonly recordatorios = computed(() => this.agenda().filter((a) => a.tipo === 'Recordatorio' && !a.hecho).length);
  protected readonly deuda = computed(() => this.deudas()?.deudas.find((d) => d.restante > 0) ?? null);

  constructor() {
    const get = <T>(url: string) => firstValueFrom(this.http.get<T>(url));
    get<Aparicion[]>('/api/agenda/proximos?dias=7').then((a) => this.agenda.set(a)).catch(() => {});
    get<Nota[]>('/api/notas').then((n) => this.notas.set(n.filter((x) => x.fijada).slice(0, 3))).catch(() => {});
    get<Sonando>('/api/musica/ahora').then((s) => this.sonando.set(s)).catch(() => {});
    get<Deudas>('/api/gastos/deudas').then((d) => this.deudas.set(d)).catch(() => {});
  }

  protected cuando(a: Aparicion): string {
    const i = new Date(a.inicio), hoy = new Date(), man = new Date(hoy.getFullYear(), hoy.getMonth(), hoy.getDate() + 1);
    const dia = mismoDia(i, hoy) ? 'Hoy' : mismoDia(i, man) ? 'Mañana' : i < hoy ? 'Atrasado' : i.toLocaleDateString('es-ES', { weekday: 'short', day: 'numeric' });
    return a.todoElDia ? dia : `${dia} ${hora(i)}`;
  }
}
