import { HttpClient } from '@angular/common/http';
import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { Avisame } from './avisame';
import { EventosCielo } from './eventos';
import { Cielo } from './cielo';
import { AjustesLuz, Dia, DiaSemana, Semana, Ventana, cardinal, colorNota, creciente, hhmm, instante } from './modelo';

const COLOR_ETAPA: Record<string, string> = { noche: '#070b1c', astronomico: '#0f1a3c', nautico: '#1f2f66', azul: '#3d56a6', dorada: '#f0a24a', dia: '#78b8ee' };
const NOMBRE_ETAPA: Record<string, string> = { noche: 'noche cerrada', astronomico: 'crepúsculo astronómico', nautico: 'crepúsculo náutico', azul: 'hora azul', dorada: 'hora dorada', dia: 'día' };
const ICONO_TIEMPO = {
  sol: 'M12 8a4 4 0 1 0 0 8 4 4 0 0 0 0-8zM12 2v2M12 20v2M2 12h2M20 12h2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4',
  parcial: 'M7 18a4 4 0 0 1 0-8 5 5 0 0 1 9.6-1A4.5 4.5 0 0 1 17 18zM9 6l1 1.5M4 9l1.5.7',
  nube: 'M7 18a4 4 0 0 1 0-8 5 5 0 0 1 9.6-1A4.5 4.5 0 0 1 17 18z',
  lluvia: 'M7 15a4 4 0 0 1 0-8 5 5 0 0 1 9.6-1A4.5 4.5 0 0 1 17 15zM8 18l-1 3M12 18l-1 3M16 18l-1 3',
};

const minDeIso = (iso: string | null): number | null => (iso ? Number(iso.slice(11, 13)) * 60 + Number(iso.slice(14, 16)) : null);
const horaDe = (iso: string | null): string => (iso ? iso.slice(11, 16) : '—');

// «Cuándo salir a hacer fotografía»: el cielo del día con el sol, la luna, las nubes y las sombras en cada hora, las mejores ventanas
// y la semana de un vistazo. El sol y la luna se calculan en el servidor; las nubes vienen de la previsión (Open-Meteo).
@Component({
  selector: 'app-fotografia',
  imports: [Avisame, Cielo, EventosCielo, NgTemplateOutlet, RouterLink],
  styleUrls: ['../gastos/comun.css', './fotografia.css'],
  template: `
    <header class="cab">
      <h2 class="cab__titulo">Cuándo salir a hacer fotografía</h2>
      <p class="cab__sub">{{ dia()?.lugar ? 'Desde ' + dia()!.lugar + ' · ' + fechaLarga() : 'El cielo de hoy, hora a hora' }}</p>
    </header>

    @if (dia()?.sinLugar) {
      <section class="caja"><p class="nada">Para calcular el cielo hace falta tu ciudad. Elígela en la tarjeta del tiempo de <a routerLink="/">Inicio</a>.</p></section>
    } @else if (cargando() && !dia()) {
      <section class="caja esqueleto" aria-busy="true"><div class="esqueleto__cielo"></div></section>
    } @else if (dia(); as d) {

      <section class="escena caja" aria-label="El cielo a la hora elegida">
        <div class="escena__lienzo">
          <app-cielo [i]="inst()!" [fase]="fase()" [etiqueta]="frase()"/>
          <div class="escena__datos" aria-hidden="true">
            <span class="hora">{{ hora() }}</span>
            <span class="etapa">{{ etapaTexto() }}</span>
          </div>
          @if (inst()!.nota != null) {
            <span class="nota" [style.--c]="colorNota(inst()!.nota)" title="Nota para fotografiar a esta hora">{{ inst()!.nota }}<small>/10</small></span>
          }
        </div>
        <div class="controles">
          <button class="boton" type="button" (click)="reproducir()" [attr.aria-pressed]="reproduciendo()">
            <svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path [attr.d]="reproduciendo() ? 'M8 5v14M16 5v14' : 'M7 4l13 8-13 8z'"/></svg>
            {{ reproduciendo() ? 'Pausa' : 'Reproducir el día' }}
          </button>
          @if (d.hoy) { <button class="boton" type="button" (click)="ahora()">Ahora</button> }
          @if (ventanaMejor(); as m) { <button class="boton" type="button" (click)="irA(m)">Mejor momento · {{ horaDe(m.inicio) }}</button> }
        </div>

        <div class="linea">
          <svg class="linea__svg" viewBox="0 0 1440 96" preserveAspectRatio="none" aria-hidden="true">
            @for (b of bandas(); track b.x) { <rect [attr.x]="b.x" y="0" [attr.width]="b.w" height="34" [attr.fill]="b.c"/> }
            @for (v of marcas(); track v.x) { <rect class="marca" [attr.x]="v.x" y="0" [attr.width]="v.w" height="34" [attr.fill]="v.c"/> }
            <polyline [attr.points]="curva()" fill="none" stroke="var(--acento)" stroke-width="3" stroke-linejoin="round" vector-effect="non-scaling-stroke"/>
            <line x1="0" y1="95" x2="1440" y2="95" stroke="var(--vidrio-borde)" vector-effect="non-scaling-stroke"/>
            @if (ahoraX() !== null) { <line [attr.x1]="ahoraX()" [attr.x2]="ahoraX()" y1="0" y2="96" stroke="#fff" stroke-dasharray="4 4" vector-effect="non-scaling-stroke" opacity=".75"/> }
          </svg>
          <input class="linea__rango" type="range" min="0" max="1440" step="10" [value]="min()" (input)="mover($event)" aria-label="Hora del día" [attr.aria-valuetext]="hora() + ', ' + etapaTexto()">
          <div class="linea__horas" aria-hidden="true"><span>00</span><span>06</span><span>12</span><span>18</span><span>24</span></div>
        </div>
      </section>

      @if (d.hayPrevision === false) { <p class="aviso">No hay previsión de nubes ahora mismo (¿sin conexión?): se ve solo lo que calcula la astronomía.</p> }

      <div class="rejilla">
        <section class="caja tarjeta tarjeta--mejor">
          <header class="caja__cab"><h3>Lo mejor de {{ d.hoy ? 'hoy' : 'este día' }}</h3></header>
          @for (v of ordenadas(); track v.tipo) {
            <button type="button" class="ventana" (click)="irA(v)">
              <span class="ventana__nota" [style.--c]="colorNota(v.puntuacion)">{{ v.puntuacion ?? '–' }}</span>
              <span class="ventana__txt"><strong>{{ v.titulo }}</strong>
                <small>{{ horaDe(v.inicio) }}–{{ horaDe(v.fin) }}{{ v.motivo ? ' · ' + v.motivo : '' }}</small></span>
            </button>
          } @empty { <p class="nada">No hay ventanas de luz especiales este día.</p> }
          @if (cuentaAtras()) { <p class="cuenta">{{ cuentaAtras() }}</p> }
        </section>

        <section class="caja tarjeta">
          <header class="caja__cab"><h3>Sol</h3><span class="sub">hacia dónde apuntar</span></header>
          <div class="brujulas">
            <ng-container *ngTemplateOutlet="brujula; context: { titulo: 'Amanece', az: d.efemerides!.azAmanecer, hora: horaDe(d.efemerides!.amanecer) }"/>
            <ng-container *ngTemplateOutlet="brujula; context: { titulo: 'Anochece', az: d.efemerides!.azAtardecer, hora: horaDe(d.efemerides!.atardecer) }"/>
          </div>
          <p class="dato">Mediodía solar {{ horaDe(d.efemerides!.mediodia) }} · sol a {{ d.efemerides!.alturaMediodia.toFixed(0) }}°</p>
          <p class="dato">Hora azul {{ horaDe(d.efemerides!.civilManana) }} · {{ horaDe(d.efemerides!.civilTarde) }}</p>
        </section>

        <section class="caja tarjeta">
          <header class="caja__cab"><h3>Luna</h3><span class="sub">{{ d.efemerides!.fase }}</span></header>
          <div class="luna">
            <svg viewBox="-24 -24 48 48" class="luna__disco" aria-hidden="true">
              <circle r="20" fill="#1d2542"/><path [attr.d]="lunaPath()" [attr.transform]="fase().creciente ? '' : 'scale(-1 1)'" fill="#f4f1e4"/>
            </svg>
            <div><strong>{{ (d.efemerides!.iluminacionLuna * 100).toFixed(0) }} %</strong><small>iluminada</small></div>
          </div>
          <p class="dato">Sale {{ horaDe(d.efemerides!.lunaSale) }} · se pone {{ horaDe(d.efemerides!.lunaPone) }}</p>
          <p class="dato">{{ nucleoTexto() }}</p>
        </section>

        <section class="caja tarjeta">
          <header class="caja__cab"><h3>A las {{ hora() }}</h3><span class="sub">cielo y sombras</span></header>
          @if (inst()!.nubes; as n) {
            @for (c of capas(); track c.n) {
              <div class="capa"><span>{{ c.n }}</span><span class="capa__barra"><i [style.width.%]="c.v"></i></span><b>{{ c.v.toFixed(0) }}%</b></div>
            }
            <p class="dato">Lluvia {{ n.lluvia.toFixed(0) }} % · visibilidad {{ n.vis.toFixed(0) }} km · viento {{ n.viento.toFixed(0) }} km/h</p>
          } @else { <p class="nada">Sin previsión de nubes.</p> }
          <p class="dato">{{ sombraTexto() }}</p>
        </section>
      </div>

      @if (semana()?.dias; as dias) {
        <section class="caja">
          <header class="caja__cab"><h3>La semana</h3><span class="sub">toca un día para ver su cielo</span></header>
          <div class="semana" role="list">
            @for (s of dias; track s.fecha) {
              <button type="button" role="listitem" class="semana__dia" [class.activo]="s.fecha === d.fecha" (click)="cargar(s.fecha)" [attr.aria-label]="etiquetaDia(s)">
                <small>{{ diaCorto(s.fecha) }}</small>
                <svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path [attr.d]="iconoDe(s)"/></svg>
                <span class="semana__nota" [style.--c]="colorNota(s.mejor?.puntuacion ?? null)">{{ s.mejor?.puntuacion ?? '–' }}</span>
                <small>{{ s.mejor ? tipoCorto(s.mejor.tipo) : 'sin luz' }}</small>
              </button>
            }
          </div>
          @if (semana()?.mejores?.length) {
            <ul class="mejores">
              @for (m of semana()!.mejores!; track m.inicio) {
                <li><b [style.color]="colorNota(m.puntuacion)">{{ m.puntuacion }}</b> {{ m.titulo }} · {{ diaLargo(m.inicio) }} {{ horaDe(m.inicio) }}{{ m.motivo ? ' — ' + m.motivo : '' }}</li>
              }
            </ul>
          }
        </section>
      }

      <app-eventos-cielo />

      <app-avisame [ajustes]="ajustes()" [semana]="semana()" (cambiar)="ajustar($event)" />
    }

    <ng-template #brujula let-titulo="titulo" let-az="az" let-hora="hora">
      <div class="brujula">
        <svg viewBox="-30 -30 60 60" aria-hidden="true">
          <circle r="26" fill="none" stroke="var(--vidrio-borde)" stroke-width="1.5"/>
          <text y="-17" text-anchor="middle" font-size="7" fill="var(--soft)">N</text>
          @if (az != null) { <g [attr.transform]="'rotate(' + az + ')'"><path d="M0 -22 L4 2 L0 -2 L-4 2 Z" fill="var(--acento)"/></g> }
        </svg>
        <div><strong>{{ hora }}</strong><small>{{ titulo }}{{ az != null ? ' · ' + cardinalDe(az) + ' ' + az.toFixed(0) + '°' : '' }}</small></div>
      </div>
    </ng-template>
  `,
})
export class Fotografia implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly destruido = inject(DestroyRef);
  protected readonly dia = signal<Dia | null>(null);
  protected readonly semana = signal<Semana | null>(null);
  protected readonly ajustes = signal<AjustesLuz | null>(null);
  protected readonly cargando = signal(true);
  protected readonly min = signal(720);
  protected readonly reproduciendo = signal(false);
  private marco = 0;

  protected readonly colorNota = colorNota;
  protected readonly horaDe = horaDe;
  protected readonly cardinalDe = cardinal;

  protected readonly inst = computed(() => { const s = this.dia()?.serie; return s && s.length > 1 ? instante(s, this.min()) : null; });
  protected readonly hora = computed(() => hhmm(this.min()));
  protected readonly etapaTexto = computed(() => NOMBRE_ETAPA[this.inst()?.etapa ?? 'dia']);
  protected readonly fase = computed(() => ({ iluminacion: this.dia()?.efemerides?.iluminacionLuna ?? 0, creciente: creciente(this.dia()?.efemerides?.fase ?? '') }));
  protected readonly lunaPath = computed(() => {
    const f = this.fase().iluminacion, rx = Math.abs(1 - 2 * f) * 20;
    return `M 0 -20 A 20 20 0 0 1 0 20 A ${rx.toFixed(2)} 20 0 0 ${f < 0.5 ? 0 : 1} 0 -20 Z`;
  });

  protected readonly fechaLarga = computed(() => {
    const f = this.dia()?.fecha;
    return f ? new Date(f + 'T12:00:00').toLocaleDateString('es-ES', { weekday: 'long', day: 'numeric', month: 'long' }) : '';
  });

  protected readonly capas = computed(() => { const n = this.inst()?.nubes; return n ? [{ n: 'Nube baja', v: n.baja }, { n: 'Nube media', v: n.media }, { n: 'Nube alta', v: n.alta }] : []; });
  protected readonly ordenadas = computed(() => [...(this.dia()?.ventanas ?? [])].sort((a, b) => (b.puntuacion ?? -1) - (a.puntuacion ?? -1)));
  protected readonly ventanaMejor = computed(() => this.ordenadas().find((v) => v.puntuacion != null) ?? this.ordenadas()[0] ?? null);

  // las franjas de color del día (una por cada tramo con la misma etapa) y la curva de la nota
  protected readonly bandas = computed(() => {
    const s = this.dia()?.serie ?? [];
    const r: { x: number; w: number; c: string }[] = [];
    for (let i = 0; i < s.length - 1; i++) {
      const c = COLOR_ETAPA[s[i].etapa], u = r[r.length - 1];
      if (u && u.c === c) u.w += 10; else r.push({ x: s[i].min, w: 10, c });
    }
    return r;
  });
  protected readonly curva = computed(() => (this.dia()?.serie ?? []).filter((p) => p.nota != null).map((p) => `${p.min},${92 - p.nota! * 5.4}`).join(' '));
  protected readonly marcas = computed(() => (this.dia()?.ventanas ?? []).map((v) => {
    const a = minDeIso(v.inicio) ?? 0; let b = minDeIso(v.fin) ?? 1440; if (b <= a) b = 1440;
    return { x: a, w: b - a, c: v.puntuacion != null ? colorNota(v.puntuacion) : '#ffffff55' };
  }));
  protected readonly ahoraX = computed(() => (this.dia()?.hoy ? this.minutoAhora() : null));

  protected readonly frase = computed(() => {
    const i = this.inst();
    if (!i) return '';
    const sol = i.sol.alt > 0 ? `sol a ${i.sol.alt.toFixed(0)}° al ${cardinal(i.sol.az)}` : 'sol bajo el horizonte';
    const luna = i.luna.alt > 0 ? `, luna a ${i.luna.alt.toFixed(0)}° al ${cardinal(i.luna.az)}` : '';
    const nubes = i.nubes ? `, nubes al ${i.nubes.total.toFixed(0)} %${i.nubes.lluvia >= 50 ? ' con riesgo de lluvia' : ''}` : '';
    return `${this.hora()}, ${this.etapaTexto()}: ${sol}${luna}${nubes}${i.nota != null ? `. Nota ${i.nota} sobre 10` : ''}.`;
  });

  protected readonly sombraTexto = computed(() => {
    const s = this.inst()?.sombra;
    if (s != null && s >= 15) return 'Sombras larguísimas: el sol está casi en el horizonte.';
    return s == null ? 'Sin sombra: el sol no ha salido o ya se ha puesto.' : `Una persona de 1,75 m proyecta una sombra de ${(s * 1.75).toFixed(1)} m (${s.toFixed(1)}× su altura).`;
  });
  protected readonly nucleoTexto = computed(() => {
    const i = this.inst();
    if (!i) return '';
    const visible = i.nucleo.alt > 10 && (i.etapa === 'noche' || i.etapa === 'astronomico');
    return visible ? `Núcleo de la Vía Láctea a ${i.nucleo.alt.toFixed(0)}° al ${cardinal(i.nucleo.az)}.` : `Núcleo de la Vía Láctea a ${i.nucleo.alt.toFixed(0)}°: ${i.nucleo.alt > 10 ? 'aún hay luz' : 'bajo el horizonte'}.`;
  });

  protected readonly cuentaAtras = computed(() => {
    const d = this.dia();
    const m = this.ventanaMejor();
    if (!d?.hoy || !m || m.puntuacion == null) return '';
    const ini = minDeIso(m.inicio) ?? 0, fin = Math.max(minDeIso(m.fin) ?? 1440, ini), ya = this.minutoAhora();
    if (ya >= ini && ya <= fin) return `${m.titulo}: ahora mismo.`;
    if (ya > fin) return '';
    const f = ini - ya;
    return `${m.titulo}: empieza en ${f >= 60 ? `${Math.floor(f / 60)} h ` : ''}${f % 60} min.`;
  });

  ngOnInit(): void {
    void this.cargar();
    firstValueFrom(this.http.get<Semana>('/api/luz/semana')).then((s) => this.semana.set(s)).catch(() => {});
    firstValueFrom(this.http.get<{ ajustes: AjustesLuz }>('/api/luz')).then((l) => this.ajustes.set(l.ajustes)).catch(() => {});
    this.destruido.onDestroy(() => cancelAnimationFrame(this.marco));
  }

  private minutoAhora(): number { const n = new Date(); return n.getHours() * 60 + n.getMinutes(); }

  protected async cargar(fecha?: string): Promise<void> {
    this.parar();
    try {
      const d = await firstValueFrom(this.http.get<Dia>('/api/luz/dia' + (fecha ? `?fecha=${fecha}` : '')));
      this.dia.set(d);
      if (!d.sinLugar) {
        const mejor = [...(d.ventanas ?? [])].sort((a, b) => (b.puntuacion ?? -1) - (a.puntuacion ?? -1))[0];
        this.min.set(d.hoy ? Math.round(this.minutoAhora() / 10) * 10 : mejor ? Math.round((minDeIso(mejor.inicio) ?? 720) / 10) * 10 : 720);
      }
    } catch { this.dia.set({ sinLugar: false, serie: undefined }); }
    this.cargando.set(false);
  }

  protected mover(e: Event): void { this.parar(); this.min.set(+(e.target as HTMLInputElement).value); }
  protected ahora(): void { this.parar(); this.min.set(this.minutoAhora()); }
  protected irA(v: Ventana): void { this.parar(); this.min.set(minDeIso(v.inicio) ?? 720); }

  // «Reproducir el día»: una hora de reloj por segundo; con movimiento reducido avanza a saltos de media hora para no marear
  protected reproducir(): void {
    if (this.reproduciendo()) { this.parar(); return; }
    if (this.min() >= 1430) this.min.set(0);
    this.reproduciendo.set(true);
    const reducido = matchMedia('(prefers-reduced-motion: reduce)').matches;
    let antes = performance.now();
    const paso = (t: number) => {
      if (!this.reproduciendo()) return;
      const m = this.min() + ((t - antes) / 1000) * (reducido ? 20 : 60);
      antes = t;
      if (m >= 1440) { this.min.set(1440); this.parar(); return; }
      this.min.set(m);
      this.marco = requestAnimationFrame(paso);
    };
    this.marco = requestAnimationFrame(paso);
  }
  private parar(): void { this.reproduciendo.set(false); cancelAnimationFrame(this.marco); }

  protected async ajustar(cambio: Partial<AjustesLuz>): Promise<void> {
    const a = this.ajustes();
    if (!a) return;
    const nuevos = { ...a, ...cambio };
    this.ajustes.set(nuevos);
    await firstValueFrom(this.http.put('/api/luz/ajustes', nuevos));
    firstValueFrom(this.http.get<Semana>('/api/luz/semana')).then((s) => this.semana.set(s)).catch(() => {});
  }

  // ---- semana ----
  protected diaCorto(f: string): string { return new Date(f + 'T12:00:00').toLocaleDateString('es-ES', { weekday: 'short', day: 'numeric' }); }
  protected diaLargo(iso: string): string { return new Date(iso).toLocaleDateString('es-ES', { weekday: 'long', day: 'numeric' }); }
  protected tipoCorto(t: string): string { return t === 'noche' ? 'Vía Láctea' : t === 'amanecer' ? 'amanecer' : 'atardecer'; }
  protected iconoDe(s: DiaSemana): string {
    const n = s.nubes;
    return !n ? ICONO_TIEMPO.sol : n.lluvia >= 50 ? ICONO_TIEMPO.lluvia : n.total >= 70 ? ICONO_TIEMPO.nube : n.total >= 30 ? ICONO_TIEMPO.parcial : ICONO_TIEMPO.sol;
  }
  protected etiquetaDia(s: DiaSemana): string {
    return `${new Date(s.fecha + 'T12:00:00').toLocaleDateString('es-ES', { weekday: 'long', day: 'numeric' })}: ${s.mejor ? `mejor ${this.tipoCorto(s.mejor.tipo)}, nota ${s.mejor.puntuacion}` : 'sin ventanas de luz'}`;
  }
}
