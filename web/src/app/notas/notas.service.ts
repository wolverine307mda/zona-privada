import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface Nota {
  id: number; titulo: string; contenido: string; color: string; etiquetas: string[];
  fijada: boolean; archivada: boolean; creado: string; modificado: string;
}
export type NotaEntrada = Pick<Nota, 'titulo' | 'contenido' | 'color' | 'etiquetas' | 'fijada' | 'archivada'>;

// colores de las notas: el id es el que guarda la API
export const COLORES_NOTA: { id: string; nombre: string; color: string }[] = [
  { id: '', nombre: 'Sin color', color: 'var(--soft)' },
  { id: 'amarillo', nombre: 'Amarillo', color: '#ffd166' },
  { id: 'naranja', nombre: 'Naranja', color: '#ff8a3d' },
  { id: 'rojo', nombre: 'Rojo', color: '#ff5a5f' },
  { id: 'rosa', nombre: 'Rosa', color: '#ff5fa2' },
  { id: 'morado', nombre: 'Morado', color: '#9b6bff' },
  { id: 'azul', nombre: 'Azul', color: '#4d8dff' },
  { id: 'verde', nombre: 'Verde', color: '#3ddc84' },
];
export const colorNota = (id: string) => COLORES_NOTA.find((c) => c.id === id)?.color ?? 'var(--soft)';

@Injectable({ providedIn: 'root' })
export class NotasService {
  private readonly http = inject(HttpClient);

  lista(archivadas = false, q = '') {
    return firstValueFrom(this.http.get<Nota[]>('/api/notas', { params: q ? { archivadas, q } : { archivadas } }));
  }
  guardar(n: NotaEntrada, id?: number) {
    return firstValueFrom(id ? this.http.put<Nota>(`/api/notas/${id}`, n) : this.http.post<Nota>('/api/notas', n));
  }
  borrar(id: number) { return firstValueFrom(this.http.delete(`/api/notas/${id}`)); }
}
