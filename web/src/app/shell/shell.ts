import { HttpClient } from '@angular/common/http';
import { Component, DestroyRef, computed, effect, inject, signal, untracked, viewChild } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map } from 'rxjs';
import { activarInclinacion } from '../core/inclinar';
import { Instalar } from '../core/instalar';
import { GRUPOS, INICIO, MODULOS } from '../core/modulos';
import { SesionService } from '../core/sesion.service';
import { TemaService } from '../core/tema.service';
import { SistemaService } from '../inicio/sistema.service';
import { PerfilService } from '../core/perfil.service';
import { CieloConfig } from '../core/cielo-config';
import { AsistenteService } from '../core/asistente.service';
import { RecargaService } from '../core/recarga';
import { Ajustes } from './ajustes';
import { Asistente } from './asistente';
import { Bloqueo } from './bloqueo';
import { Confirmar } from './confirmar';
import { Fondo } from './fondo';
import { Pin } from './pin';
import { Buscador } from './buscador';
import { MiniMusica } from './mini-musica';

const leer = (k: string): string | null => { try { return localStorage.getItem(k); } catch { return null; } };
const guardar = (k: string, v: string): void => { try { localStorage.setItem(k, v); } catch { /* sin almacenamiento */ } };
const leerLista = (k: string): string[] => { try { const v = JSON.parse(leer(k) ?? '[]'); return Array.isArray(v) ? v.filter((x) => typeof x === 'string') : []; } catch { return []; } };

const leerUso = (): Record<string, number> => { try { const v = JSON.parse(leer('mda-uso') ?? '{}'); return v && typeof v === 'object' ? v : {}; } catch { return {}; } };
// de partida, las de siempre: así la barra no empieza vacía de criterio
const PESO_INICIAL: Record<string, number> = { inicio: 4, gastos: 3, agenda: 2, notas: 1 };

// Marco de la zona privada: menú lateral (secciones, ajustes y usuario), barra superior y la pantalla activa.
@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, Buscador, MiniMusica, Asistente, Bloqueo, Pin, Confirmar, Fondo, Ajustes],
  templateUrl: './shell.html',
  styleUrl: './shell.css',
  host: {
    '(document:keydown.escape)': 'menuAbierto.set(false)',
    '(document:keydown)': 'tecla($event)',
    '(document:touchstart)': 'bordeGesto($event)',
  },
})
export class Shell {
  private readonly sesion = inject(SesionService);
  protected readonly entorno = this.sesion.entorno;
  private readonly router = inject(Router);
  protected readonly tema = inject(TemaService);
  protected readonly sistema = inject(SistemaService);
  protected readonly instalar = inject(Instalar);
  protected readonly perfil = inject(PerfilService);
  protected readonly asistente = inject(AsistenteService);
  protected readonly recarga = inject(RecargaService);

  protected readonly inicio = INICIO;
  protected readonly listos = MODULOS.filter((m) => m.listo);
  protected readonly grupos = GRUPOS;
  protected readonly usuario = this.sesion.usuario;
  protected readonly menuAbierto = signal(false);
  protected readonly ahora = signal(new Date());
  protected readonly hora = computed(() => this.ahora().toLocaleTimeString('es-ES', { hour: '2-digit', minute: '2-digit', second: '2-digit' }));
  private readonly ajustes = viewChild.required(Ajustes);
  protected readonly buscador = viewChild.required(Buscador);

  // título de la barra superior según la sección abierta
  private readonly url = toSignal(this.router.events.pipe(filter((e) => e instanceof NavigationEnd), map(() => this.router.url)), { initialValue: this.router.url });
  protected readonly seccion = computed(() => {
    // /gastos/fijos -> gastos; /pronto/fotos -> fotos
    const partes = this.url().split(/[?#]/)[0].split('/').filter(Boolean);
    const id = partes[0] === 'pronto' ? partes[1] : partes[0] ?? 'inicio';
    if (id === 'perfil') return { ...INICIO, id: 'perfil', titulo: 'Perfil', resumen: 'Tus datos, tu foto y tu cuenta.' };
    return [INICIO, ...MODULOS].find((m) => m.id === id) ?? INICIO;
  });

  // ---- barra de abajo del móvil: la sección actual en el centro y a los lados las que más usas ----
  private readonly uso = signal<Record<string, number>>(leerUso());
  protected readonly centro = computed(() => this.candidatas.find((m) => m.id === this.seccion().id) ?? INICIO);
  protected readonly laterales = computed(() => {
    const uso = this.uso();
    return this.candidatas.filter((m) => m.id !== this.centro().id)
      .sort((a, b) => ((uso[b.id] ?? 0) + (PESO_INICIAL[b.id] ?? 0)) - ((uso[a.id] ?? 0) + (PESO_INICIAL[a.id] ?? 0)))
      .slice(0, 3);
  });
  private get candidatas() { return [INICIO, ...this.listos]; }

  // ---- submenús y modo compacto del lateral (se recuerdan en este navegador) ----
  protected readonly compacto = signal(leer('mda-menu-compacto') === '1');
  private readonly abiertos = signal<string[]>(leerLista('mda-menu-grupos'));
  protected readonly grupoActivo = computed(() => GRUPOS.find((g) => g.modulos.some((m) => m.id === this.seccion().id))?.id ?? null);
  protected abierto(id: string): boolean { return this.abiertos().includes(id); }

  protected alternar(id: string): void {
    this.abiertos.update((l) => (l.includes(id) ? l.filter((x) => x !== id) : [...l, id]));
    guardar('mda-menu-grupos', JSON.stringify(this.abiertos()));
  }

  protected alternarCompacto(): void {
    this.compacto.update((c) => !c);
    guardar('mda-menu-compacto', this.compacto() ? '1' : '0');
  }

  // «[» reduce o amplía el menú (salvo mientras se escribe)
  protected tecla(e: KeyboardEvent): void {
    if (e.key !== '[' || e.ctrlKey || e.metaKey || e.altKey) return;
    const t = e.target as HTMLElement | null;
    if (t && (t.isContentEditable || /^(input|textarea|select)$/i.test(t.tagName))) return;
    this.alternarCompacto();
  }

  constructor() {
    this.sistema.empezar();
    this.perfil.cargar();
    // la pantalla de entrar no puede preguntar nada antes de que haya sesión: se lleva ya el sitio del tiempo para dibujar el cielo
    void inject(CieloConfig).sincronizar();
    const reloj = setInterval(() => this.ahora.set(new Date()), 1000);
    const sinInclinar = activarInclinacion();
    // latido: la pestaña abierta avisa cada minuto de que sigue ahí (Seguridad lo enseña como «en línea»)
    const http = inject(HttpClient);
    const app = matchMedia('(display-mode: standalone)').matches || (navigator as Navigator & { standalone?: boolean }).standalone === true;
    const latir = () => { if (!document.hidden) http.post('/api/seguridad/latido', { app }).subscribe({ error: () => {} }); };
    latir();
    const latido = setInterval(latir, 60_000);
    document.addEventListener('visibilitychange', latir);
    // el gesto que empieza en el borde se sigue por todo el documento
    const mover = (e: TouchEvent) => this.sigueGesto(e);
    const acabar = () => {
      if (this.x0 != null && !this.menuAbierto() && this.dx > 60) this.menuAbierto.set(true);
      if (!this.menuAbierto()) this.x0 = null;
    };
    document.addEventListener('touchmove', mover, { passive: true });
    document.addEventListener('touchend', acabar);
    inject(DestroyRef).onDestroy(() => {
      clearInterval(reloj);
      clearInterval(latido);
      document.removeEventListener('visibilitychange', latir);
      sinInclinar();
      document.removeEventListener('touchmove', mover);
      document.removeEventListener('touchend', acabar);
    });
    // cada visita a una sección suma al uso (la barra de abajo enseña las 3 más usadas)
    effect(() => {
      const id = this.seccion().id;
      untracked(() => {
        if (!this.candidatas.some((m) => m.id === id)) return;
        this.uso.update((u) => ({ ...u, [id]: (u[id] ?? 0) + 1 }));
        guardar('mda-uso', JSON.stringify(this.uso()));
      });
    });
    // el fondo «solo del menú» o «solo de las páginas» necesita saber cuánto mide el menú ahora
    effect(() => document.documentElement.style.setProperty('--ancho-lateral-html', this.compacto() ? '112px' : '312px'));
    // la sección abierta despliega su grupo (sin cerrar los demás)
    effect(() => {
      const g = this.grupoActivo();
      if (g) untracked(() => { if (!this.abierto(g)) this.alternar(g); });
    });
    // cerrar el menú del móvil al navegar
    this.router.events.pipe(filter((e) => e instanceof NavigationEnd)).subscribe(() => this.menuAbierto.set(false));
    // con el menú abierto, la página de detrás no se desplaza
    effect(() => document.documentElement.classList.toggle('menu-movil-abierto', this.menuAbierto()));
  }

  // ---- gestos del menú en el móvil: deslizar desde el borde izquierdo lo abre; hacia la izquierda, lo cierra ----
  private x0: number | null = null;
  private dx = 0;

  protected bordeGesto(e: TouchEvent): void {
    const t = e.touches[0];
    if (!this.menuAbierto() && t.clientX < 24 && innerWidth <= 960) { this.x0 = t.clientX; this.dx = 0; }
  }

  protected empiezaGesto(e: TouchEvent): void { this.x0 = e.touches[0].clientX; this.dx = 0; }

  protected sigueGesto(e: TouchEvent): void {
    if (this.x0 == null) return;
    this.dx = e.touches[0].clientX - this.x0;
  }

  protected acabaGesto(): void {
    if (this.x0 == null) return;
    if (this.menuAbierto() && this.dx < -60) this.menuAbierto.set(false);
    this.x0 = null;
  }

  // Ajustes en un modal (ajustes.ts)
  protected abrirAjustes(): void {
    this.menuAbierto.set(false);
    this.ajustes().abrir();
  }

  // Android y PC: el diálogo del navegador; iPhone: los pasos, dentro de Ajustes
  protected async instalarApp(): Promise<void> {
    if (this.instalar.puede()) { await this.instalar.instalar(); return; }
    this.instalar.pasosIos.set(true);
    this.abrirAjustes();
  }


  protected async salir(): Promise<void> {
    await this.sistema.parar();
    await this.sesion.salir();
    this.router.navigateByUrl('/entrar');
  }
}
