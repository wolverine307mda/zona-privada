import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';

interface Cancion { id: string; nombre: string; artistas: string; album: string | null; imagen: string | null; url: string | null; duracion: number; }
interface Artista { id: string; nombre: string; imagen: string | null; url: string | null; generos: string[]; popularidad: number; }
interface Reproduccion { sonando: boolean; cancion: Cancion | null; progreso: number; dispositivo: string | null; tipoDispositivo: string | null; volumen: number | null; aleatorio: boolean; repetir: string; }
export interface Conexion { id: string; nombre: string; configurado: boolean; conectado: boolean; cuenta: string | null; desde: string | null; vuelta: string; }

const PERIODOS = [{ id: 'short_term', nombre: '4 semanas' }, { id: 'medium_term', nombre: '6 meses' }, { id: 'long_term', nombre: 'Siempre' }];
const MENSAJES: Record<string, string> = {
  ok: '¡Spotify enlazado!', cancelada: 'Has cancelado el enlace con Spotify.', caducada: 'El enlace caducó: vuelve a intentarlo.',
  error: 'Spotify no dejó completar el enlace. Revisa el ID, el secreto y la dirección de vuelta.', 'sin-configurar': 'Falta configurar Spotify en el servidor.',
};

// Música: lo que suena ahora (con la portada de fondo y los controles), lo más escuchado y lo último.
// Todo pasa por la API, que guarda la cuenta de Spotify: el navegador nunca ve los tokens.
@Component({
  selector: 'app-musica',
  imports: [RouterLink],
  templateUrl: './musica.html',
  styleUrls: ['../gastos/comun.css', '../gastos/dialogos.css', './musica.css'],
})
export class Musica {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  protected readonly conexion = signal<Conexion | null>(null);
  protected readonly ahora = signal<Reproduccion | null>(null);
  protected readonly progreso = signal(0);
  protected readonly topCanciones = signal<Cancion[]>([]);
  protected readonly topArtistas = signal<Artista[]>([]);
  protected readonly recientes = signal<{ cancion: Cancion; cuando: string }[]>([]);
  protected readonly tipoTop = signal<'canciones' | 'artistas'>('canciones');
  protected readonly periodo = signal('short_term');
  protected readonly periodos = PERIODOS;
  protected readonly aviso = signal('');
  protected readonly copiado = signal(false);

  protected readonly pct = computed(() => { const c = this.ahora()?.cancion; return c?.duracion ? Math.min(100, (this.progreso() / c.duracion) * 100) : 0; });
  // la portada que tiñe la página: la que suena o, si no suena nada, la última escuchada
  protected readonly portada = computed(() => this.ahora()?.cancion?.imagen ?? this.recientes()[0]?.cancion.imagen ?? null);

  constructor() {
    const q = inject(ActivatedRoute).snapshot.queryParamMap.get('conexion');
    if (q) { this.aviso.set(MENSAJES[q] ?? ''); this.router.navigate([], { queryParams: {}, replaceUrl: true }); }
    this.empezar();
    // lo que suena se pregunta cada 5 s con la pestaña a la vista; el progreso avanza solo cada segundo
    const t = setInterval(() => { if (!document.hidden && this.conexion()?.conectado) this.leerAhora(); }, 5000);
    const s = setInterval(() => { if (this.ahora()?.sonando) this.progreso.update((p) => p + 1000); }, 1000);
    inject(DestroyRef).onDestroy(() => { clearInterval(t); clearInterval(s); });
  }

  private async empezar(): Promise<void> {
    try {
      const l = await firstValueFrom(this.http.get<Conexion[]>('/api/conexiones'));
      this.conexion.set(l.find((c) => c.id === 'spotify') ?? null);
    } catch { return; }
    if (!this.conexion()?.conectado) return;
    this.leerAhora();
    this.leerTop();
    firstValueFrom(this.http.get<{ cancion: Cancion; cuando: string }[]>('/api/musica/recientes')).then((r) => this.recientes.set(r)).catch(() => {});
  }

  private async leerAhora(): Promise<void> {
    try {
      const r = await firstValueFrom(this.http.get<Reproduccion>('/api/musica/ahora'));
      this.ahora.set(r);
      this.progreso.set(r.progreso);
    } catch { /* se reintenta en el siguiente ciclo */ }
  }

  protected async leerTop(): Promise<void> {
    const params = { tipo: this.tipoTop(), periodo: this.periodo() };
    try {
      if (this.tipoTop() === 'artistas') this.topArtistas.set(await firstValueFrom(this.http.get<Artista[]>('/api/musica/top', { params })));
      else this.topCanciones.set(await firstValueFrom(this.http.get<Cancion[]>('/api/musica/top', { params })));
    } catch { /* sin datos */ }
  }

  protected verTop(t: 'canciones' | 'artistas'): void { this.tipoTop.set(t); this.leerTop(); }
  protected verPeriodo(p: string): void { this.periodo.set(p); this.leerTop(); }

  protected async control(accion: 'play' | 'pause' | 'next' | 'previous'): Promise<void> {
    if (accion === 'play' || accion === 'pause') this.ahora.update((a) => (a ? { ...a, sonando: accion === 'play' } : a));
    try {
      await firstValueFrom(this.http.post(`/api/musica/control/${accion}`, null));
      setTimeout(() => this.leerAhora(), 450);
    } catch (e) {
      this.aviso.set(e instanceof HttpErrorResponse ? e.error?.detail ?? 'Spotify no ha respondido.' : 'Sin conexión.');
      this.leerAhora();
    }
  }

  protected conectar(): void { location.href = '/api/conexiones/spotify/conectar'; }

  protected async copiar(t: string): Promise<void> {
    try { await navigator.clipboard.writeText(t); this.copiado.set(true); setTimeout(() => this.copiado.set(false), 1600); } catch { /* sin portapapeles */ }
  }

  protected tiempo(ms: number): string { const s = Math.floor(ms / 1000); return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`; }
  protected hace(iso: string): string {
    const m = Math.round((Date.now() - new Date(iso).getTime()) / 60000);
    if (m < 1) return 'ahora';
    if (m < 60) return `hace ${m} min`;
    if (m < 1440) return `hace ${Math.round(m / 60)} h`;
    return new Date(iso).toLocaleDateString('es-ES', { day: 'numeric', month: 'short' });
  }
  protected icono(tipo: string | null): string {
    return tipo === 'Smartphone' ? 'M8 3h8v18H8zM11.5 18h1' : tipo === 'Speaker' ? 'M7 3h10v18H7zM12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6zM12 6h.01' : 'M4 5h16v11H4zM8 20h8M12 16v4';
  }
}
