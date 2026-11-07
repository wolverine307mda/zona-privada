import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import { firstValueFrom } from 'rxjs';

interface Ejercicio { id: string; nombre: string; series: number; reps: string; descansoSeg: number; }
interface Sesion { semana: number; diaSemana: number; tipo: string; titulo: string; minutos: number; ejercicios: Ejercicio[]; descarga: boolean; }
interface Plan { plantilla: string; resumen: string; semanas: number; sesiones: Sesion[]; avisos: string[]; }
interface Hoy {
  tienePerfil: boolean; tienePlan: boolean; hoy: string; sesion: Sesion | null; entrenadoHoy: boolean; dia: { agua: number; sueño: number | null; pasos: number | null; animo: number | null };
  semana: string; semanaDelPlan: number | null; tendenciaKgSemana: number | null; avisoRitmo: string | null; aviso: string;
}
interface Info { id: string; nombre: string; musculos: string; pasos: string[]; errores: string; facil: string; dificil: string; }
interface Medidas { puntos: { fecha: string; peso: number | null; cintura: number | null }[]; peso: { fecha: string; valor: number; media: number }[]; }
interface Asistencia { hayPlan: boolean; previstas: number; hechas: number; asistencia: number; simplificar: boolean; }

const DIAS = ['', 'Lunes', 'Martes', 'Miércoles', 'Jueves', 'Viernes', 'Sábado', 'Domingo'];
const OBJETIVOS = [['grasa', 'Perder grasa'], ['fuerza', 'Ganar fuerza'], ['musculo', 'Ganar músculo'], ['resistencia', 'Resistencia'], ['movilidad', 'Movilidad'], ['salud', 'Salud general']];
const AYUDAS = [
  { t: '💧 Hidratación', x: 'Bebe a lo largo del día; una orina clara es una buena señal. Más si hace calor o entrenas.' },
  { t: '😴 Sueño', x: 'Como referencia general, 7–9 horas. Acostarse y levantarse a horas parecidas ayuda más que recuperar los fines de semana.' },
  { t: '🥚 Proteínas', x: 'Como referencia, entre 1,2 y 2 g por kilo de peso al día si entrenas fuerza. Sin dietas milagro: come variado.' },
  { t: '🔥 Calentar', x: 'Cinco minutos de movimiento suave y una o dos series ligeras del primer ejercicio antes de empezar con peso.' },
  { t: '🛌 Descansar', x: 'El músculo mejora descansando: no entrenes lo mismo dos días seguidos y respeta la semana de descarga.' },
  { t: '📏 Medir el esfuerzo', x: 'Del 1 al 10: un 7 es «podría hacer 3 repeticiones más». No hace falta llegar siempre al límite.' },
  { t: '🦵 Agujetas fuertes', x: 'Las normales duelen un par de días. Si hay mucho dolor, hinchazón, orina oscura o dura más de 5 días, consulta a un profesional.' },
];

// Bienestar: una ayuda personal para moverte más y apuntar tu progreso (NO es consejo médico). Cuestionario de 6 preguntas, un plan por reglas con descarga cada 4 semanas
// y simplificación sin culpa si faltas, registro de entrenos, agua, sueño y pasos, y el peso con media móvil de 7 días. Todo se guarda cifrado.
@Component({
  selector: 'app-bienestar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrls: ['../gastos/comun.css', '../gastos/dialogos.css'],
  template: `
    <p class="legal" role="note">{{ hoy()?.aviso ?? 'Esto es una ayuda personal, no consejo médico. Ante dolor, mareos, enfermedad, lesión o embarazo, consulta a un profesional.' }}</p>
    @if (aviso()) { <p class="aviso" role="status">{{ aviso() }}</p> }

    @if (hoy(); as h) {
      @if (!h.tienePlan) {
        <section class="caja"><h3>Empieza aquí</h3><p class="sub">Seis preguntas y te preparo un plan por semanas, sin dietas ni promesas raras. Puedes cambiarlo cuando quieras.</p>
          <button class="button" type="button" (click)="abrirPerfil()">Hacer el cuestionario</button></section>
      } @else {
        <section class="caja hoy">
          <header class="caja__cab"><h3>Hoy</h3><span class="sub">{{ h.semana }}</span></header>
          @if (h.sesion; as s) {
            <p class="toca"><strong>Hoy toca: {{ s.titulo }}</strong> <small>{{ s.minutos }} min{{ s.descarga ? ' · semana de descarga' : '' }}</small></p>
            <ul class="ejs">@for (e of s.ejercicios; track $index) { <li><button type="button" class="enlace" (click)="verEjercicio(e.id)">{{ e.nombre }}</button> <small>{{ e.series }} × {{ e.reps }}</small></li> }</ul>
          } @else { <p class="sub">Hoy no hay sesión: descansar también es parte del plan. Un paseo siempre suma.</p> }
          <div class="acciones">
            <button class="button" type="button" (click)="entrene(h)" [disabled]="h.entrenadoHoy">{{ h.entrenadoHoy ? '✔ Entrenado hoy' : 'Entrené hoy' }}</button>
            <button class="button button--ghost" type="button" (click)="agua(1)">💧 +1 vaso ({{ h.dia.agua }})</button>
            @if (h.dia.agua > 0) { <button class="button button--ghost" type="button" (click)="agua(-1)" aria-label="Quitar un vaso">−1</button> }
          </div>
          <div class="fila2">
            <label class="campo"><span class="campo__etiqueta">Sueño (horas)</span><input class="campo__input" type="number" step="0.25" min="0" max="24" [value]="h.dia['sueño'] ?? ''" (change)="dia({ 'sueño': num($event) })"></label>
            <label class="campo"><span class="campo__etiqueta">Pasos</span><input class="campo__input" type="number" min="0" max="100000" [value]="h.dia.pasos ?? ''" (change)="dia({ pasos: num($event) })"></label>
          </div>
          @if (asist(); as a) { @if (a.simplificar) {
            <p class="suave">Estas dos semanas ha salido menos de lo previsto ({{ a.hechas }} de {{ a.previstas }}) y no pasa nada. ¿Simplifico lo que queda (un día menos y una serie menos)?
              <button class="button button--ghost" type="button" (click)="simplificar()">Simplificar el plan</button></p>
          } }
        </section>

        <section class="caja">
          <header class="caja__cab"><h3>Tu plan</h3><span class="sub">{{ plan()?.resumen }}</span></header>
          @for (sem of semanas(); track sem.n) {
            <details class="semana" [open]="sem.n === h.semanaDelPlan">
              <summary>Semana {{ sem.n }}{{ sem.descarga ? ' · descarga' : '' }}{{ sem.n === h.semanaDelPlan ? ' · esta' : '' }}</summary>
              <ul>@for (s of sem.sesiones; track s.diaSemana) { <li><strong>{{ nombreDia(s.diaSemana) }}</strong> · {{ s.titulo }} <small>{{ s.minutos }} min</small></li> }</ul>
            </details>
          }
          <p class="sub">@for (a of plan()?.avisos ?? []; track $index) { {{ a }} }</p>
          <button class="enlace" type="button" (click)="abrirPerfil()">Cambiar el cuestionario y rehacer el plan</button>
        </section>
      }

      <section class="caja">
        <header class="caja__cab"><h3>Peso</h3><button class="enlace" type="button" (click)="abrirMedida()">Apuntar medida</button></header>
        @if (medidas()?.peso?.length) {
          <div class="grafica" #grafica role="img" aria-label="Peso con media móvil de 7 días"></div>
          <p class="sub">La línea gruesa es la media de 7 días: es la que importa, el dato de un día sube y baja por muchas cosas.{{ h.tendenciaKgSemana !== null ? ' Tendencia: ' + h.tendenciaKgSemana + ' kg por semana.' : '' }}</p>
          @if (h.avisoRitmo) { <p class="suave">{{ h.avisoRitmo }}</p> }
        } @else { <p class="sub">Aún no hay medidas. Apunta tu peso cuando quieras (una vez por semana basta).</p> }
      </section>
    }

    <section class="caja">
      <header class="caja__cab"><h3>Ayuda</h3></header>
      <div class="ayudas">@for (a of ayudas; track a.t) { <article><strong>{{ a.t }}</strong><p>{{ a.x }}</p></article> }</div>
    </section>

    <section class="caja">
      <header class="caja__cab"><h3>Biblioteca de ejercicios</h3><button class="enlace" type="button" (click)="cargarBiblioteca()">{{ biblioteca().length ? 'Actualizar' : 'Ver' }}</button></header>
      @for (e of biblioteca(); track e.id) {
        <details class="ej"><summary>{{ e.nombre }} <small>{{ e.musculos }}</small></summary>
          <ol>@for (p of e.pasos; track $index) { <li>{{ p }}</li> }</ol><p><b>Errores típicos:</b> {{ e.errores }}</p><p><b>Más fácil:</b> {{ e.facil }} · <b>Más difícil:</b> {{ e.dificil }}</p></details>
      }
    </section>
    <p class="pie"><button class="enlace enlace--mal" type="button" (click)="borrarTodo()">{{ confirmar() ? 'Seguro: borrar todos mis datos de bienestar' : 'Borrar todos mis datos de bienestar' }}</button></p>

    <dialog class="dlg" #dlgPerfil (click)="fuera($event, dlgPerfil)" aria-labelledby="dlg-per">
      <form class="dlg__caja" (submit)="guardarPerfil($event)" novalidate>
        <header class="dlg__cabeza"><h2 id="dlg-per">Cuestionario</h2><button class="dlg__x" type="button" (click)="dlgPerfil.close()" aria-label="Cerrar"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6l12 12M18 6 6 18" /></svg></button></header>
        <label class="campo"><span class="campo__etiqueta">1. Tu objetivo</span><select class="campo__input" (change)="p.objetivo.set($any($event.target).value)">@for (o of objetivos; track o[0]) { <option [value]="o[0]" [selected]="p.objetivo() === o[0]">{{ o[1] }}</option> }</select></label>
        <label class="campo"><span class="campo__etiqueta">2. Tu nivel</span><select class="campo__input" (change)="p.nivel.set($any($event.target).value)">@for (o of niveles; track o[0]) { <option [value]="o[0]" [selected]="p.nivel() === o[0]">{{ o[1] }}</option> }</select></label>
        <label class="campo"><span class="campo__etiqueta">3. Días por semana: {{ p.dias() }}</span><input type="range" min="1" max="6" [value]="p.dias()" (input)="p.dias.set(+$any($event.target).value)"></label>
        <label class="campo"><span class="campo__etiqueta">4. Qué tienes a mano</span><select class="campo__input" (change)="p.equipo.set($any($event.target).value)">@for (o of equipos; track o[0]) { <option [value]="o[0]" [selected]="p.equipo() === o[0]">{{ o[1] }}</option> }</select></label>
        <label class="campo"><span class="campo__etiqueta">5. Limitaciones o lesiones <em>(opcional)</em></span><input class="campo__input" type="text" maxlength="500" [value]="p.limitaciones()" (input)="p.limitaciones.set($any($event.target).value)"></label>
        <label class="campo"><span class="campo__etiqueta">6. Minutos por sesión: {{ p.minutos() }}</span><input type="range" min="15" max="90" step="5" [value]="p.minutos()" (input)="p.minutos.set(+$any($event.target).value)"></label>
        <div class="fila2"><label class="campo"><span class="campo__etiqueta">Altura (cm) <em>(opcional)</em></span><input class="campo__input" type="number" [value]="p.altura()" (input)="p.altura.set($any($event.target).value)"></label>
          <label class="campo"><span class="campo__etiqueta">Año de nacimiento <em>(opcional)</em></span><input class="campo__input" type="number" [value]="p.anio()" (input)="p.anio.set($any($event.target).value)"></label></div>
        @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
        <footer class="dlg__pie"><button class="button" type="submit">Preparar mi plan</button></footer>
      </form>
    </dialog>

    <dialog class="dlg" #dlgMedida (click)="fuera($event, dlgMedida)" aria-labelledby="dlg-med">
      <form class="dlg__caja" (submit)="guardarMedida($event)" novalidate>
        <header class="dlg__cabeza"><h2 id="dlg-med">Apuntar medida</h2><button class="dlg__x" type="button" (click)="dlgMedida.close()" aria-label="Cerrar"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6l12 12M18 6 6 18" /></svg></button></header>
        <div class="fila2"><label class="campo"><span class="campo__etiqueta">Peso (kg)</span><input class="campo__input" type="number" step="0.1" [value]="m.peso()" (input)="m.peso.set($any($event.target).value)"></label>
          <label class="campo"><span class="campo__etiqueta">Cintura (cm)</span><input class="campo__input" type="number" step="0.5" [value]="m.cintura()" (input)="m.cintura.set($any($event.target).value)"></label></div>
        @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
        <footer class="dlg__pie"><button class="button" type="submit">Guardar</button></footer>
      </form>
    </dialog>
  `,
  styles: `
    .legal { margin: 0 0 12px; padding: 10px 14px; border-radius: 12px; background: var(--tarjeta-2); color: var(--soft); font-size: .85rem; line-height: 1.5; }
    .aviso { margin: 0 0 12px; padding: 8px 12px; border-radius: 10px; background: var(--tarjeta-2); font-size: .9rem; }
    .sub { margin: 0; color: var(--soft); font-size: .88rem; line-height: 1.55; }
    .suave { margin: 8px 0 0; padding: 10px 12px; border-radius: 12px; background: color-mix(in srgb, var(--brand) 12%, transparent); font-size: .9rem; display: grid; gap: 8px; }
    .toca { margin: 0 0 6px; } .toca small { color: var(--soft); }
    .ejs { list-style: none; margin: 0 0 10px; padding: 0; display: grid; gap: 4px; } .ejs small { color: var(--soft); margin-left: 8px; }
    .acciones { display: flex; flex-wrap: wrap; gap: 8px; margin: 8px 0; }
    .semana summary, .ej summary { cursor: pointer; min-height: 40px; display: flex; align-items: center; gap: 8px; } .semana ul { margin: 0 0 8px; padding-left: 20px; display: grid; gap: 3px; font-size: .9rem; }
    .ej small { color: var(--soft); } .ej ol { margin: 4px 0; padding-left: 20px; } .ej p { margin: 4px 0; font-size: .88rem; }
    .grafica { width: 100%; height: 220px; }
    .ayudas { display: grid; grid-template-columns: repeat(auto-fill, minmax(240px, 1fr)); gap: 10px; } .ayudas article { padding: 10px 12px; border-radius: 12px; background: var(--tarjeta-2); } .ayudas p { margin: 4px 0 0; font-size: .85rem; color: var(--soft); line-height: 1.5; }
    .enlace { background: none; border: 0; color: var(--brand); font: inherit; cursor: pointer; min-height: 36px; padding: 0; text-align: left; } .enlace--mal { color: var(--bad, #ff5a5a); }
    .pie { margin: 16px 0 0; text-align: center; }
  `,
})
export class Bienestar {
  private readonly http = inject(HttpClient);
  private readonly dlgPerfil = viewChild.required<ElementRef<HTMLDialogElement>>('dlgPerfil');
  private readonly dlgMedida = viewChild.required<ElementRef<HTMLDialogElement>>('dlgMedida');
  private readonly graficaEl = viewChild<ElementRef<HTMLDivElement>>('grafica');
  private grafica: { dispose(): void; setOption(o: unknown, n?: boolean): void; resize(): void } | null = null;

  protected readonly ayudas = AYUDAS;
  protected readonly objetivos = OBJETIVOS;
  protected readonly niveles = [['principiante', 'Principiante'], ['intermedio', 'Intermedio'], ['avanzado', 'Avanzado']];
  protected readonly equipos = [['gimnasio', 'Gimnasio'], ['casa', 'Casa con material'], ['nada', 'Sin nada']];
  protected readonly hoy = signal<Hoy | null>(null);
  protected readonly plan = signal<Plan | null>(null);
  protected readonly medidas = signal<Medidas | null>(null);
  protected readonly asist = signal<Asistencia | null>(null);
  protected readonly biblioteca = signal<Info[]>([]);
  protected readonly aviso = signal('');
  protected readonly error = signal('');
  protected readonly confirmar = signal(false);
  protected readonly semanas = computed(() => {
    const p = this.plan(); if (!p) return [];
    return Array.from({ length: p.semanas }, (_, i) => ({ n: i + 1, sesiones: p.sesiones.filter((s) => s.semana === i + 1), descarga: p.sesiones.some((s) => s.semana === i + 1 && s.descarga) })).filter((x) => x.sesiones.length);
  });
  protected readonly p = { objetivo: signal('salud'), nivel: signal('principiante'), dias: signal(3), equipo: signal('casa'), limitaciones: signal(''), minutos: signal(45), altura: signal(''), anio: signal('') };
  protected readonly m = { peso: signal(''), cintura: signal('') };

  constructor() { void this.cargar(); }

  protected nombreDia(n: number): string { return DIAS[n] ?? ''; }
  protected num(e: Event): number | undefined { const v = (e.target as HTMLInputElement).value; return v === '' ? undefined : Number(v); }

  private async cargar(): Promise<void> {
    try {
      const h = await firstValueFrom(this.http.get<Hoy>('/api/bienestar/hoy')); this.hoy.set(h);
      if (h.tienePlan) {
        const [pl, a] = await Promise.all([firstValueFrom(this.http.get<{ plan: Plan }>('/api/bienestar/plan')), firstValueFrom(this.http.get<Asistencia>('/api/bienestar/asistencia'))]);
        this.plan.set(pl.plan); this.asist.set(a);
      } else { this.plan.set(null); this.asist.set(null); }
      this.medidas.set(await firstValueFrom(this.http.get<Medidas>('/api/bienestar/medidas')));
      setTimeout(() => void this.pintar());
    } catch { this.aviso.set('No se pudo cargar Bienestar.'); }
  }

  private async pintar(): Promise<void> {
    const el = this.graficaEl()?.nativeElement, m = this.medidas();
    if (!el || !m?.peso.length) return;
    const { echarts } = await import('../inicio/graficas');
    const cs = getComputedStyle(document.documentElement), brand = cs.getPropertyValue('--brand').trim() || '#ff7a1f', soft = cs.getPropertyValue('--soft').trim() || '#8d979e';
    this.grafica?.dispose();
    const g = echarts.init(el); this.grafica = g as never;
    g.setOption({
      grid: { left: 6, right: 6, top: 14, bottom: 4, containLabel: true }, tooltip: { trigger: 'axis' },
      xAxis: { type: 'category', data: m.peso.map((p) => p.fecha.slice(5)), axisLabel: { color: soft } }, yAxis: { type: 'value', scale: true, axisLabel: { color: soft } },
      series: [{ name: 'Peso', type: 'line', data: m.peso.map((p) => p.valor), showSymbol: true, symbolSize: 5, lineStyle: { width: 1, color: soft, type: 'dashed' }, itemStyle: { color: soft } },
        { name: 'Media 7 días', type: 'line', data: m.peso.map((p) => p.media), showSymbol: false, smooth: true, lineStyle: { width: 3, color: brand }, itemStyle: { color: brand } }],
    });
  }

  protected async entrene(h: Hoy): Promise<void> {
    try { await firstValueFrom(this.http.post('/api/bienestar/entrenos', { tipo: h.sesion?.tipo ?? 'general', minutos: h.sesion?.minutos ?? 0 })); this.aviso.set('¡Apuntado! Eso es constancia.'); await this.cargar(); } catch { this.aviso.set('No se pudo apuntar.'); }
  }
  protected async agua(n: number): Promise<void> { await this.dia({ sumarAgua: n }); }
  // guarda lo del día (sueño, pasos o agua) y vuelve a leer
  protected async dia(c: Record<string, number | undefined>): Promise<void> { try { await firstValueFrom(this.http.put('/api/bienestar/dia', c)); await this.cargar(); } catch { this.aviso.set('No se pudo guardar.'); } }
  protected async simplificar(): Promise<void> { try { await firstValueFrom(this.http.post('/api/bienestar/plan/simplificar', null)); this.aviso.set('Plan simplificado. Sin culpa.'); await this.cargar(); } catch { this.aviso.set('No se pudo simplificar.'); } }

  protected abrirPerfil(): void { this.error.set(''); this.dlgPerfil().nativeElement.showModal(); }
  protected async guardarPerfil(e: Event): Promise<void> {
    e.preventDefault();
    try {
      await firstValueFrom(this.http.put('/api/bienestar/perfil', { objetivo: this.p.objetivo(), nivel: this.p.nivel(), diasSemana: this.p.dias(), equipo: this.p.equipo(), limitaciones: this.p.limitaciones(), minutos: this.p.minutos(), alturaCm: this.p.altura() ? +this.p.altura() : null, anioNacimiento: this.p.anio() ? +this.p.anio() : null }));
      this.dlgPerfil().nativeElement.close(); this.aviso.set('Plan preparado. Empieza esta semana, a tu ritmo.'); await this.cargar();
    } catch (x) { const er = (x as HttpErrorResponse).error?.errors as Record<string, string[]> | undefined; this.error.set(er ? Object.values(er)[0]?.[0] ?? 'Revisa los datos.' : 'No se pudo guardar.'); }
  }

  protected abrirMedida(): void { this.m.peso.set(''); this.m.cintura.set(''); this.error.set(''); this.dlgMedida().nativeElement.showModal(); }
  protected async guardarMedida(e: Event): Promise<void> {
    e.preventDefault();
    try { await firstValueFrom(this.http.post('/api/bienestar/medidas', { peso: this.m.peso() ? +this.m.peso() : undefined, cintura: this.m.cintura() ? +this.m.cintura() : undefined })); this.dlgMedida().nativeElement.close(); await this.cargar(); }
    catch { this.error.set('Pon un peso o una medida con valores razonables.'); }
  }

  protected async cargarBiblioteca(): Promise<void> { try { this.biblioteca.set(await firstValueFrom(this.http.get<Info[]>('/api/bienestar/ejercicios'))); } catch { this.aviso.set('No se pudo cargar.'); } }
  protected async verEjercicio(id: string): Promise<void> { if (!this.biblioteca().length) await this.cargarBiblioteca(); this.aviso.set(this.biblioteca().find((x) => x.id === id)?.pasos.join(' ') ?? ''); }

  protected async borrarTodo(): Promise<void> {
    if (!this.confirmar()) { this.confirmar.set(true); return; }
    this.confirmar.set(false);
    try { await firstValueFrom(this.http.delete('/api/bienestar/todo')); this.aviso.set('Borrado todo lo de bienestar.'); await this.cargar(); } catch { this.aviso.set('No se pudo borrar.'); }
  }

  protected fuera(e: MouseEvent, d: HTMLDialogElement): void { if (e.target === d) d.close(); }
}
