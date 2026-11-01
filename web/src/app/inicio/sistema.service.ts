import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { firstValueFrom } from 'rxjs';

// Lo que emite la API (MetricasSistema en C#)
export interface Metricas {
  cpu: number;
  memoriaUsada: number;
  memoriaTotal: number;
  discoUsado: number;
  discoTotal: number;
  encendidoSegundos: number;
  nucleos: number;
  equipo: string;
  sistemaOperativo: string;
  hora: string;
  cpuModelo: string;
  discos: { nombre: string; usado: number; total: number }[] | null;
  redBajada: number;
  redSubida: number;
  // «equipo»: datos reales del PC (agente de Windows); «contenedor»: lo que ve Docker
  origen: 'equipo' | 'contenedor';
  // solo de un portátil con agente
  bateria?: number | null;
  cargando?: boolean | null;
  volumen?: number | null;
  brillo?: number | null;
}

export type EstadoConexion = 'conectando' | 'en-vivo' | 'reconectando' | 'sin-conexion';

const MAXIMO = 60;

// Estado del servidor: el historial al entrar por HTTP y, después, cada lectura en directo por SignalR
@Injectable({ providedIn: 'root' })
export class SistemaService {
  private readonly http = inject(HttpClient);
  private hub?: HubConnection;

  readonly actual = signal<Metricas | null>(null);
  readonly historial = signal<Metricas[]>([]);
  readonly estado = signal<EstadoConexion>('conectando');

  async empezar(): Promise<void> {
    if (this.hub) return; // ya conectado (el marco lo arranca una vez)
    try {
      const r = await firstValueFrom(this.http.get<{ actual: Metricas | null; historial: Metricas[] }>('/api/sistema'));
      this.historial.set(r.historial);
      this.actual.set(r.actual);
    } catch { /* el directo lo rellenará */ }

    this.hub = new HubConnectionBuilder()
      .withUrl('/hubs/sistema')
      // reintentos por defecto (0, 2, 10, 30 s) y, si se agotan, uno cada 30 s para siempre (ver reintentar)
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();
    this.hub.on('metricas', (m: Metricas) => {
      this.actual.set(m);
      this.historial.update((h) => [...h, m].slice(-MAXIMO));
    });
    this.hub.onreconnecting(() => this.estado.set('reconectando'));
    this.hub.onreconnected(() => this.estado.set('en-vivo'));
    this.hub.onclose(() => { this.estado.set('sin-conexion'); this.reintentar(); });
    try {
      await this.hub.start();
      this.estado.set('en-vivo');
    } catch {
      this.estado.set('sin-conexion');
      this.reintentar();
    }
  }

  private temporizador?: ReturnType<typeof setTimeout>;

  // sin red o con el equipo apagado: cada 30 s hasta que vuelva (antes se quedaba en «Sin conexión» hasta recargar)
  private reintentar(): void {
    clearTimeout(this.temporizador);
    this.temporizador = setTimeout(async () => {
      const hub = this.hub;
      if (!hub || hub.state !== HubConnectionState.Disconnected) return;
      try { await hub.start(); this.estado.set('en-vivo'); } catch { this.reintentar(); }
    }, 30_000);
  }

  async parar(): Promise<void> {
    clearTimeout(this.temporizador);
    if (this.hub && this.hub.state !== HubConnectionState.Disconnected) await this.hub.stop();
    this.hub = undefined;
    this.historial.set([]);
    this.actual.set(null);
    this.estado.set('conectando');
  }
}
