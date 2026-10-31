import { Injectable, signal } from '@angular/core';

// Para abrir el asistente desde cualquier sitio (la barra, el buscador, el inicio), con una frase ya escrita o sin nada
@Injectable({ providedIn: 'root' })
export class AsistenteService {
  readonly peticion = signal<{ texto: string; voz: boolean; n: number } | null>(null);
  private n = 0;

  abrir(texto = '', voz = false): void { this.peticion.set({ texto, voz, n: ++this.n }); }
}
