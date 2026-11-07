import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

// Fondos que reaccionan: el fondo animado respira con lo que pasa (la música que suena, la carga del PC o la hora del día).
// Todo es suave y aproximado: Spotify ya no da el análisis del audio a las apps nuevas, así que el «latido» no sigue el ritmo real,
// solo sabe que suena. Cada cuenta es pura y está aparte para poder probarla; `energia()` devuelve siempre un número de 0 a 1.
export type Reaccion = 'nada' | 'musica' | 'carga' | 'hora';
export const REACCIONES: { id: Reaccion; nombre: string; nota: string }[] = [
  { id: 'nada', nombre: 'Nada', nota: 'el fondo va a su aire' },
  { id: 'musica', nombre: 'Música', nota: 'respira mientras suena algo en Spotify' },
  { id: 'carga', nombre: 'Carga del PC', nota: 'se acelera con el procesador' },
  { id: 'hora', nombre: 'Hora', nota: 'más vivo de día, más tranquilo de noche' },
];

const limitar = (x: number, a = 0, b = 1) => Math.min(b, Math.max(a, x));

// Respiración lenta (un ciclo cada ~1,7 s) mientras suena; en pausa, nada
export const energiaMusica = (sonando: boolean, t: number): number => (sonando ? 0.5 + 0.5 * Math.sin(t * Math.PI * 2 * 0.6) : 0);
// La carga del procesador (0–100 %) como 0–1
export const energiaCarga = (cpu: number | null | undefined): number => limitar((cpu ?? 0) / 100);
// Máxima a media tarde (14 h) y mínima de madrugada (2 h)
export const energiaHora = (hora: number): number => limitar(0.5 - 0.5 * Math.cos(((hora - 2) / 24) * Math.PI * 2));
// De cuánto acelera / aviva: «cuanto» va de 0 (nada) a 1 (a tope)
export const factorVelocidad = (energia: number, cuanto: number): number => 1 + limitar(energia) * limitar(cuanto) * 0.8;
export const factorIntensidad = (energia: number, cuanto: number): number => 1 + limitar(energia) * limitar(cuanto) * 0.35;
// En el modo ligero (móvil) solo reacciona la música, y a media tasa
export const reaccionPermitida = (r: Reaccion, ligero: boolean): Reaccion => (ligero && r !== 'musica' ? 'nada' : r);

const CLAVE_MODO = 'zp-fondo-reaccion', CLAVE_CUANTO = 'zp-fondo-cuanto';

@Injectable({ providedIn: 'root' })
export class SenalesFondo {
  private readonly http = inject(HttpClient);
  readonly modo = signal<Reaccion>(SenalesFondo.leerModo());
  readonly cuanto = signal<number>(SenalesFondo.leerCuanto());      // 0–100
  private sonando = false;
  private cpu = 0;
  private sondeo?: ReturnType<typeof setInterval>;
  private sinSpotify = false;

  private static leerModo(): Reaccion { try { const v = localStorage.getItem(CLAVE_MODO); return REACCIONES.some((r) => r.id === v) ? (v as Reaccion) : 'nada'; } catch { return 'nada'; } }
  private static leerCuanto(): number { try { const n = Number(localStorage.getItem(CLAVE_CUANTO)); return Number.isFinite(n) && n >= 0 && n <= 100 && localStorage.getItem(CLAVE_CUANTO) !== null ? n : 50; } catch { return 50; } }

  ponerModo(m: Reaccion): void { this.modo.set(m); try { localStorage.setItem(CLAVE_MODO, m); } catch { /* sin almacenamiento */ } this.vigilar(); }
  ponerCuanto(n: number): void { this.cuanto.set(limitar(n, 0, 100)); try { localStorage.setItem(CLAVE_CUANTO, String(this.cuanto())); } catch { /* sin almacenamiento */ } }

  // Solo se pregunta al servidor mientras haga falta (música o carga) y con la pestaña visible
  vigilar(ligero = false): void {
    clearInterval(this.sondeo);
    const m = reaccionPermitida(this.modo(), ligero);
    if (m !== 'musica' && m !== 'carga') return;
    void this.leer(m);
    this.sondeo = setInterval(() => { if (!document.hidden) void this.leer(m); }, m === 'musica' ? (ligero ? 30_000 : 15_000) : 10_000);
  }

  parar(): void { clearInterval(this.sondeo); }

  private async leer(m: Reaccion): Promise<void> {
    try {
      if (m === 'musica') {
        if (this.sinSpotify) return;
        this.sonando = !!(await firstValueFrom(this.http.get<{ sonando: boolean }>('/api/musica/ahora'))).sonando;
      } else {
        this.cpu = (await firstValueFrom(this.http.get<{ actual?: { cpu?: number } }>('/api/sistema'))).actual?.cpu ?? 0;
      }
    } catch (e) { if ((e as { status?: number }).status === 409) this.sinSpotify = true; this.sonando = false; this.cpu = 0; }
  }

  // La energía del momento (0–1) para el modo elegido; `t` es el reloj del fondo en segundos
  energia(t: number, ligero = false, hora = new Date().getHours() + new Date().getMinutes() / 60): number {
    switch (reaccionPermitida(this.modo(), ligero)) {
      case 'musica': return energiaMusica(this.sonando, t);
      case 'carga': return energiaCarga(this.cpu);
      case 'hora': return energiaHora(hora);
      default: return 0;
    }
  }
}
