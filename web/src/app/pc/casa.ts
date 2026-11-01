import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

interface AvisosPc { descargas: boolean; windows: boolean; copias: boolean; bateria: boolean; }

// Casa y modos, música del PC y qué quieres que te avise el PC. Todo son órdenes al agente de Windows: si no está en marcha, lo dice.
@Component({
  selector: 'app-casa-pc',
  styleUrls: ['../gastos/comun.css', './modulos.css'],
  template: `
    <section class="caja">
      <header class="caja__cab"><h3>Casa y modos</h3><span class="sub">varias órdenes de una vez</span></header>
      <div class="botones">
        <button class="boton" type="button" (click)="casa('llegada')" [disabled]="ocupado()">🏠 Llegué a casa</button>
        <button class="boton" type="button" (click)="casa('salida')" [disabled]="ocupado()">🚪 Me voy</button>
        <button class="boton" type="button" [attr.aria-pressed]="concentracion()" (click)="concentrar()" [disabled]="ocupado()">🎯 Modo concentración {{ concentracion() ? '· activo' : '' }}</button>
      </div>
      <p class="nada">«Llegué»: brillo al 70 %, volumen al 30 % y el PC puede suspenderse. «Me voy»: bloquea el PC. «Concentración»: silencia, baja la pantalla y bloquea; al apagarla vuelve todo como estaba.
        También lo hace tu iPhone solo con una automatización por lugar (Seguridad › Atajos, permiso «casa»).</p>
      @if (mensaje()) { <p class="msg" role="status">{{ mensaje() }}</p> }
    </section>

    <section class="caja">
      <header class="caja__cab"><h3>Música del PC</h3><span class="sub">Spotify de escritorio, el navegador, VLC…</span></header>
      <div class="botones botones--musica">
        <button class="boton" type="button" (click)="musica('anterior')" aria-label="Anterior">⏮</button>
        <button class="boton boton--grande" type="button" (click)="musica('playpause')" aria-label="Reproducir o pausar">⏯</button>
        <button class="boton" type="button" (click)="musica('siguiente')" aria-label="Siguiente">⏭</button>
        <button class="boton" type="button" (click)="musica('silencio')" aria-label="Silenciar">🔇</button>
      </div>
    </section>

    <section class="caja">
      <header class="caja__cab"><h3>Avisos del PC</h3><span class="sub">en el móvil y por Telegram</span></header>
      @if (avisos(); as a) {
        <label class="interruptor"><input type="checkbox" [checked]="a.descargas" (change)="cambiar({ descargas: $any($event.target).checked })"><span><strong>Descargas terminadas</strong><small>Cuando un archivo acaba de bajar a la carpeta Descargas.</small></span></label>
        <label class="interruptor"><input type="checkbox" [checked]="a.windows" (change)="cambiar({ windows: $any($event.target).checked })"><span><strong>Windows quiere reiniciar</strong><small>Cuando termina de preparar actualizaciones.</small></span></label>
        <label class="interruptor"><input type="checkbox" [checked]="a.copias" (change)="cambiar({ copias: $any($event.target).checked })"><span><strong>Copia de seguridad hecha</strong><small>Un aviso por cada copia nueva.</small></span></label>
        <label class="interruptor"><input type="checkbox" [checked]="a.bateria" (change)="cambiar({ bateria: $any($event.target).checked })"><span><strong>Batería del portátil</strong><small>Al bajar del 15 % sin enchufar y si lleva más de 2 h enchufado al 100 %.</small></span></label>
      }
    </section>
  `,
})
export class CasaPc implements OnInit {
  private readonly http = inject(HttpClient);
  protected readonly concentracion = signal(false);
  protected readonly avisos = signal<AvisosPc | null>(null);
  protected readonly mensaje = signal('');
  protected readonly ocupado = signal(false);

  ngOnInit(): void {
    firstValueFrom(this.http.get<{ concentracion: boolean }>('/api/pc')).then((p) => this.concentracion.set(p.concentracion)).catch(() => {});
    firstValueFrom(this.http.get<AvisosPc>('/api/pc/avisos')).then((a) => this.avisos.set(a)).catch(() => {});
  }

  private async pedir(url: string, cuerpo: unknown): Promise<{ mensaje?: string } | null> {
    this.ocupado.set(true); this.mensaje.set('');
    try { return await firstValueFrom(this.http.post<{ mensaje?: string }>(url, cuerpo)); }
    catch (e) {
      const x = e as HttpErrorResponse;
      this.mensaje.set(x.error?.detalle ?? x.error?.mensaje ?? (x.status === 429 ? 'Demasiado seguido: espera un momento.' : 'No se pudo.'));
      return null;
    } finally { this.ocupado.set(false); }
  }

  protected async casa(estado: 'llegada' | 'salida'): Promise<void> {
    const r = await this.pedir('/api/pc/casa', { estado });
    if (r) this.mensaje.set(r.mensaje ?? 'Hecho.');
  }

  protected async concentrar(): Promise<void> {
    const activo = !this.concentracion();
    const r = await this.pedir('/api/pc/concentracion', { activo });
    if (r) { this.concentracion.set(activo); this.mensaje.set(r.mensaje ?? 'Hecho.'); }
  }

  protected async musica(accion: string): Promise<void> {
    this.mensaje.set('');
    try { await firstValueFrom(this.http.post('/api/pc/multimedia', { accion })); }
    catch (e) { this.mensaje.set((e as HttpErrorResponse).error?.detalle ?? 'No se pudo.'); }
  }

  protected async cambiar(parte: Partial<AvisosPc>): Promise<void> {
    const nuevo = { ...this.avisos()!, ...parte };
    this.avisos.set(nuevo);
    await firstValueFrom(this.http.put('/api/pc/avisos', nuevo));
  }
}
