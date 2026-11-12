import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface DatosPerfil { usuario: string; nombre: string; apellidos: string; email: string; telefono: string; foto: boolean; }

// El perfil del dueño (nombre, correo, teléfono y foto), compartido por el menú y la página de perfil
@Injectable({ providedIn: 'root' })
export class PerfilService {
  private readonly http = inject(HttpClient);
  readonly datos = signal<DatosPerfil | null>(null);
  private readonly version = signal(Date.now());

  readonly nombreVisible = computed(() => {
    const d = this.datos();
    if (!d) return null;
    return d.nombre ? `${d.nombre} ${d.apellidos ?? ''}`.trim() : 'Mario'; // sin nombre en el perfil, el de siempre
  });
  // se recuerda si hay foto propia para pintar la buena desde el primer instante (sin saltar del avatar a la foto cuando llegan los datos)
  private readonly conFotoGuardada = (() => { try { return localStorage.getItem('mda-foto-propia') === '1'; } catch { return false; } })();
  readonly foto = computed(() => {
    const d = this.datos();
    return (d ? d.foto : this.conFotoGuardada) ? `/api/perfil/foto?v=${this.version()}` : 'img/avatar.webp?v=3';
  });

  async cargar(): Promise<void> {
    try {
      const d = await firstValueFrom(this.http.get<DatosPerfil>('/api/perfil'));
      this.datos.set(d);
      try { localStorage.setItem('mda-foto-propia', d.foto ? '1' : '0'); } catch { /* sin almacenamiento */ }
    } catch { /* sin conexión: se queda el usuario */ }
  }

  async guardar(d: { nombre: string; apellidos: string; email: string; telefono: string }): Promise<void> {
    await firstValueFrom(this.http.put('/api/perfil', d));
    await this.cargar();
  }

  async subirFoto(f: Blob): Promise<void> {
    await firstValueFrom(this.http.put('/api/perfil/foto', f, { headers: { 'Content-Type': f.type || 'image/jpeg' } }));
    this.version.set(Date.now());
    await this.cargar();
  }

  async quitarFoto(): Promise<void> {
    await firstValueFrom(this.http.delete('/api/perfil/foto'));
    await this.cargar();
  }
}
