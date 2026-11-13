import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { EstadoError, EstadoVacio, Esqueleto } from '../ui';
import { SalidaCasa } from './salida-casa';

interface Regla {
  id: number; nombre: string; activa: boolean; evento: 'llego' | 'salgo' | 'duermo' | 'despierto' | 'hora'; desde: string | null; hasta: string | null; dias: number; tiempo: string;
  soloEnCasa: boolean; escenaCasaId: number | null; escenaPcId: number | null; aviso: string; esperaMinutos: number; ultima: string | null; resultado: string;
}
interface Escena { id: number; nombre: string; icono: string; apaga?: boolean; }
interface Estado {
  presencia: { estado: 'casa' | 'fuera' | '?'; desde: string | null };
  tiempo: { temperatura: number; texto: string; lluvia: boolean } | null;
  reglas: Regla[]; escenasCasa: Escena[]; escenasPc: Escena[];
}

const DIAS = ['L', 'M', 'X', 'J', 'V', 'S', 'D'];
const NOMBRE_DIAS = ['lunes', 'martes', 'miércoles', 'jueves', 'viernes', 'sábado', 'domingo'];
const TIEMPOS: Record<string, string> = { '': 'Da igual el tiempo', lluvia: 'Si llueve', seco: 'Si no llueve', frio: 'Si hace frío (10° o menos)', calor: 'Si hace calor (28° o más)' };

// Escenas por contexto: «cuando llegue a casa, de noche y si llueve, pon la escena Cine». El iPhone avisa al llegar o salir (una automatización de Atajos con el permiso «casa»)
// y las reglas de «a una hora» las mira el servidor cada minuto. Una regla puede poner una escena de casa, una del PC (las que apagan el equipo nunca se ponen solas) y mandarte un aviso.
@Component({
  selector: 'app-contexto',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [EstadoError, EstadoVacio, Esqueleto, SalidaCasa],
  styleUrls: ['../gastos/comun.css', '../gastos/dialogos.css'],
  template: `
    <header class="cab">
      <p class="cab__sub" aria-live="polite">{{ resumen() }}</p>
      <button class="button" type="button" (click)="nueva()">Nueva regla</button>
    </header>
    @if (aviso()) { <p class="ok" role="status">{{ aviso() }}</p> }

    @if (fallo()) { <app-estado-error titulo="No pude cargar las escenas" (reintentar)="cargar()" /> }
    @else if (datos() === null) { <section class="caja"><app-esqueleto forma="bloque" alto="88px" /><app-esqueleto forma="bloque" alto="72px" /></section> }
    @else {
      <section class="caja donde" [attr.data-e]="datos()!.presencia.estado">
        <div class="donde__texto">
          <strong>{{ dondeTexto() }}</strong>
          <small>{{ climaTexto() }}</small>
        </div>
        <div class="donde__botones">
          <button type="button" class="button" [disabled]="ocupado()" (click)="evento('llego')">Llegué a casa</button>
          <button type="button" class="button button--ghost button--sm" [disabled]="ocupado()" (click)="evento('salgo')">Me voy</button>
          <button type="button" class="button button--ghost button--sm" [disabled]="ocupado()" (click)="rutina('duermo')">Buenas noches</button>
          <button type="button" class="button button--ghost button--sm" [disabled]="ocupado()" (click)="rutina('despierto')">Buenos días</button>
        </div>
        <p class="nota">Esto hace lo mismo que tu iPhone al llegar o salir: apunta dónde estás y aplica tus reglas.</p>
      </section>

      @if (datos()!.reglas.length === 0) {
        <app-estado-vacio icono="M12 3 4 7v6c0 4 3.5 7 8 8 4.5-1 8-4 8-8V7zM9 12l2 2 4-4" titulo="Aún no tienes reglas" texto="Por ejemplo: al llegar a casa de noche y con lluvia, escena «Cine»; o cada día a las 23:00, escena «Dormir».">
          <button class="button" type="button" (click)="nueva()">Crear la primera</button>
        </app-estado-vacio>
      } @else {
        <ul class="lista" aria-label="Reglas">
          @for (r of datos()!.reglas; track r.id) {
            <li class="regla" [class.regla--off]="!r.activa">
              <label class="interruptor" [title]="r.activa ? 'Activa' : 'Apagada'">
                <input type="checkbox" [checked]="r.activa" (change)="alternar(r, $any($event.target).checked)" [attr.aria-label]="'Activar ' + r.nombre">
                <span class="interruptor__pista" aria-hidden="true"></span>
              </label>
              <button type="button" class="regla__texto" (click)="editar(r)" [attr.aria-label]="'Editar ' + r.nombre">
                <strong>{{ r.nombre }}</strong>
                <small>{{ descripcion(r) }}</small>
                @if (r.ultima) { <small>Última vez {{ hora(r.ultima) }}{{ r.resultado ? ': ' + r.resultado : '' }}</small> }
              </button>
              <button type="button" class="button button--ghost button--sm" [disabled]="probando() === r.id" (click)="probar(r)">Probar</button>
            </li>
          }
        </ul>
      }

      <details class="caja ayuda">
        <summary>Cómo avisar desde el iPhone</summary>
        <ol>
          <li>En <strong>Seguridad › Atajos</strong> crea un token con el permiso <strong>casa</strong> y copia su valor.</li>
          <li>En la app <strong>Atajos</strong> ve a <strong>Automatización › + › Llegar</strong> (elige tu casa) y márcala como «Ejecutar inmediatamente».</li>
          <li>Añade <strong>Obtener contenido de URL</strong>: dirección <code>{{ origen }}/api/atajos/casa</code>, método <strong>POST</strong>, cabecera <code>Authorization</code> = <code>Bearer tu-token</code> y cuerpo JSON con <code>estado</code> = <code>llegada</code>.</li>
          <li>Repite con <strong>Salir</strong> y <code>estado</code> = <code>salida</code>.</li>
        </ol>
      </details>
    }

    <app-salida-casa />

    <dialog class="dlg" #dialogo (click)="fuera($event)" (close)="cerrado()" aria-labelledby="dlg-regla">
      <form class="dlg__caja" (submit)="guardar($event)" novalidate>
        <header class="dlg__cabeza">
          <h2 id="dlg-regla">{{ editando() ? 'Editar regla' : 'Nueva regla' }}</h2>
          <button class="dlg__x" type="button" (click)="cerrar()" aria-label="Cerrar"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6l12 12M18 6 6 18" /></svg></button>
        </header>
        <label class="campo"><span class="campo__etiqueta">Nombre</span>
          <input class="campo__input" type="text" maxlength="60" placeholder="Noche de cine" [value]="nombre()" (input)="nombre.set(valor($event))"></label>
        <label class="campo"><span class="campo__etiqueta">Cuándo</span>
          <select class="campo__input" [value]="evento_()" (change)="evento_.set(valor($event))">
            <option value="llego">Cuando llegue a casa</option>
            <option value="salgo">Cuando salga de casa</option>
            <option value="duermo">Cuando me vaya a dormir</option>
            <option value="despierto">Cuando me despierte</option>
            <option value="hora">Todos los días a una hora</option>
          </select></label>
        <div class="fila2">
          <label class="campo"><span class="campo__etiqueta">{{ evento_() === 'hora' ? 'A las' : 'Desde' }} <em>{{ evento_() === 'hora' ? '' : '(opcional)' }}</em></span>
            <input class="campo__input" type="time" [value]="desde()" (input)="desde.set(valor($event))"></label>
          <label class="campo"><span class="campo__etiqueta">Hasta <em>(opcional)</em></span>
            <input class="campo__input" type="time" [value]="hasta()" (input)="hasta.set(valor($event))"></label>
        </div>
        <fieldset class="cats">
          <legend class="campo__etiqueta">Qué días</legend>
          <div class="dias">
            @for (d of dias; track d; let i = $index) {
              <button type="button" [attr.aria-pressed]="tocaDia(i)" [attr.aria-label]="nombreDia(i)" (click)="alternarDia(i)">{{ d }}</button>
            }
          </div>
        </fieldset>
        <label class="campo"><span class="campo__etiqueta">Con qué tiempo</span>
          <select class="campo__input" [value]="tiempo()" (change)="tiempo.set(valor($event))">
            @for (t of tiempos; track t[0]) { <option [value]="t[0]">{{ t[1] }}</option> }
          </select></label>
        @if (evento_() === 'hora') {
          <label class="campo campo--linea"><input type="checkbox" [checked]="soloEnCasa()" (change)="soloEnCasa.set($any($event.target).checked)"> <span>Solo si estoy en casa</span></label>
        }
        <label class="campo"><span class="campo__etiqueta">Escena de casa <em>(luces, aire, tele…)</em></span>
          <select class="campo__input" [value]="escenaCasa()" (change)="escenaCasa.set(valor($event))">
            <option value="">Ninguna</option>
            @for (e of datos()?.escenasCasa ?? []; track e.id) { <option [value]="e.id">{{ e.icono }} {{ e.nombre }}</option> }
          </select></label>
        <label class="campo"><span class="campo__etiqueta">Escena del PC</span>
          <select class="campo__input" [value]="escenaPc()" (change)="escenaPc.set(valor($event))">
            <option value="">Ninguna</option>
            @for (e of datos()?.escenasPc ?? []; track e.id) { <option [value]="e.id" [disabled]="e.apaga">{{ e.icono }} {{ e.nombre }}{{ e.apaga ? ' (apaga el PC: no se pone sola)' : '' }}</option> }
          </select></label>
        <label class="campo"><span class="campo__etiqueta">Mensaje por Telegram <em>(opcional)</em></span>
          <input class="campo__input" type="text" maxlength="120" [value]="avisoTexto()" (input)="avisoTexto.set(valor($event))"></label>
        <label class="campo"><span class="campo__etiqueta">Esperar entre una vez y otra <em>(minutos)</em></span>
          <input class="campo__input" type="number" inputmode="numeric" min="1" max="1440" [value]="espera()" (input)="espera.set(valor($event))"></label>
        @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
        <footer class="dlg__pie">
          @if (editando()) { <button type="button" class="boton-borrar" (click)="borrar()">{{ confirmarBorrado() ? 'Seguro: borrar' : 'Borrar' }}</button> }
          <button class="button" type="submit" [attr.aria-busy]="guardando() || null" [disabled]="guardando()">Guardar</button>
        </footer>
      </form>
    </dialog>
  `,
  styles: `
    .cab__sub { margin: 0; font: var(--peso-ligero) 1.3rem/1.2 var(--font-text); }
    .cab { display: flex; align-items: center; justify-content: space-between; gap: 12px; flex-wrap: wrap; margin-bottom: var(--esp-4); }
    .ok { margin: 0 0 var(--esp-3); padding: 10px 14px; border-radius: 12px; background: color-mix(in srgb, var(--ok) 18%, transparent); }
    .donde { display: grid; gap: 12px; margin-bottom: var(--esp-4); }
    .donde[data-e='casa'] { border-color: color-mix(in srgb, var(--ok) 50%, transparent); }
    .donde__texto { display: grid; gap: 2px; }
    .donde__texto strong { font: var(--peso-ligero) 1.35rem/1.2 var(--font-text); }
    .donde__texto small, .nota { color: var(--soft); font-size: .8rem; margin: 0; }
    .donde__botones { display: flex; flex-wrap: wrap; gap: 10px; }
    .lista { list-style: none; margin: 0 0 var(--esp-4); padding: 0; display: grid; gap: 8px; }
    .regla { display: grid; grid-template-columns: auto minmax(0, 1fr) auto; align-items: center; gap: 12px; padding: 12px 16px; border-radius: var(--radio-l); border: 1px solid var(--vidrio-borde); background: var(--vidrio); }
    .regla--off { opacity: .6; }
    .regla__texto { display: grid; gap: 2px; min-width: 0; padding: 0; border: 0; background: none; color: inherit; font: inherit; text-align: left; cursor: pointer; }
    .regla__texto strong { font-weight: 500; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .regla__texto small { color: var(--soft); font-size: .78rem; }
    .ayuda summary { cursor: pointer; min-height: 44px; display: flex; align-items: center; font-weight: 500; }
    .ayuda ol { margin: 8px 0 0; padding-left: 1.2em; display: grid; gap: 8px; color: var(--soft); font-size: .86rem; }
    .ayuda code { word-break: break-all; }
    .dias { display: grid; grid-template-columns: repeat(7, minmax(0, 1fr)); gap: 6px; margin-top: 6px; }
    .dias button { min-height: 44px; border-radius: 50%; border: 1px solid var(--linea); background: transparent; color: var(--texto); font-weight: 600; cursor: pointer; }
    .dias button[aria-pressed='true'] { background: var(--brand); border-color: var(--brand); color: var(--on-brand); }
    .campo--linea { display: flex; align-items: center; gap: 10px; }
  `,
})
export class Contexto {
  private readonly http = inject(HttpClient);
  private readonly dialogo = viewChild.required<ElementRef<HTMLDialogElement>>('dialogo');
  protected readonly dias = DIAS;
  protected readonly tiempos = Object.entries(TIEMPOS);
  protected readonly origen = location.origin;

  protected readonly datos = signal<Estado | null>(null);
  protected readonly fallo = signal(false);
  protected readonly aviso = signal('');
  protected readonly ocupado = signal(false);
  protected readonly probando = signal<number | null>(null);

  protected readonly editando = signal<Regla | null>(null);
  protected readonly nombre = signal('');
  protected readonly evento_ = signal('llego');
  protected readonly desde = signal('');
  protected readonly hasta = signal('');
  protected readonly mascara = signal(127);
  protected readonly tiempo = signal('');
  protected readonly soloEnCasa = signal(false);
  protected readonly escenaCasa = signal('');
  protected readonly escenaPc = signal('');
  protected readonly avisoTexto = signal('');
  protected readonly espera = signal('60');
  protected readonly error = signal('');
  protected readonly guardando = signal(false);
  protected readonly confirmarBorrado = signal(false);

  protected readonly resumen = computed(() => {
    const d = this.datos();
    if (!d) return 'Que la casa se adapte a ti';
    const activas = d.reglas.filter((r) => r.activa).length;
    return d.reglas.length === 0 ? 'Sin reglas todavía' : `${activas} regla${activas === 1 ? '' : 's'} activa${activas === 1 ? '' : 's'}`;
  });
  protected readonly dondeTexto = computed(() => {
    const p = this.datos()?.presencia;
    if (!p || p.estado === '?') return 'Aún no sé si estás en casa';
    return `${p.estado === 'casa' ? 'Estás en casa' : 'Estás fuera'}${p.desde ? ' desde ' + this.hora(p.desde) : ''}`;
  });
  protected readonly climaTexto = computed(() => {
    const t = this.datos()?.tiempo;
    return t ? `Ahora ${Math.round(t.temperatura)}° · ${t.texto}${t.lluvia ? ' · llueve' : ''}` : 'Sin datos del tiempo (elige tu sitio en Inicio › Tiempo)';
  });

  constructor() { void this.cargar(); }

  protected async cargar(): Promise<void> {
    this.fallo.set(false);
    try { this.datos.set(await firstValueFrom(this.http.get<Estado>('/api/contexto'))); } catch { this.fallo.set(true); }
  }

  protected hora(iso: string): string { return new Date(iso).toLocaleString('es-ES', { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' }); }

  protected descripcion(r: Regla): string {
    const d = this.datos();
    const cuando = r.evento === 'llego' ? 'Al llegar a casa' : r.evento === 'salgo' ? 'Al salir de casa' : r.evento === 'duermo' ? 'Al irme a dormir' : r.evento === 'despierto' ? 'Al despertar' : `Cada día a las ${r.desde}`;
    const partes = [cuando];
    if (r.evento !== 'hora' && (r.desde || r.hasta)) partes.push(r.desde && r.hasta ? `de ${r.desde} a ${r.hasta}` : r.desde ? `desde las ${r.desde}` : `hasta las ${r.hasta}`);
    if (r.dias !== 127) partes.push(NOMBRE_DIAS.filter((_, i) => r.dias & (1 << i)).map((n) => n.slice(0, 3)).join(' '));
    if (r.tiempo) partes.push(TIEMPOS[r.tiempo].toLowerCase());
    if (r.soloEnCasa) partes.push('si estás en casa');
    const acciones: string[] = [];
    const casa = d?.escenasCasa.find((e) => e.id === r.escenaCasaId); if (casa) acciones.push(`${casa.icono} ${casa.nombre}`);
    const pc = d?.escenasPc.find((e) => e.id === r.escenaPcId); if (pc) acciones.push(`💻 ${pc.nombre}`);
    if (r.aviso) acciones.push('mensaje');
    return `${partes.join(' · ')} → ${acciones.join(' + ') || 'nada'}`;
  }

  protected async alternar(r: Regla, activa: boolean): Promise<void> {
    try { await firstValueFrom(this.http.put(`/api/contexto/${r.id}`, { activa })); await this.cargar(); } catch { void this.cargar(); }
  }

  protected async probar(r: Regla): Promise<void> {
    if (this.probando() !== null) return;
    this.probando.set(r.id); this.aviso.set('');
    try {
      const x = await firstValueFrom(this.http.post<{ resumen: string }>(`/api/contexto/${r.id}/probar`, {}));
      this.aviso.set(`Regla aplicada: ${x.resumen}.`); await this.cargar();
    } catch { this.aviso.set('No se pudo probar.'); } finally { this.probando.set(null); }
  }

  // la rutina de la noche o de la mañana: aplica tus reglas y enseña lo que contestaría el atajo
  protected async rutina(e: 'duermo' | 'despierto'): Promise<void> {
    if (this.ocupado()) return;
    this.ocupado.set(true); this.aviso.set('');
    try { this.aviso.set((await firstValueFrom(this.http.post<{ mensaje: string }>('/api/contexto/dormir', { evento: e }))).mensaje); await this.cargar(); }
    catch { this.aviso.set('No se pudo.'); } finally { this.ocupado.set(false); }
  }

  protected async evento(e: 'llego' | 'salgo'): Promise<void> {
    if (this.ocupado()) return;
    this.ocupado.set(true); this.aviso.set('');
    try { await firstValueFrom(this.http.post('/api/contexto/evento', { evento: e })); this.aviso.set(e === 'llego' ? 'Bienvenido a casa: reglas aplicadas.' : 'Hasta luego: reglas aplicadas.'); await this.cargar(); }
    catch { this.aviso.set('No se pudo.'); } finally { this.ocupado.set(false); }
  }

  // ---- diálogo ----
  protected nueva(): void { this.abrir(null); }
  protected editar(r: Regla): void { this.abrir(r); }

  private abrir(r: Regla | null): void {
    this.editando.set(r);
    this.nombre.set(r?.nombre ?? ''); this.evento_.set(r?.evento ?? 'llego'); this.desde.set(r?.desde ?? ''); this.hasta.set(r?.hasta ?? '');
    this.mascara.set(r?.dias ?? 127); this.tiempo.set(r?.tiempo ?? ''); this.soloEnCasa.set(r?.soloEnCasa ?? false);
    this.escenaCasa.set(r?.escenaCasaId ? String(r.escenaCasaId) : ''); this.escenaPc.set(r?.escenaPcId ? String(r.escenaPcId) : '');
    this.avisoTexto.set(r?.aviso ?? ''); this.espera.set(String(r?.esperaMinutos ?? 60));
    this.error.set(''); this.confirmarBorrado.set(false);
    this.dialogo().nativeElement.showModal();
  }

  protected cerrar(): void { this.dialogo().nativeElement.close(); }
  protected cerrado(): void { this.editando.set(null); }
  protected fuera(e: MouseEvent): void { if (e.target === this.dialogo().nativeElement) this.cerrar(); }
  protected valor(e: Event): string { return (e.target as HTMLInputElement).value; }
  protected tocaDia(i: number): boolean { return (this.mascara() & (1 << i)) !== 0; }
  protected nombreDia(i: number): string { return NOMBRE_DIAS[i]; }
  protected alternarDia(i: number): void { this.mascara.update((m) => m ^ (1 << i)); }

  protected async guardar(ev: Event): Promise<void> {
    ev.preventDefault();
    if (this.guardando()) return;
    const espera = Number(this.espera());
    if (!this.nombre().trim()) { this.error.set('Ponle un nombre.'); return; }
    if (this.evento_() === 'hora' && !this.desde()) { this.error.set('Pon la hora a la que debe saltar.'); return; }
    if (!this.mascara()) { this.error.set('Elige al menos un día.'); return; }
    if (!this.escenaCasa() && !this.escenaPc() && !this.avisoTexto().trim()) { this.error.set('Elige una escena o escribe un mensaje.'); return; }
    if (!Number.isInteger(espera) || espera < 1 || espera > 1440) { this.error.set('La espera va de 1 a 1440 minutos.'); return; }
    this.guardando.set(true); this.error.set('');
    const e = this.editando();
    const cuerpo = {
      nombre: this.nombre().trim(), evento: this.evento_(), desde: this.desde(), hasta: this.hasta(), dias: this.mascara(), tiempo: this.tiempo(),
      soloEnCasa: this.evento_() === 'hora' && this.soloEnCasa(), aviso: this.avisoTexto().trim(), esperaMinutos: espera,
      escenaCasaId: this.escenaCasa() ? Number(this.escenaCasa()) : null, escenaPcId: this.escenaPc() ? Number(this.escenaPc()) : null,
      quitarEscenaCasa: !this.escenaCasa(), quitarEscenaPc: !this.escenaPc(),
    };
    try {
      if (e) await firstValueFrom(this.http.put(`/api/contexto/${e.id}`, cuerpo));
      else await firstValueFrom(this.http.post('/api/contexto', cuerpo));
      this.cerrar();
      await this.cargar();
    } catch (x) { this.error.set(this.mensaje(x)); } finally { this.guardando.set(false); }
  }

  protected async borrar(): Promise<void> {
    const e = this.editando(); if (!e) return;
    if (!this.confirmarBorrado()) { this.confirmarBorrado.set(true); return; }
    try { await firstValueFrom(this.http.delete(`/api/contexto/${e.id}`)); this.cerrar(); await this.cargar(); } catch (x) { this.error.set(this.mensaje(x)); }
  }

  private mensaje(x: unknown): string {
    const e = x as HttpErrorResponse;
    const errores = e.error?.errors as Record<string, string[]> | undefined;
    return (errores ? Object.values(errores).flat()[0] : null) ?? 'No se ha podido guardar.';
  }
}
