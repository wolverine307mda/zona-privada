import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export type TipoArchivo = 'carpeta' | 'imagen' | 'audio' | 'video' | 'pdf' | 'texto' | 'hoja' | 'presentacion' | 'comprimido' | 'codigo' | 'programa' | 'otro';

export interface Raiz { id: string; nombre: string; elementos: number; }
export interface Elemento { nombre: string; ruta: string; carpeta: boolean; tamano: number; modificado: string; tipo: TipoArchivo; }
export interface Lista { raiz: string; ruta: string; elementos: Elemento[]; }
export interface EnPapelera { raiz: string; id: string; nombre: string; origen: string; carpeta: boolean; tamano: number; borrado: string; }

export interface Subida { id: number; nombre: string; tamano: number; progreso: number; estado: 'subiendo' | 'hecho' | 'error'; }

// Habla con /api/archivos y lleva la cola de subidas (con progreso de cada archivo)
@Injectable({ providedIn: 'root' })
export class ArchivosService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/archivos';
  private siguiente = 1;

  readonly subidas = signal<Subida[]>([]);

  raices() { return firstValueFrom(this.http.get<Raiz[]>(`${this.base}/raices`)); }
  lista(raiz: string, ruta: string) { return firstValueFrom(this.http.get<Lista>(`${this.base}/lista`, { params: { raiz, ruta } })); }
  buscar(raiz: string, q: string) { return firstValueFrom(this.http.get<Elemento[]>(`${this.base}/buscar`, { params: { raiz, q } })); }
  papelera() { return firstValueFrom(this.http.get<EnPapelera[]>(`${this.base}/papelera`)); }

  url(raiz: string, ruta: string, ver = false): string {
    const p = new HttpParams({ fromObject: ver ? { raiz, ruta, ver: 'true' } : { raiz, ruta } });
    return `${this.base}/archivo?${p.toString()}`;
  }

  nuevaCarpeta(raiz: string, ruta: string, nombre: string) { return firstValueFrom(this.http.post<Elemento>(`${this.base}/carpeta`, { raiz, ruta, nombre })); }
  renombrar(raiz: string, ruta: string, nombre: string) { return firstValueFrom(this.http.post(`${this.base}/renombrar`, { raiz, ruta, nombre })); }
  mover(raiz: string, ruta: string, destino: string) { return firstValueFrom(this.http.post(`${this.base}/mover`, { raiz, ruta, destino })); }
  borrar(raiz: string, ruta: string) { return firstValueFrom(this.http.delete(this.base, { params: { raiz, ruta } })); }
  recuperar(raiz: string, id: string) { return firstValueFrom(this.http.post<{ ruta: string }>(`${this.base}/papelera/recuperar`, { raiz, id })); }
  borrarParaSiempre(raiz?: string, id?: string) {
    return firstValueFrom(this.http.delete(`${this.base}/papelera`, { params: raiz && id ? { raiz, id } : {} }));
  }

  // Sube los archivos de tres en tres; cada uno va en su propia petición para ver su progreso
  async subir(raiz: string, ruta: string, archivos: File[]): Promise<number> {
    const cola = archivos.map((f) => ({ f, s: { id: this.siguiente++, nombre: f.name, tamano: f.size, progreso: 0, estado: 'subiendo' as const } }));
    this.subidas.update((l) => [...l.filter((s) => s.estado === 'subiendo'), ...cola.map((c) => c.s)]);
    let bien = 0;
    const trabajar = async () => {
      for (let c = cola.shift(); c; c = cola.shift()) {
        if (await this.una(raiz, ruta, c.f, c.s.id)) bien++;
      }
    };
    await Promise.all([trabajar(), trabajar(), trabajar()]);
    return bien;
  }

  // XMLHttpRequest y no HttpClient: con fetch el navegador no informa del progreso de la subida
  private una(raiz: string, ruta: string, f: File, id: number): Promise<boolean> {
    const datos = new FormData();
    datos.append('archivos', f, f.name);
    const cambiar = (c: Partial<Subida>) => this.subidas.update((l) => l.map((s) => (s.id === id ? { ...s, ...c } : s)));
    return new Promise((fin) => {
      const xhr = new XMLHttpRequest();
      xhr.open('POST', `${this.base}/subir?${new HttpParams({ fromObject: { raiz, ruta } }).toString()}`);
      xhr.upload.onprogress = (e) => { if (e.lengthComputable) cambiar({ progreso: e.loaded / e.total }); };
      xhr.onload = () => {
        const bien = xhr.status >= 200 && xhr.status < 300;
        cambiar(bien ? { progreso: 1, estado: 'hecho' } : { estado: 'error' });
        fin(bien);
      };
      xhr.onerror = () => { cambiar({ estado: 'error' }); fin(false); };
      xhr.send(datos);
    });
  }

  limpiarSubidas(): void { this.subidas.update((l) => l.filter((s) => s.estado === 'subiendo')); }
}

// ---- formato ----
const fmtFecha = new Intl.DateTimeFormat('es-ES', { day: 'numeric', month: 'short', year: 'numeric' });
const fmtHora = new Intl.DateTimeFormat('es-ES', { hour: '2-digit', minute: '2-digit' });

export const tamano = (b: number): string => {
  if (b < 1024) return `${b} B`;
  const u = ['KB', 'MB', 'GB', 'TB'];
  let n = b / 1024, i = 0;
  while (n >= 1024 && i < u.length - 1) { n /= 1024; i++; }
  return `${n.toLocaleString('es-ES', { maximumFractionDigits: n < 10 ? 1 : 0 })} ${u[i]}`;
};

export const fecha = (iso: string): string => {
  const d = new Date(iso), hoy = new Date();
  if (d.toDateString() === hoy.toDateString()) return `hoy, ${fmtHora.format(d)}`;
  const ayer = new Date(hoy); ayer.setDate(hoy.getDate() - 1);
  if (d.toDateString() === ayer.toDateString()) return `ayer, ${fmtHora.format(d)}`;
  return fmtFecha.format(d);
};

// icono (trazos SVG en 24×24) y color de cada tipo y de cada carpeta raíz
export const TIPOS: Record<TipoArchivo, { nombre: string; color: string; icono: string }> = {
  carpeta: { nombre: 'Carpeta', color: '#f5b83d', icono: 'M3 6.5A1.5 1.5 0 0 1 4.5 5H9l2 2.5h8.5A1.5 1.5 0 0 1 21 9v9.5a1.5 1.5 0 0 1-1.5 1.5h-15A1.5 1.5 0 0 1 3 18.5z' },
  imagen: { nombre: 'Imagen', color: '#3ddc84', icono: 'M4 5h16v14H4zM4 16l5-5 4 4 3-3 4 4M15.5 9.5a1.5 1.5 0 1 0 0-.01' },
  audio: { nombre: 'Audio', color: '#ff5fa2', icono: 'M9 18V5l11-2v13M9 18a3 3 0 1 1-6 0 3 3 0 0 1 6 0zM20 16a3 3 0 1 1-6 0 3 3 0 0 1 6 0z' },
  video: { nombre: 'Vídeo', color: '#9b6bff', icono: 'M3 6h13v12H3zM16 10l5-3v10l-5-3' },
  pdf: { nombre: 'PDF', color: '#ff5a5f', icono: 'M6 3h9l4 4v14H6zM14 3v5h5M9 13h6M9 17h4' },
  texto: { nombre: 'Documento', color: '#4d8dff', icono: 'M6 3h9l4 4v14H6zM14 3v5h5M9 12h7M9 15h7M9 18h4' },
  hoja: { nombre: 'Hoja de cálculo', color: '#21b26b', icono: 'M5 4h14v16H5zM5 9h14M5 14h14M10 4v16' },
  presentacion: { nombre: 'Presentación', color: '#ff8a3d', icono: 'M3 4h18v12H3zM12 16v4M8 20h8' },
  comprimido: { nombre: 'Comprimido', color: '#b08b5b', icono: 'M6 3h12v18H6zM12 3v2M12 7v2M12 11v2M10 15h4v3h-4z' },
  codigo: { nombre: 'Código', color: '#2ec5d3', icono: 'M8 8l-4 4 4 4M16 8l4 4-4 4M14 5l-4 14' },
  programa: { nombre: 'Programa', color: '#8d979e', icono: 'M4 5h16v14H4zM4 9h16M7 7h.01M10 7h.01' },
  otro: { nombre: 'Archivo', color: '#8d979e', icono: 'M6 3h9l4 4v14H6zM14 3v5h5' },
};

export const RAICES: Record<string, { icono: string; color: string }> = {
  documentos: { icono: TIPOS.texto.icono, color: '#4d8dff' },
  escritorio: { icono: 'M3 4h18v12H3zM8 20h8M12 16v4', color: '#2ec5d3' },
  descargas: { icono: 'M12 4v11M7 10l5 5 5-5M5 20h14', color: '#3ddc84' },
  imagenes: { icono: TIPOS.imagen.icono, color: '#f5b83d' },
  musica: { icono: TIPOS.audio.icono, color: '#ff5fa2' },
  videos: { icono: TIPOS.video.icono, color: '#9b6bff' },
};
export const ICONO_PAPELERA = 'M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3';

export const extension = (n: string) => (n.includes('.') ? n.slice(n.lastIndexOf('.') + 1).toUpperCase().slice(0, 4) : '');
export const seVe = (e: Elemento) => ['imagen', 'audio', 'video', 'pdf'].includes(e.tipo) || /\.(txt|md|csv|log|json)$/i.test(e.nombre);
export const IMAGEN_WEB = /\.(jpe?g|png|gif|webp|avif|bmp)$/i;
