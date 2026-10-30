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
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();
    this.hub.on('metricas', (m: Metricas) => {
      this.actual.set(m);
      this.historial.update((h) => [...h, m].slice(-MAXIMO));
    });
    this.hub.onreconnecting(() => this.estado.set('reconectando'));
    this.hub.onreconnected(() => this.estado.set('en-vivo'));
    this.hub.onclose(() => this.estado.set('sin-conexion'));
    try {
      await this.hub.start();
      this.estado.set('en-vivo');
    } catch {
      this.estado.set('sin-conexion');
    }
  }

  async parar(): Promise<void> {
    if (this.hub && this.hub.state !== HubConnectionState.Disconnected) await this.hub.stop();
    this.hub = undefined;
    this.historial.set([]);
    this.actual.set(null);
    this.estado.set('conectando');
  }
}
