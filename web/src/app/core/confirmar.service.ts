import { Injectable, signal } from '@angular/core';

// «Confirma que eres tú» (Face ID o contraseña) como ventana propia: quien lo necesita hace `await confirmar.pedir()` y sigue si es true.
// La ventana es app-confirmar, que vive en el marco de la app. Vale lo mismo que Reautenticar: el servidor deja la sesión confirmada 2 minutos.
@Injectable({ providedIn: 'root' })
export class ConfirmarService {
  readonly abierto = signal(false);
  readonly motivo = signal('');
  private espera: Promise<boolean> | null = null;
  private resolver: ((ok: boolean) => void) | null = null;

  pedir(motivo = 'Confirma que eres tú para seguir.'): Promise<boolean> {
    this.motivo.set(motivo);
    if (this.espera) return this.espera;
    this.abierto.set(true);
    return (this.espera = new Promise((res) => { this.resolver = res; }));
  }

  terminar(ok: boolean): void {
    this.abierto.set(false);
    const r = this.resolver; this.resolver = null; this.espera = null;
    r?.(ok);
  }
}
