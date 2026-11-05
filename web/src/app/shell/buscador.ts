import { HttpClient } from '@angular/common/http';
import { Component, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { INICIO, MODULOS } from '../core/modulos';
import { AsistenteService } from '../core/asistente.service';
import { TemaService } from '../core/tema.service';
import { AgendaService, EventoEntrada } from '../agenda/agenda.service';
import { ConfirmarService } from '../core/confirmar.service';
import { GastosService, MovimientoEntrada } from '../gastos/gastos.service';
import { euros } from '../gastos/modelo';
import { Accion, crearAcciones, esComando, filtrarAcciones, textoComando } from './acciones';

interface Opcion { id: string; titulo: string; detalle: string; icono: string; grupo: string; hacer: () => void | Promise<void>; mantener?: boolean; }
// lo que el asistente ha entendido de la frase escrita (para enseñarlo antes de guardar)
interface Vista { clase: string; resumen: string; movimiento: MovimientoEntrada | null; evento: EventoEntrada | null; dudas: string[] | null; respuesta: string | null; }
interface Hecho { texto: string; deshacer?: () => Promise<unknown>; }
interface ResultadoBuscar { agenda: { id: number; titulo: string; inicio: string; lugar: string | null; todoElDia: boolean; tipo: string }[]; movimientos: { id: number; concepto: string; importe: number; fecha: string; categoria: string; tipo: string }[]; }

const quitarTildes = (t: string) => t.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();

// Buscador rápido (Ctrl+K o «/»): ir a cualquier sección, crear algo en un toque o abrir una nota por su texto
@Component({
  selector: 'app-buscador',
  host: { '(document:keydown)': 'atajo($event)' },
  template: `
    <dialog class="buscador" #dlg (click)="fuera($event)" (close)="q.set('')" aria-label="Buscador">
      <div class="buscador__caja">
        <label class="buscador__campo">
          <svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M11 18a7 7 0 1 0 0-14 7 7 0 0 0 0 14zM20 20l-4-4" /></svg>
          <span class="sr">Buscar</span>
          <input #campo type="search" placeholder="Busca algo, apunta («12 café») o escribe > para acciones…" [value]="q()" (input)="escribir($any($event.target).value)"
                 (keydown)="mover($event)" autocomplete="off" role="combobox" aria-controls="buscador-lista" [attr.aria-activedescendant]="'op-' + activa()">
          <kbd>Esc</kbd>
        </label>
        @if (hecho(); as h) {
          <p class="buscador__hecho" role="status">{{ h.texto }}@if (h.deshacer) { <button type="button" (click)="deshacer()">Deshacer</button> }</p>
        }
        <ul class="buscador__lista" id="buscador-lista" role="listbox">
          @for (o of opciones(); track o.id; let i = $index) {
            @if (i === 0 || opciones()[i - 1].grupo !== o.grupo) { <li class="buscador__grupo" role="presentation">{{ o.grupo }}</li> }
            <li [id]="'op-' + i" role="option" [attr.aria-selected]="i === activa()" [class.activa]="i === activa()" (click)="elegir(o)" (mousemove)="activa.set(i)">
              <span class="buscador__icono"><svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path [attr.d]="o.icono" /></svg></span>
              <span class="buscador__texto"><strong>{{ o.titulo }}</strong>@if (o.detalle) {<span>{{ o.detalle }}</span>}</span>
              <svg class="icon buscador__ir" viewBox="0 0 24 24" aria-hidden="true"><path d="M5 12h14M13 6l6 6-6 6" /></svg>
            </li>
          } @empty { <li class="buscador__nada">Nada con «{{ q() }}»</li> }
        </ul>
        <p class="buscador__pie"><kbd>↑</kbd><kbd>↓</kbd> moverse · <kbd>↵</kbd> elegir · <kbd>&gt;</kbd> acciones</p>
      </div>
    </dialog>
  `,
  styles: `
    .sr { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); }
    .buscador { width: min(620px, calc(100vw - 24px)); margin: 12vh auto auto; padding: 0; border: 1px solid var(--vidrio-borde); border-radius: 22px; color: var(--texto);
      background: var(--brillo), var(--tarjeta); backdrop-filter: blur(30px) saturate(1.6); -webkit-backdrop-filter: blur(30px) saturate(1.6); box-shadow: 0 40px 100px rgba(0, 0, 0, 0.5); }
    .buscador[open] { animation: cae 340ms var(--ease-expo); }
    @keyframes cae { from { opacity: 0; transform: perspective(900px) rotateX(-14deg) translateY(-18px) scale(0.97); } }
    .buscador::backdrop { background: rgba(3, 8, 11, 0.45); backdrop-filter: blur(3px); }
    .buscador__caja { display: flex; flex-direction: column; }
    .buscador__campo { display: flex; align-items: center; gap: 12px; padding: 16px 18px; border-bottom: 1px solid var(--linea); }
    .buscador__campo .icon { color: var(--soft); }
    .buscador__campo input { flex: 1; min-width: 0; border: 0; background: none; color: var(--texto); font: 500 17px/1 var(--font-text); }
    .buscador__campo input:focus { outline: none; }
    kbd { display: inline-grid; place-items: center; min-width: 22px; height: 22px; padding: 0 6px; border-radius: 6px; border: 1px solid var(--linea); font: 600 11px/1 var(--font-code); color: var(--soft); background: var(--tarjeta-2); }
    .buscador__lista { max-height: min(56vh, 460px); overflow-y: auto; list-style: none; margin: 0; padding: 8px; }
    .buscador__grupo { padding: 10px 10px 6px; font: 600 11px/1 var(--font-text); text-transform: uppercase; letter-spacing: 0.08em; color: var(--soft); }
    [role='option'] { display: grid; grid-template-columns: 38px minmax(0, 1fr) auto; align-items: center; gap: 12px; padding: 8px 10px; border-radius: 12px; cursor: pointer; }
    [role='option'].activa { background: color-mix(in srgb, var(--brand) 14%, var(--tarjeta-2)); }
    .buscador__icono { display: grid; place-items: center; width: 38px; height: 38px; border-radius: 11px; background: var(--tarjeta-2); color: var(--acento); }
    .activa .buscador__icono { background: var(--brand); color: var(--on-brand); }
    .buscador__icono .icon { width: 18px; height: 18px; }
    .buscador__texto { display: flex; flex-direction: column; gap: 2px; min-width: 0; }
    .buscador__texto strong { font-weight: 500; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .buscador__texto span { font-size: 12px; color: var(--soft); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .buscador__ir { width: 16px; height: 16px; color: var(--soft); opacity: 0; transition: opacity 120ms ease, transform 200ms var(--ease-out); }
    .activa .buscador__ir { opacity: 1; transform: translateX(2px); }
    .buscador__hecho { display: flex; align-items: center; justify-content: space-between; gap: 12px; margin: 8px 8px 0; padding: 10px 14px; border-radius: 12px; font-size: 14px; background: color-mix(in srgb, var(--ok) 14%, var(--tarjeta-2)); }
    .buscador__hecho button { border: 0; background: none; color: var(--acento); font: 600 13px/1 var(--font-text); cursor: pointer; min-height: 32px; padding: 0 8px; }
    .buscador__nada { padding: 24px; text-align: center; color: var(--soft); }
    .buscador__pie { display: flex; gap: 6px; align-items: center; justify-content: flex-end; padding: 10px 16px; border-top: 1px solid var(--linea); font-size: 12px; color: var(--soft); }
    @media (max-width: 640px) {
      .buscador { margin: 0 auto auto; width: 100vw; max-width: 100vw; border-radius: 0 0 22px 22px; border-top: 0; }
      .buscador__campo { padding-top: max(16px, env(safe-area-inset-top)); }
      .buscador__campo kbd, .buscador__pie { display: none; }
    }
  `,
})
export class Buscador {
  private readonly router = inject(Router);
  private readonly http = inject(HttpClient);
  private readonly tema = inject(TemaService);
  private readonly gastos = inject(GastosService);
  private readonly asistente = inject(AsistenteService);
  private readonly confirmar = inject(ConfirmarService);
  private readonly agenda = inject(AgendaService);
  private readonly dlg = viewChild.required<ElementRef<HTMLDialogElement>>('dlg');
  private readonly campo = viewChild.required<ElementRef<HTMLInputElement>>('campo');

  protected readonly q = signal('');
  protected readonly activa = signal(0);
  private readonly notas = signal<Opcion[]>([]);
  private readonly contenido = signal<Opcion[]>([]);   // agenda y movimientos que coinciden
  protected readonly vista = signal<Vista | null>(null);
  protected readonly hecho = signal<Hecho | null>(null);
  private readonly escenas = signal<{ id: number; nombre: string }[]>([]);
  private temporizador?: ReturnType<typeof setTimeout>;
  private temporizadorVista?: ReturnType<typeof setTimeout>;
  private peticion = 0;

  private readonly acciones = computed<Accion[]>(() => crearAcciones({
    tema: this.tema,
    orden: (tipo, parametro) => this.orden(tipo, parametro),
    escenas: () => this.escenas(),
    aplicarEscena: async (id) => { const r = await firstValueFrom(this.http.post<{ mensaje: string }>(`/api/escenas/${id}/aplicar`, null)); return r.mensaje || 'Escena aplicada.'; },
    avisar: (t) => this.hecho.set({ texto: t }),
  }));

  private readonly fijas: Opcion[] = [
    ...[INICIO, ...MODULOS.filter((m) => m.listo)].map((m) => ({ id: `ir-${m.id}`, titulo: m.titulo, detalle: m.resumen, icono: m.icono, grupo: 'Ir a', hacer: () => this.ir(`/${m.id}`) })),
    ...[['movimientos', 'Movimientos'], ['fijos', 'Fijos y suscripciones'], ['gasolina', 'Gasolina'], ['deudas', 'Coche y deudas'], ['estadisticas', 'Estadísticas']].map(([id, t]) => ({
      id: `ir-g-${id}`, titulo: t, detalle: 'Gastos', icono: 'M3 6h18v12H3zM3 10h18M7 15h3', grupo: 'Ir a', hacer: () => this.ir(`/gastos/${id}`),
    })),
    { id: 'n-gasto', titulo: 'Apuntar un gasto', detalle: 'Gastos', icono: 'M12 5v14M5 12h14', grupo: 'Crear', hacer: () => { this.ir('/gastos'); this.gastos.editandoMovimiento.set({ tipo: 'Gasto' }); } },
    { id: 'n-ingreso', titulo: 'Apuntar un ingreso', detalle: 'Gastos', icono: 'M12 19V5M6 11l6-6 6 6', grupo: 'Crear', hacer: () => { this.ir('/gastos'); this.gastos.editandoMovimiento.set({ tipo: 'Ingreso' }); } },
    { id: 'n-repostaje', titulo: 'Apuntar un repostaje', detalle: 'Gasolina', icono: 'M4 21V5a2 2 0 0 1 2-2h6a2 2 0 0 1 2 2v16M3 21h12M4 10h10', grupo: 'Crear', hacer: () => { this.ir('/gastos/gasolina'); this.gastos.editandoMovimiento.set({ tipo: 'Gasto', categoria: 'gasolina' }); } },
    { id: 'n-nota', titulo: 'Nota nueva', detalle: 'Notas', icono: 'M6 3h9l5 5v13H6zM14 3v6h6M12 12v6M9 15h6', grupo: 'Crear', hacer: () => this.ir('/notas', { nueva: 1 }) },
    { id: 'n-evento', titulo: 'Evento nuevo', detalle: 'Agenda', icono: 'M4 5h16v15H4zM4 10h16M8 3v4M16 3v4', grupo: 'Crear', hacer: () => this.ir('/agenda', { nuevo: 'evento' }) },
    { id: 'n-rec', titulo: 'Recordatorio nuevo', detalle: 'Agenda · avisa por correo o WhatsApp', icono: 'M12 8v4l3 2M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18z', grupo: 'Crear', hacer: () => this.ir('/agenda', { nuevo: 'recordatorio' }) },
    { id: 't-dia', titulo: 'Modo día', detalle: 'Tema', icono: 'M12 4V2M12 22v-2M4 12H2M22 12h-2M12 17a5 5 0 1 0 0-10 5 5 0 0 0 0 10z', grupo: 'Tema', hacer: () => this.tema.ponerModo('light') },
    { id: 't-noche', titulo: 'Modo noche', detalle: 'Tema', icono: 'M20 14.5A8 8 0 0 1 9.5 4 8 8 0 1 0 20 14.5z', grupo: 'Tema', hacer: () => this.tema.ponerModo('dark') },
  ];

  protected readonly opciones = computed(() => {
    const entrada = this.q();
    // «> color rojo»: solo acciones
    if (esComando(entrada)) {
      return filtrarAcciones(this.acciones(), textoComando(entrada)).map((x): Opcion => ({ ...x, grupo: 'Acciones' }));
    }
    const q = quitarTildes(entrada.trim());
    const fijas = q ? this.fijas.filter((o) => quitarTildes(`${o.titulo} ${o.detalle}`).includes(q)) : this.fijas.filter((o) => o.grupo !== 'Tema');
    const acciones = q.length >= 2 ? filtrarAcciones(this.acciones(), entrada).slice(0, 5).map((x): Opcion => ({ ...x, grupo: 'Acciones' })) : [];
    // si parece una frase para apuntar («12 € en el Mercadona», «recuérdame…»), lo primero es dársela al asistente
    const frase = entrada.trim();
    const parece = /\d|^(recu[eé]rd|av[ií]s|cu[aá]nt|ayer|ma[ñn]ana|cobr|pagu|gast)/i.test(frase) && frase.length > 2;
    const asis: Opcion[] = parece ? [this.opcionAsistente(frase)] : [];
    return [...asis, ...fijas, ...acciones, ...this.contenido(), ...this.notas()];
  });

  // la opción de la frase: con la vista previa lista y sin dudas, Intro la guarda; si no, abre el asistente para retocarla
  private opcionAsistente(frase: string): Opcion {
    const v = this.vista();
    const icono = 'M12 3l1.8 4.9L19 9.7l-4.9 1.8L12 16.4l-1.8-4.9L5.3 9.7l4.9-1.8zM19 15l.8 2.2L22 18l-2.2.8L19 21l-.8-2.2L16 18l2.2-.8z';
    const listo = !!v && !v.dudas?.length && ((v.clase === 'movimiento' && !!v.movimiento) || (v.clase === 'agenda' && !!v.evento));
    if (v && listo) return { id: 'asistente', titulo: v.resumen, detalle: 'Intro para guardarlo · Mayús+Intro para retocarlo', icono, grupo: 'Asistente', mantener: true, hacer: () => this.guardarVista(v) };
    if (v?.clase === 'pregunta' && v.respuesta) return { id: 'asistente', titulo: v.respuesta.split('\n')[0], detalle: 'Intro para ver la respuesta entera', icono, grupo: 'Asistente', hacer: () => this.asistente.abrir(frase) };
    return { id: 'asistente', titulo: 'Decírselo al asistente', detalle: `«${frase}»`, icono, grupo: 'Asistente', hacer: () => this.asistente.abrir(frase) };
  }

  private async guardarVista(v: Vista): Promise<void> {
    try {
      if (v.clase === 'movimiento' && v.movimiento) {
        const creado = await this.gastos.guardarMovimiento(v.movimiento);
        this.gastos.recargar();
        this.hecho.set({ texto: `${v.movimiento.tipo === 'Ingreso' ? 'Ingreso' : 'Gasto'} apuntado: ${creado.concepto} · ${euros(creado.importe)}`, deshacer: async () => { await this.gastos.borrarMovimiento(creado.id); this.gastos.recargar(); } });
      } else if (v.clase === 'agenda' && v.evento) {
        const r = await this.agenda.guardar(v.evento);
        window.dispatchEvent(new Event('mda:agenda'));
        this.hecho.set({ texto: `${v.evento.tipo === 'Recordatorio' ? 'Te lo recuerdo' : 'En la agenda'}: ${v.evento.titulo}`, deshacer: async () => { await firstValueFrom(this.http.delete(`/api/agenda/${r.id}`)); window.dispatchEvent(new Event('mda:agenda')); } });
      }
      this.q.set(''); this.vista.set(null); this.contenido.set([]); this.notas.set([]);
    } catch { this.hecho.set({ texto: 'No se ha podido guardar. Ábrelo en el asistente para ver qué falta.' }); }
  }

  protected async deshacer(): Promise<void> {
    const h = this.hecho();
    if (!h?.deshacer) return;
    try { await h.deshacer(); this.hecho.set({ texto: 'Deshecho.' }); } catch { this.hecho.set({ texto: 'No se ha podido deshacer.' }); }
  }

  // una orden al PC; si pide confirmar que eres tú, se confirma y se repite
  private async orden(tipo: string, parametro = ''): Promise<string> {
    for (let intento = 0; ; intento++) {
      try {
        const r = await firstValueFrom(this.http.post<{ mensaje?: string; estado?: string }>('/api/pc/ordenes', { tipo, parametro }));
        return r?.mensaje ?? 'Orden enviada al PC.';
      } catch (e) {
        const x = e as { status?: number; error?: { reautenticar?: boolean; detail?: string } };
        if (x.status === 403 && x.error?.reautenticar && intento === 0) { if (!(await this.confirmar.pedir('Confirma que eres tú para mandar la orden al PC.'))) return 'Cancelado.'; continue; }
        return x.error?.detail ?? 'El PC no ha aceptado la orden (¿está el agente conectado?).';
      }
    }
  }

  abrir(): void {
    const d = this.dlg().nativeElement;
    if (d.open) return;
    this.activa.set(0);
    this.notas.set([]);
    this.contenido.set([]);
    this.vista.set(null);
    this.hecho.set(null);
    d.showModal();
    setTimeout(() => this.campo().nativeElement.focus(), 30);
    // las escenas del PC, para poder ponerlas con «> escena»
    firstValueFrom(this.http.get<{ id: number; nombre: string }[]>('/api/escenas')).then((l) => this.escenas.set(l)).catch(() => {});
  }

  protected escribir(t: string): void {
    this.q.set(t);
    this.activa.set(0);
    this.hecho.set(null);
    clearTimeout(this.temporizador);
    clearTimeout(this.temporizadorVista);
    const texto = t.trim();
    const miPeticion = ++this.peticion;   // una respuesta que llega tarde (ya escribiste otra cosa) se descarta
    this.vista.set(null);
    if (texto.length < 2 || esComando(t)) { this.notas.set([]); this.contenido.set([]); return; }

    this.temporizador = setTimeout(async () => {
      const [notas, contenido] = await Promise.all([
        firstValueFrom(this.http.get<{ id: number; titulo: string; contenido: string }[]>('/api/notas', { params: { q: texto } })).catch(() => []),
        firstValueFrom(this.http.get<ResultadoBuscar>('/api/buscar', { params: { q: texto } })).catch(() => null),
      ]);
      if (miPeticion !== this.peticion) return;
      this.notas.set(notas.slice(0, 6).map((n) => ({
        id: `nota-${n.id}`, titulo: n.titulo || n.contenido.split('\n')[0].replace(/^[-*#\s[\]x]+/, '').slice(0, 60) || 'Nota', detalle: n.contenido.replace(/\s+/g, ' ').slice(0, 80),
        icono: 'M6 3h9l5 5v13H6zM14 3v6h6M9 13h8M9 17h5', grupo: 'Notas', hacer: () => this.ir('/notas'),
      })));
      this.contenido.set([
        ...(contenido?.agenda ?? []).map((e): Opcion => ({
          id: `evento-${e.id}`, titulo: e.titulo, detalle: `${new Date(e.inicio).toLocaleDateString('es-ES', { weekday: 'short', day: 'numeric', month: 'short' })}${e.lugar ? ' · ' + e.lugar : ''}`,
          icono: 'M4 5h16v15H4zM4 10h16M8 3v4M16 3v4', grupo: 'Agenda', hacer: () => this.ir('/agenda'),
        })),
        ...(contenido?.movimientos ?? []).map((m): Opcion => ({
          id: `mov-${m.id}`, titulo: m.concepto, detalle: `${m.tipo === 'Ingreso' ? '+' : '−'}${euros(m.importe)} · ${new Date(m.fecha + 'T12:00:00').toLocaleDateString('es-ES', { day: 'numeric', month: 'short' })}`,
          icono: 'M3 6h18v12H3zM3 10h18M7 15h3', grupo: 'Movimientos', hacer: () => this.ir('/gastos/movimientos'),
        })),
      ]);
    }, 200);

    // vista previa de lo que entendería el asistente (solo con frases que parecen un apunte o una pregunta)
    if (/\d|^(recu[eé]rd|av[ií]s|cu[aá]nt|ayer|ma[ñn]ana|cobr|pagu|gast)/i.test(texto) && texto.length > 2 && texto.length <= 300) {
      this.temporizadorVista = setTimeout(async () => {
        try {
          const v = await firstValueFrom(this.http.post<Vista>('/api/asistente/entender', { texto }));
          if (miPeticion === this.peticion) this.vista.set(v);
        } catch { /* sin vista previa: Intro abre el asistente como siempre */ }
      }, 280);
    }
  }

  protected mover(e: KeyboardEvent): void {
    const n = this.opciones().length;
    if (e.key === 'ArrowDown') { e.preventDefault(); this.activa.update((i) => (i + 1) % Math.max(n, 1)); this.verActiva(); }
    else if (e.key === 'ArrowUp') { e.preventDefault(); this.activa.update((i) => (i - 1 + n) % Math.max(n, 1)); this.verActiva(); }
    else if (e.key === 'Enter') {
      e.preventDefault();
      const o = this.opciones()[this.activa()];
      if (!o) return;
      // Mayús+Intro sobre una frase: en vez de guardarla, se abre el asistente para retocarla
      if (e.shiftKey && o.id === 'asistente') { this.dlg().nativeElement.close(); this.asistente.abrir(this.q().trim()); return; }
      this.elegir(o);
    }
  }

  private verActiva(): void { setTimeout(() => document.getElementById(`op-${this.activa()}`)?.scrollIntoView({ block: 'nearest' })); }

  protected elegir(o: Opcion): void {
    if (!o.mantener) this.dlg().nativeElement.close();
    void o.hacer();
  }
  private ir(url: string, queryParams?: Record<string, string | number>): void { this.router.navigate([url], { queryParams }); }
  protected fuera(e: MouseEvent): void { if (e.target === this.dlg().nativeElement) this.dlg().nativeElement.close(); }

  protected atajo(e: KeyboardEvent): void {
    const t = e.target as HTMLElement | null;
    const escribiendo = !!t?.closest?.('input, textarea, select, [contenteditable]');
    if ((e.key === 'k' && (e.ctrlKey || e.metaKey)) || (e.key === '/' && !escribiendo)) {
      e.preventDefault();
      this.abrir();
    }
  }
}
