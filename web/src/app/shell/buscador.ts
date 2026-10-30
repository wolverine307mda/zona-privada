import { HttpClient } from '@angular/common/http';
import { Component, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { INICIO, MODULOS } from '../core/modulos';
import { TemaService } from '../core/tema.service';
import { GastosService } from '../gastos/gastos.service';

interface Opcion { id: string; titulo: string; detalle: string; icono: string; grupo: string; hacer: () => void; }

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
          <input #campo type="search" placeholder="Busca una sección, una nota o una acción…" [value]="q()" (input)="escribir($any($event.target).value)"
                 (keydown)="mover($event)" autocomplete="off" role="combobox" aria-controls="buscador-lista" [attr.aria-activedescendant]="'op-' + activa()">
          <kbd>Esc</kbd>
        </label>
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
        <p class="buscador__pie"><kbd>↑</kbd><kbd>↓</kbd> moverse · <kbd>↵</kbd> abrir</p>
      </div>
    </dialog>
  `,
  styles: `
    .sr { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); }
    .buscador { width: min(620px, calc(100vw - 24px)); margin: 12vh auto auto; padding: 0; border: 1px solid var(--vidrio-borde); border-radius: 22px; color: var(--texto);
      background: var(--brillo), color-mix(in srgb, var(--tarjeta) 88%, transparent); backdrop-filter: blur(30px) saturate(1.6); -webkit-backdrop-filter: blur(30px) saturate(1.6); box-shadow: 0 40px 100px rgba(0, 0, 0, 0.5); }
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
  private readonly dlg = viewChild.required<ElementRef<HTMLDialogElement>>('dlg');
  private readonly campo = viewChild.required<ElementRef<HTMLInputElement>>('campo');

  protected readonly q = signal('');
  protected readonly activa = signal(0);
  private readonly notas = signal<Opcion[]>([]);
  private temporizador?: ReturnType<typeof setTimeout>;

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
    const q = quitarTildes(this.q().trim());
    const fijas = q ? this.fijas.filter((o) => quitarTildes(`${o.titulo} ${o.detalle}`).includes(q)) : this.fijas.filter((o) => o.grupo !== 'Tema');
    return [...fijas, ...this.notas()];
  });

  abrir(): void {
    const d = this.dlg().nativeElement;
    if (d.open) return;
    this.activa.set(0);
    this.notas.set([]);
    d.showModal();
    setTimeout(() => this.campo().nativeElement.focus(), 30);
  }

  protected escribir(t: string): void {
    this.q.set(t);
    this.activa.set(0);
    clearTimeout(this.temporizador);
    if (t.trim().length < 2) { this.notas.set([]); return; }
    this.temporizador = setTimeout(async () => {
      try {
        const l = await firstValueFrom(this.http.get<{ id: number; titulo: string; contenido: string }[]>('/api/notas', { params: { q: t.trim() } }));
        this.notas.set(l.slice(0, 6).map((n) => ({
          id: `nota-${n.id}`, titulo: n.titulo || n.contenido.split('\n')[0].replace(/^[-*#\s[\]x]+/, '').slice(0, 60) || 'Nota', detalle: n.contenido.replace(/\s+/g, ' ').slice(0, 80),
          icono: 'M6 3h9l5 5v13H6zM14 3v6h6M9 13h8M9 17h5', grupo: 'Notas', hacer: () => this.ir('/notas'),
        })));
      } catch { /* sin notas */ }
    }, 200);
  }

  protected mover(e: KeyboardEvent): void {
    const n = this.opciones().length;
    if (e.key === 'ArrowDown') { e.preventDefault(); this.activa.update((i) => (i + 1) % Math.max(n, 1)); this.verActiva(); }
    else if (e.key === 'ArrowUp') { e.preventDefault(); this.activa.update((i) => (i - 1 + n) % Math.max(n, 1)); this.verActiva(); }
    else if (e.key === 'Enter') { e.preventDefault(); const o = this.opciones()[this.activa()]; if (o) this.elegir(o); }
  }

  private verActiva(): void { setTimeout(() => document.getElementById(`op-${this.activa()}`)?.scrollIntoView({ block: 'nearest' })); }

  protected elegir(o: Opcion): void { this.dlg().nativeElement.close(); o.hacer(); }
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
