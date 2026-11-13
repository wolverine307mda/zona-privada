import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { pedirCredencial, soportaPasskeys } from './passkey';

// Si el móvil no tiene una llave válida para este dominio, la espera de Face ID puede no terminar nunca y la pantalla se queda en
// «Comprobando…»: pasado este tiempo se da por fallida y se ofrece la contraseña.
export const ESPERA_PASSKEY = 45_000;

export function conLimite<T>(p: Promise<T>, ms: number): Promise<T> {
  return new Promise<T>((res, rej) => {
    const t = setTimeout(() => rej(new Error('Face ID no responde')), ms);
    p.then((v) => { clearTimeout(t); res(v); }, (e) => { clearTimeout(t); rej(e); });
  });
}

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
      const credencial = await conLimite(pedirCredencial(opciones), ESPERA_PASSKEY);
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
