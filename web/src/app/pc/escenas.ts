import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ConfirmarService } from '../core/confirmar.service';

interface Escena { id: number; nombre: string; icono: string; energia: string; brillo: number | null; volumen: number | null; concentracion: boolean | null; despierto: boolean | null; apagarMinutos: number | null; resumen: string; apagaElPc: boolean; }
interface Borrador { nombre: string; icono: string; energia: string; tocarBrillo: boolean; brillo: number; tocarVolumen: boolean; volumen: number; concentracion: string; despierto: string; apagar: string; }

const VACIO: Borrador = { nombre: '', icono: '✨', energia: '', tocarBrillo: false, brillo: 60, tocarVolumen: false, volumen: 30, concentracion: '', despierto: '', apagar: '' };
const tri = (v: string): boolean | null => (v === 'si' ? true : v === 'no' ? false : null);
const triTexto = (v: boolean | null): string => (v === true ? 'si' : v === false ? 'no' : '');

// Escenas: ajustes del PC que se aplican de un toque («Cine», «Trabajo», «Dormir»…). Cada ajuste que no eliges se deja como está.
// Las que programan el apagado piden Face ID. También se pueden lanzar desde el iPhone con un atajo (permiso «casa»).
@Component({
  selector: 'app-escenas',
  styleUrls: ['../gastos/comun.css', './modulos.css'],
  template: `
    <section class="caja">
      <header class="caja__cab"><h3>Escenas</h3><span class="sub">varios ajustes del PC de un toque</span></header>
      <ul class="escenas">
        @for (e of escenas(); track e.id) {
          <li class="escena">
            <button class="escena__aplicar" type="button" (click)="aplicar(e)" [disabled]="ocupado() === e.id" [attr.aria-label]="'Aplicar la escena ' + e.nombre">
              <span class="escena__icono" aria-hidden="true">{{ e.icono }}</span>
              <span class="escena__texto"><strong>{{ e.nombre }}</strong><small>{{ e.resumen }}</small></span>
            </button>
            <span class="escena__acciones">
              <button class="enlace" type="button" (click)="editar(e)">Editar</button>
              @if (borrando() === e.id) { <button class="enlace enlace--peligro" type="button" (click)="quitar(e)">¿Seguro?</button> }
              @else { <button class="enlace" type="button" (click)="borrando.set(e.id)" [attr.aria-label]="'Quitar ' + e.nombre">Quitar</button> }
            </span>
          </li>
        } @empty { <li class="nada">No hay escenas: crea una.</li> }
      </ul>
      @if (aviso()) { <p class="msg" role="status">{{ aviso() }}</p> }

      @if (!formulario()) { <button class="boton" type="button" (click)="nueva()">+ Nueva escena</button> }
      @else {
        <form class="alta" (submit)="guardar($event)" novalidate>
          <div class="fila2">
            <label class="campo"><span class="campo__etiqueta">Nombre</span><input class="campo__input" maxlength="40" [value]="b().nombre" (input)="poner('nombre', $any($event.target).value)" placeholder="Cine"></label>
            <label class="campo campo--corto"><span class="campo__etiqueta">Icono</span><input class="campo__input" maxlength="8" [value]="b().icono" (input)="poner('icono', $any($event.target).value)"></label>
          </div>
          <label class="campo"><span class="campo__etiqueta">Plan de energía</span>
            <select class="campo__input" [value]="b().energia" (change)="poner('energia', $any($event.target).value)">
              <option value="">No tocar</option><option value="equilibrado">Equilibrado</option><option value="rendimiento">Rendimiento</option><option value="ahorro">Ahorro</option><option value="cine">Cine (no apaga la pantalla)</option>
            </select></label>
          <label class="interruptor"><input type="checkbox" [checked]="b().tocarBrillo" (change)="poner('tocarBrillo', $any($event.target).checked)"><span><strong>Brillo de la pantalla: {{ b().brillo }} %</strong></span></label>
          @if (b().tocarBrillo) { <input type="range" min="0" max="100" step="5" aria-label="Brillo" [value]="b().brillo" (input)="poner('brillo', +$any($event.target).value)"> }
          <label class="interruptor"><input type="checkbox" [checked]="b().tocarVolumen" (change)="poner('tocarVolumen', $any($event.target).checked)"><span><strong>Volumen: {{ b().volumen }} %</strong></span></label>
          @if (b().tocarVolumen) { <input type="range" min="0" max="100" step="5" aria-label="Volumen" [value]="b().volumen" (input)="poner('volumen', +$any($event.target).value)"> }
          <div class="fila2">
            <label class="campo"><span class="campo__etiqueta">Modo concentración</span>
              <select class="campo__input" [value]="b().concentracion" (change)="poner('concentracion', $any($event.target).value)"><option value="">No tocar</option><option value="si">Activar</option><option value="no">Desactivar</option></select></label>
            <label class="campo"><span class="campo__etiqueta">PC despierto</span>
              <select class="campo__input" [value]="b().despierto" (change)="poner('despierto', $any($event.target).value)"><option value="">No tocar</option><option value="si">Mantener despierto</option><option value="no">Que pueda suspenderse</option></select></label>
          </div>
          <label class="campo"><span class="campo__etiqueta">Apagar el PC en … minutos (vacío = no apagar; pide Face ID)</span>
            <input class="campo__input" inputmode="numeric" maxlength="3" [value]="b().apagar" (input)="poner('apagar', $any($event.target).value.replace(/\\D/g, ''))" placeholder="60"></label>
          @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
          <div class="acciones"><button class="enlace" type="button" (click)="formulario.set(false)">Cancelar</button><button class="button" type="submit" [disabled]="guardando()">{{ guardando() ? 'Guardando…' : 'Guardar' }}</button></div>
        </form>
      }
    </section>
  `,
  styles: `
    .escenas { list-style: none; margin: 0 0 12px; padding: 0; display: grid; gap: 8px; }
    .escena { display: grid; grid-template-columns: minmax(0, 1fr) auto; align-items: center; gap: 6px; padding: 6px 10px 6px 6px; border-radius: 16px; border: 1px solid var(--vidrio-borde); background: color-mix(in srgb, var(--texto) 4%, transparent); }
    .escena__aplicar { display: flex; align-items: center; gap: 12px; min-width: 0; min-height: 52px; padding: 4px 8px; border: 0; border-radius: 12px; background: none; color: inherit; text-align: left; cursor: pointer; transition: background 160ms; }
    .escena__aplicar:hover:not(:disabled), .escena__aplicar:focus-visible { background: color-mix(in srgb, var(--brand) 12%, transparent); }
    .escena__aplicar:disabled { opacity: .6; }
    .escena__icono { display: grid; place-items: center; flex: none; width: 42px; height: 42px; border-radius: 14px; background: color-mix(in srgb, var(--brand) 16%, transparent); font-size: 1.35rem; }
    .escena__texto { display: grid; min-width: 0; }
    .escena__texto small { color: var(--soft); font-size: .78rem; }
    .escena__acciones { display: flex; gap: 2px; }
    input[type='range'] { width: 100%; accent-color: var(--brand); min-height: 32px; }
    @media (max-width: 520px) { .escena { grid-template-columns: minmax(0, 1fr); } .escena__acciones { justify-content: flex-end; } }
  `,
})
export class Escenas implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly confirmador = inject(ConfirmarService);
  protected readonly escenas = signal<Escena[]>([]);
  protected readonly formulario = signal(false);
  protected readonly editandoId = signal<number | null>(null);
  protected readonly b = signal<Borrador>({ ...VACIO });
  protected readonly aviso = signal('');
  protected readonly error = signal('');
  protected readonly ocupado = signal<number | null>(null);
  protected readonly guardando = signal(false);
  protected readonly borrando = signal<number | null>(null);

  ngOnInit(): void { void this.leer(); }

  private async leer(): Promise<void> {
    try { this.escenas.set(await firstValueFrom(this.http.get<Escena[]>('/api/escenas'))); } catch { /* se queda como estaba */ }
  }

  protected poner<K extends keyof Borrador>(campo: K, valor: Borrador[K]): void { this.b.update((x) => ({ ...x, [campo]: valor })); }

  protected nueva(): void { this.editandoId.set(null); this.b.set({ ...VACIO }); this.error.set(''); this.formulario.set(true); }

  protected editar(e: Escena): void {
    this.editandoId.set(e.id); this.error.set(''); this.borrando.set(null);
    this.b.set({
      nombre: e.nombre, icono: e.icono, energia: e.energia, tocarBrillo: e.brillo !== null, brillo: e.brillo ?? 60, tocarVolumen: e.volumen !== null, volumen: e.volumen ?? 30,
      concentracion: triTexto(e.concentracion), despierto: triTexto(e.despierto), apagar: e.apagarMinutos?.toString() ?? '',
    });
    this.formulario.set(true);
  }

  protected async guardar(ev: Event): Promise<void> {
    ev.preventDefault();
    const x = this.b();
    const cuerpo = {
      nombre: x.nombre, icono: x.icono, energia: x.energia, brillo: x.tocarBrillo ? x.brillo : null, volumen: x.tocarVolumen ? x.volumen : null,
      concentracion: tri(x.concentracion), despierto: tri(x.despierto), apagarMinutos: x.apagar ? Number(x.apagar) : null,
    };
    this.guardando.set(true); this.error.set('');
    try {
      const id = this.editandoId();
      if (id === null) await firstValueFrom(this.http.post('/api/escenas', cuerpo)); else await firstValueFrom(this.http.put(`/api/escenas/${id}`, cuerpo));
      this.formulario.set(false);
      await this.leer();
    } catch (e) {
      const d = (e as HttpErrorResponse).error as { errors?: Record<string, string[]> } | undefined;
      this.error.set(Object.values(d?.errors ?? {})[0]?.[0] ?? 'No se pudo guardar.');
    } finally { this.guardando.set(false); }
  }

  protected async quitar(e: Escena): Promise<void> {
    this.borrando.set(null);
    try { await firstValueFrom(this.http.delete(`/api/escenas/${e.id}`)); await this.leer(); } catch { this.aviso.set('No se pudo quitar.'); }
  }

  protected async aplicar(e: Escena): Promise<void> {
    this.aviso.set(''); this.ocupado.set(e.id);
    try {
      for (let intento = 0; intento < 2; intento++) {
        try {
          const r = await firstValueFrom(this.http.post<{ mensaje: string }>(`/api/escenas/${e.id}/aplicar`, null));
          this.aviso.set(r.mensaje);
          return;
        } catch (err) {
          const x = err as HttpErrorResponse;
          if (x.status === 403 && x.error?.reautenticar && intento === 0) { if (!(await this.confirmador.pedir(`Confirma que eres tú: «${e.nombre}» apaga el PC.`))) return; continue; }
          this.aviso.set(x.error?.detalle ?? 'No se pudo aplicar la escena.');
          return;
        }
      }
    } finally { this.ocupado.set(null); }
  }
}
