import { HttpClient } from '@angular/common/http';
import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { PerfilService } from '../core/perfil.service';
import { Errores, erroresDe, reducirFoto } from '../gastos/gastos.service';

// Perfil: el usuario para entrar no cambia; nombre, apellidos, correo, teléfono y foto, sí
@Component({
  selector: 'app-perfil',
  imports: [RouterLink],
  template: `
    <section class="tarjeta-perfil">
      <div class="foto">
        <img [src]="p.foto()" alt="" width="140" height="140">
        <label class="foto__cambiar" title="Cambiar la foto">
          <svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M4 7h3l2-3h6l2 3h3v12H4zM12 17a4 4 0 1 0 0-8 4 4 0 0 0 0 8z" /></svg>
          <span class="sr">Cambiar la foto</span>
          <input type="file" accept="image/*" (change)="elegirFoto($event)" hidden>
        </label>
      </div>
      <div class="identidad">
        <h2>{{ completo() || 'Tu nombre' }}</h2>
        <p>&#64;{{ p.datos()?.usuario }} · Administrador</p>
        @if (p.datos()?.foto) { <button class="enlace" type="button" (click)="quitarFoto()">Quitar la foto</button> }
      </div>
    </section>

    <form class="caja formulario" (submit)="guardar($event)" novalidate>
      <header class="caja__cab"><h3>Tus datos</h3><span class="sub">El usuario para entrar sigue siendo «{{ p.datos()?.usuario }}»</span></header>
      <div class="fila2">
        <label class="campo"><span class="campo__etiqueta">Nombre</span>
          <input class="campo__input" type="text" maxlength="60" autocomplete="given-name" [value]="nombre()" (input)="nombre.set(valor($event))"></label>
        <label class="campo"><span class="campo__etiqueta">Apellidos</span>
          <input class="campo__input" type="text" maxlength="100" autocomplete="family-name" [value]="apellidos()" (input)="apellidos.set(valor($event))"></label>
      </div>
      @if (errores()['nombre'] || errores()['apellidos']) { <p class="error">{{ (errores()['nombre'] ?? errores()['apellidos'])[0] }}</p> }
      <div class="fila2">
        <label class="campo"><span class="campo__etiqueta">Correo</span>
          <input class="campo__input" type="email" maxlength="200" autocomplete="email" [value]="email()" (input)="email.set(valor($event))"></label>
        <label class="campo"><span class="campo__etiqueta">Teléfono</span>
          <span class="telefono"><em>🇪🇸 +34</em><input class="campo__input" type="tel" maxlength="20" autocomplete="tel-national" placeholder="600 00 00 00" [value]="telefono()" (input)="telefono.set(valor($event))"></span></label>
      </div>
      @if (errores()['email']) { <p class="error">{{ errores()['email'][0] }}</p> }
      @if (errores()['telefono']) { <p class="error">{{ errores()['telefono'][0] }}</p> }
      @if (errores()['general']) { <p class="error">{{ errores()['general'][0] }}</p> }
      <footer class="pie">
        @if (guardado()) { <span class="ok">✓ Guardado</span> }
        <button class="button" type="submit" [disabled]="guardando()">{{ guardando() ? 'Guardando…' : 'Guardar' }}</button>
      </footer>
    </form>

    <form class="caja formulario" (submit)="cambiarClave($event)" novalidate>
      <header class="caja__cab"><h3>Contraseña</h3><span class="sub">Al cambiarla se cierra la sesión en tus otros dispositivos</span></header>
      <!-- campo de usuario oculto para que el gestor de contraseñas sepa de qué cuenta es -->
      <input type="text" autocomplete="username" [value]="p.datos()?.usuario ?? ''" hidden>
      <label class="campo"><span class="campo__etiqueta">Contraseña actual</span>
        <input class="campo__input" [type]="verClave() ? 'text' : 'password'" autocomplete="current-password" [value]="actual()" (input)="actual.set(valor($event))"></label>
      @if (erroresClave()['actual']) { <p class="error">{{ erroresClave()['actual'][0] }}</p> }
      <div class="fila2">
        <label class="campo"><span class="campo__etiqueta">Nueva (mínimo 8)</span>
          <input class="campo__input" [type]="verClave() ? 'text' : 'password'" autocomplete="new-password" minlength="8" [value]="nueva()" (input)="nueva.set(valor($event))"></label>
        <label class="campo"><span class="campo__etiqueta">Repite la nueva</span>
          <input class="campo__input" [type]="verClave() ? 'text' : 'password'" autocomplete="new-password" [value]="repetida()" (input)="repetida.set(valor($event))"></label>
      </div>
      @if (nueva() && repetida() && nueva() !== repetida()) { <p class="error">Las dos nuevas no coinciden.</p> }
      @if (erroresClave()['nueva']) { <p class="error">{{ erroresClave()['nueva'][0] }}</p> }
      @if (erroresClave()['general']) { <p class="error">{{ erroresClave()['general'][0] }}</p> }
      <footer class="pie">
        <label class="ver"><input type="checkbox" [checked]="verClave()" (change)="verClave.set(!verClave())"> Ver</label>
        @if (claveCambiada()) { <span class="ok">✓ Contraseña cambiada</span> }
        <button class="button" type="submit" [disabled]="cambiando() || !actual() || nueva().length < 8 || nueva() !== repetida()">{{ cambiando() ? 'Cambiando…' : 'Cambiar contraseña' }}</button>
      </footer>
    </form>

    <section class="caja atajos">
      <a routerLink="/seguridad"><strong>Dos pasos y sesiones</strong><span>En Seguridad</span></a>
      <a routerLink="/seguridad"><strong>Avisos</strong><span>Correo, notificaciones y resumen semanal</span></a>
    </section>
  `,
  styleUrls: ['../gastos/comun.css', '../gastos/dialogos.css'],
  styles: `
    .tarjeta-perfil {
      position: relative; overflow: hidden; display: flex; align-items: center; gap: clamp(18px, 4vw, 36px); padding: clamp(22px, 4vw, 40px);
      border-radius: calc(var(--radio) + 8px); color: #fff;
      background: radial-gradient(120% 160% at 0% 0%, color-mix(in srgb, var(--brand) 85%, #000), #0a1016 70%);
      box-shadow: 0 30px 70px -40px color-mix(in srgb, var(--brand) 90%, transparent), var(--sombra);
    }
    .tarjeta-perfil::after { content: ''; position: absolute; inset: 0; background: radial-gradient(circle, rgba(255, 255, 255, 0.09) 1.2px, transparent 1.5px) 0 0 / 16px 16px; mask: linear-gradient(110deg, transparent 35%, #000); pointer-events: none; }
    .foto { position: relative; flex: none; z-index: 1; }
    .foto img { width: clamp(96px, 16vw, 140px); height: clamp(96px, 16vw, 140px); border-radius: 50%; object-fit: cover; box-shadow: 0 0 0 4px rgba(255, 255, 255, 0.9), 0 20px 40px -12px rgba(0, 0, 0, 0.6); background: var(--brand); }
    .foto__cambiar { position: absolute; right: 2px; bottom: 2px; display: grid; place-items: center; width: 40px; height: 40px; border-radius: 50%; background: #fff; color: #0a1016; cursor: pointer; box-shadow: 0 6px 16px rgba(0, 0, 0, 0.35); transition: transform 160ms var(--ease-out); }
    .foto__cambiar:active { transform: scale(0.9); }
    .foto__cambiar .icon { width: 19px; height: 19px; }
    .identidad { position: relative; z-index: 1; min-width: 0; display: flex; flex-direction: column; gap: 6px; }
    .identidad h2 { font: 700 clamp(1.6rem, 1rem + 3vw, 3rem)/1 var(--font-display); letter-spacing: -0.045em; text-transform: uppercase; overflow-wrap: anywhere; }
    .identidad p { color: rgba(255, 255, 255, 0.78); }
    .identidad .enlace { color: #fff; opacity: 0.8; align-self: flex-start; }
    .formulario { gap: 16px; }
    .sub { font-size: 12px; color: var(--soft); }
    .telefono { display: flex; align-items: stretch; gap: 8px; }
    .telefono em { display: grid; place-items: center; padding: 0 12px; border-radius: 12px; background: var(--tarjeta-2); font-style: normal; font-weight: 500; white-space: nowrap; }
    .telefono input { flex: 1; min-width: 0; }
    .pie { display: flex; justify-content: flex-end; align-items: center; gap: 14px; }
    .pie .button { min-height: 46px; padding: 0 24px; }
    .ok { color: var(--ok); font-weight: 500; }
    .ver { display: inline-flex; align-items: center; gap: 6px; margin-right: auto; font-size: var(--step-small); color: var(--soft); cursor: pointer; }
    .atajos { flex-direction: row; flex-wrap: wrap; gap: 10px; padding: 12px; }
    .atajos a { flex: 1 1 240px; display: flex; flex-direction: column; gap: 2px; padding: 14px; border-radius: 12px; background: var(--tarjeta-2); color: var(--texto); text-decoration: none; }
    .atajos span { font-size: var(--step-small); color: var(--soft); }
    @media (hover: hover) { .atajos a:hover { background: color-mix(in srgb, var(--brand) 10%, var(--tarjeta-2)); } }
    @media (max-width: 600px) { .tarjeta-perfil { flex-direction: column; text-align: center; } .identidad { align-items: center; } .identidad .enlace { align-self: center; } .fila2 { grid-template-columns: minmax(0, 1fr); } }
  `,
  host: { class: 'pagina-perfil' },
})
export class Perfil {
  protected readonly p = inject(PerfilService);
  protected readonly nombre = signal('');
  protected readonly apellidos = signal('');
  protected readonly email = signal('');
  protected readonly telefono = signal('');
  protected readonly errores = signal<Errores>({});
  protected readonly guardando = signal(false);
  protected readonly guardado = signal(false);
  // cambio de contraseña
  private readonly http = inject(HttpClient);
  protected readonly actual = signal('');
  protected readonly nueva = signal('');
  protected readonly repetida = signal('');
  protected readonly verClave = signal(false);
  protected readonly cambiando = signal(false);
  protected readonly claveCambiada = signal(false);
  protected readonly erroresClave = signal<Errores>({});
  protected readonly completo = computed(() => `${this.nombre()} ${this.apellidos()}`.trim());

  constructor() {
    this.p.cargar();
    effect(() => {
      const d = this.p.datos();
      if (!d) return;
      untracked(() => {
        this.nombre.set(d.nombre);
        this.apellidos.set(d.apellidos);
        this.email.set(d.email);
        this.telefono.set(d.telefono.replace(/^\+34\s*/, ''));
      });
    });
  }

  protected valor(e: Event): string { return (e.target as HTMLInputElement).value; }

  protected async guardar(e: Event): Promise<void> {
    e.preventDefault();
    this.guardando.set(true);
    this.guardado.set(false);
    this.errores.set({});
    try {
      const tel = this.telefono().trim();
      await this.p.guardar({ nombre: this.nombre(), apellidos: this.apellidos(), email: this.email(), telefono: tel && !tel.startsWith('+') ? `+34 ${tel}` : tel });
      this.guardado.set(true);
      setTimeout(() => this.guardado.set(false), 2500);
    } catch (err) { this.errores.set(erroresDe(err)); } finally { this.guardando.set(false); }
  }

  protected async cambiarClave(e: Event): Promise<void> {
    e.preventDefault();
    if (this.nueva() !== this.repetida()) return;
    this.cambiando.set(true);
    this.claveCambiada.set(false);
    this.erroresClave.set({});
    try {
      await firstValueFrom(this.http.post('/api/perfil/clave', { actual: this.actual(), nueva: this.nueva() }));
      this.actual.set(''); this.nueva.set(''); this.repetida.set('');
      this.claveCambiada.set(true);
    } catch (err) { this.erroresClave.set(erroresDe(err)); } finally { this.cambiando.set(false); }
  }

  protected async elegirFoto(e: Event): Promise<void> {
    const input = e.target as HTMLInputElement;
    const f = input.files?.[0];
    input.value = '';
    if (!f) return;
    try { await this.p.subirFoto(await reducirFoto(f, 512, 0.88)); } catch (err) { this.errores.set(erroresDe(err)); }
  }

  protected async quitarFoto(): Promise<void> { await this.p.quitarFoto(); }
}
