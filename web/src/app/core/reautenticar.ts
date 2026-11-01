import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { pedirCredencial, soportaPasskeys } from './passkey';

// Volver a demostrar que eres tú antes de lo delicado: con Face ID (passkey) o, de respaldo, con la contraseña.
// En los dos casos el servidor deja la sesión «confirmada» 2 minutos.
@Injectable({ providedIn: 'root' })
export class Reautenticar {
  private readonly http = inject(HttpClient);
  readonly conPasskey = soportaPasskeys();

  /** Face ID / huella. Devuelve true si se confirmó, false si cancelaste el diálogo del sistema; lanza si el passkey no sirve. */
  async confirmarConPasskey(): Promise<boolean> {
    try {
      const opciones = await firstValueFrom(this.http.post<unknown>('/api/seguridad/reautenticar/passkey/opciones', null));
      const credencial = await pedirCredencial(opciones);
      await firstValueFrom(this.http.post('/api/seguridad/reautenticar/passkey', { credencial }));
      return true;
    } catch (e) {
      if (e instanceof DOMException && (e.name === 'NotAllowedError' || e.name === 'AbortError')) return false;
      throw e;
    }
  }

  async confirmarConClave(password: string, codigo: string | null): Promise<void> {
    await firstValueFrom(this.http.post('/api/seguridad/reautenticar', { password, codigo: codigo || null }));
  }
}
