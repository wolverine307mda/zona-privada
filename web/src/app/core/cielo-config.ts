import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export type FondoEntrada = 'candado' | 'cielo';
export interface LugarCielo { nombre: string; lat: number; lon: number; origen: 'defecto' | 'servidor' | 'manual'; }

const CLAVE_FONDO = 'mda-entrada-fondo';
const CLAVE_LUGAR = 'mda-entrada-lugar';
const MADRID: LugarCielo = { nombre: 'Madrid', lat: 40.4168, lon: -3.7038, origen: 'defecto' };

function leer<T>(clave: string): T | null {
  try { const v = localStorage.getItem(clave); return v ? (JSON.parse(v) as T) : null; } catch { return null; }
}
function guardar(clave: string, valor: unknown): void {
  try { localStorage.setItem(clave, JSON.stringify(valor)); } catch { /* sin almacenamiento: vale para esta visita */ }
}

// Qué se ve detrás de la pantalla de entrar (y del bloqueo): el candado 3D de siempre o el cielo de verdad, con el Sol y la Luna donde están ahora mismo.
// Es un ajuste de cada dispositivo y se guarda ahí mismo, porque la pantalla de entrar se ve antes de que haya sesión y no puede preguntar nada al servidor.
// El lugar sale de tu sitio del tiempo (se copia aquí cada vez que entras), de tu ubicación o de la que pongas a mano; sin nada, Madrid.
@Injectable({ providedIn: 'root' })
export class CieloConfig {
  private readonly http = inject(HttpClient);
  readonly fondo = signal<FondoEntrada>(leer<FondoEntrada>(CLAVE_FONDO) === 'cielo' ? 'cielo' : 'candado');
  readonly lugar = signal<LugarCielo>(valido(leer<LugarCielo>(CLAVE_LUGAR)) ? leer<LugarCielo>(CLAVE_LUGAR)! : MADRID);

  ponerFondo(f: FondoEntrada): void { this.fondo.set(f); guardar(CLAVE_FONDO, f); }

  ponerLugar(l: LugarCielo): void { this.lugar.set(l); guardar(CLAVE_LUGAR, l); }

  /** Vuelve al sitio del tiempo de la app (o a Madrid si no hay). */
  async usarElDelTiempo(): Promise<boolean> {
    const l = await this.delServidor();
    if (!l) return false;
    this.ponerLugar({ ...l, origen: 'servidor' });
    return true;
  }

  /** Con sesión: copia aquí el sitio del tiempo para que la pantalla de entrar lo tenga a mano. Si lo pusiste a mano, no lo pisa. */
  async sincronizar(): Promise<void> {
    if (this.lugar().origen === 'manual') return;
    const l = await this.delServidor();
    if (l) this.ponerLugar({ ...l, origen: 'servidor' });
  }

  /** Con el GPS del dispositivo (pide permiso). */
  usarMiUbicacion(): Promise<boolean> {
    return new Promise((resolver) => {
      if (!navigator.geolocation) { resolver(false); return; }
      navigator.geolocation.getCurrentPosition(
        (p) => { this.ponerLugar({ nombre: 'Tu ubicación', lat: redondear(p.coords.latitude), lon: redondear(p.coords.longitude), origen: 'manual' }); resolver(true); },
        () => resolver(false),
        { maximumAge: 3_600_000, timeout: 10_000 },
      );
    });
  }

  private async delServidor(): Promise<{ nombre: string; lat: number; lon: number } | null> {
    try {
      const r = await firstValueFrom(this.http.get<{ lugar?: { nombre: string; lat: number; lon: number } | null }>('/api/tiempo'));
      const l = r?.lugar;
      return l && Number.isFinite(l.lat) && Number.isFinite(l.lon) ? { nombre: l.nombre, lat: l.lat, lon: l.lon } : null;
    } catch { return null; }
  }
}

const redondear = (n: number): number => Math.round(n * 1e4) / 1e4;
function valido(l: LugarCielo | null): boolean {
  return !!l && Number.isFinite(l.lat) && Number.isFinite(l.lon) && Math.abs(l.lat) <= 90 && Math.abs(l.lon) <= 180;
}
