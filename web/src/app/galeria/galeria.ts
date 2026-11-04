import { HttpClient } from '@angular/common/http';
import { Component, ElementRef, OnInit, computed, inject, signal, viewChild } from '@angular/core';
import { firstValueFrom } from 'rxjs';

interface Foto { id: string; ruta: string; titulo: string; descripcion: string; alt: string; categoria: string; orden: number; ancho: number; alto: number; tono: string; hash: string; publicada: boolean; enGlobo?: boolean; lat?: number | null; lon?: number | null; lugar?: string; }
interface Sitio { nombre: string; lat: number; lon: number; region?: string | null; }
interface Elemento { nombre: string; ruta: string; carpeta: boolean; tamano: number; soportada: boolean; v: string | null; foto: { id: string; publicada: boolean } | null; }
interface Estado { activa: boolean; publicadas: number; total: number; }

const CATEGORIAS = ['naturaleza', 'ciudad', 'animales', 'motor'];

// Galería del portfolio: eliges qué fotos de la carpeta «Imágenes» se enseñan en la web, con su descripción y su orden.
// Nada se publica hasta activar el interruptor general y marcar cada foto; el original nunca sale del equipo.
@Component({
  selector: 'app-galeria',
  template: `
    <nav class="pestanas" role="tablist" aria-label="Partes de la galería">
      <button type="button" role="tab" [attr.aria-selected]="pestana() === 'fotos'" (click)="pestana.set('fotos')">Fotos <small>{{ estado()?.publicadas ?? 0 }}</small></button>
      <button type="button" role="tab" [attr.aria-selected]="pestana() === 'web'" (click)="pestana.set('web')">Qué ve la web</button>
    </nav>
    @if (pestana() === 'fotos') {
    <section class="caja">
      <header class="caja__cab">
        <h3>Galería del portfolio</h3>
        <span class="sub">{{ estado()?.publicadas ?? 0 }} publicadas · solo se ve en la web mientras este equipo esté encendido</span>
      </header>
      <label class="interruptor">
        <input type="checkbox" [checked]="estado()?.activa" (change)="activar($event)">
        <span>
          <strong>{{ estado()?.activa ? 'Galería visible en la web' : 'Galería oculta' }}</strong>
          <small>Apagado = la web pública no ve ninguna foto, aunque estén marcadas.</small>
        </span>
      </label>
    </section>
    <section class="caja">
      <header class="caja__cab"><h3>En el portfolio</h3><span class="sub">El orden de aquí es el de la web</span></header>
      @if (fotos().length === 0) {
        <p class="vacio">Aún no has publicado nada. Elige fotos de tu carpeta de abajo.</p>
      } @else {
        <ol class="lista">
          @for (f of fotos(); track f.id; let i = $index) {
            <li class="fila" [class.oculta]="!f.publicada">
              <img class="mini" [src]="'/api/galeria/fotos/' + f.id + '/mini?v=' + f.hash" [style.background]="f.tono" alt="" width="64" height="64" loading="lazy">
              <div class="texto">
                <strong>{{ f.titulo || 'Sin título' }}</strong>
                <small>{{ f.categoria || 'sin categoría' }} · {{ f.ancho }}×{{ f.alto }}{{ f.publicada ? '' : ' · oculta' }}</small>
                <small class="desc">{{ f.descripcion }}</small>
              </div>
              <div class="acciones">
                <button class="icono" type="button" (click)="mover(i, -1)" [disabled]="i === 0" aria-label="Subir">↑</button>
                <button class="icono" type="button" (click)="mover(i, 1)" [disabled]="i === fotos().length - 1" aria-label="Bajar">↓</button>
                <button class="enlace" type="button" (click)="editar(f)">Editar</button>
              </div>
            </li>
          }
        </ol>
      }
    </section>
    <section class="caja">
      <header class="caja__cab">
        <h3>Tu carpeta de imágenes</h3>
        <span class="sub">
          <button class="enlace" type="button" (click)="abrir('')">Imágenes</button>
          @for (m of migas(); track m.ruta) { / <button class="enlace" type="button" (click)="abrir(m.ruta)">{{ m.nombre }}</button> }
        </span>
      </header>
      @if (fallo()) { <p class="error">No se pudo leer la carpeta. ¿Está montada «Imágenes» en el equipo?</p> }
      <ul class="rejilla" [attr.aria-busy]="cargando()">
        @if (cargando()) { @for (_ of esqueletos; track $index) { <li class="foto-esqueleto" aria-hidden="true"></li> } }
        @for (e of elementos(); track e.ruta) {
          <li>
            @if (e.carpeta) {
              <button class="carpeta" type="button" (click)="abrir(e.ruta)"><span aria-hidden="true">📁</span> {{ e.nombre }}</button>
            } @else {
              <button class="foto" type="button" [disabled]="!e.soportada" (click)="elegir(e)" [class.puesta]="e.foto?.publicada"
                      [attr.aria-label]="(e.foto ? 'Editar ' : 'Publicar ') + e.nombre"
                      [title]="e.soportada ? e.nombre : 'Formato no publicable: pásalo a JPG, PNG o WebP'">
                <img [src]="miniatura(e)" alt="" width="360" height="270" loading="lazy" decoding="async" (load)="lista($event)" (error)="lista($event)">
                <span class="marca">{{ e.foto ? (e.foto.publicada ? '✓ publicada' : 'oculta') : e.soportada ? 'publicar' : 'no soportada' }}</span>
              </button>
            }
          </li>
        } @empty { @if (!fallo()) { <li class="vacio">Esta carpeta no tiene fotos.</li> } }
      </ul>
    </section>
    }
    @if (pestana() === 'web') {
    <section class="caja">
      <header class="caja__cab">
        <h3>Estado en vivo en la web</h3>
        <span class="sub">Lo que ve cualquiera que abra el portfolio. Todo empieza apagado.</span>
      </header>
      <label class="interruptor">
        <input type="checkbox" [checked]="publico().estado" (change)="ajustarPublico('estado', $event)">
        <span><strong>Mostrar «equipo de casa · en línea»</strong><small>Solo dice si el equipo está encendido.</small></span>
      </label>
      <label class="interruptor" [class.apagado]="!publico().estado">
        <input type="checkbox" [checked]="publico().cpu" [disabled]="!publico().estado" (change)="ajustarPublico('cpu', $event)">
        <span><strong>Enseñar también la carga del equipo</strong><small>CPU y memoria en directo, redondeadas, con una mini gráfica y las horas encendido. Nunca el nombre del equipo, discos ni red.</small></span>
      </label>
    </section>
    <section class="caja">
      <header class="caja__cab"><h3>Zona de mi casa</h3><span class="sub">Las fotos hechas dentro de este radio nunca salen en el globo</span></header>
      <div class="fila-buscar">
        <input class="campo__input" placeholder="Buscar mi ciudad…" [value]="consultaCasa()" (input)="consultaCasa.set($any($event.target).value)" (keydown.enter)="buscarCasa($event)">
        <button class="enlace" type="button" (click)="buscarCasa($event)">Buscar</button>
      </div>
      @for (s of sitiosCasa(); track s.lat + ',' + s.lon) {
        <button class="enlace sitio" type="button" (click)="elegirCasa(s)">{{ s.nombre }}{{ s.region ? ' · ' + s.region : '' }}</button>
      }
      <p class="sub">{{ casa().lat != null ? 'Centro: ' + casa().lat!.toFixed(2) + ', ' + casa().lon!.toFixed(2) : 'Sin zona excluida todavía' }}</p>
      <label class="campo"><span class="campo__etiqueta">Radio: {{ casa().radioKm }} km</span>
        <input type="range" min="0" max="100" step="5" [value]="casa().radioKm" (change)="radioCasa($any($event.target).value)"></label>
    </section>
    }

    <dialog #dialogo class="dlg" (close)="actual.set(null)">
      @if (actual(); as a) {
        <form class="formulario" (submit)="guardar($event)" novalidate>
          <h3>{{ a.id ? 'Editar foto' : 'Publicar foto' }}</h3>
          <label class="campo"><span class="campo__etiqueta">Título</span>
            <input class="campo__input" maxlength="80" [value]="a.titulo" (input)="cambia('titulo', $event)"></label>
          <label class="campo"><span class="campo__etiqueta">Descripción (se enseña bajo la foto)</span>
            <textarea class="campo__input" rows="3" maxlength="400" [value]="a.descripcion" (input)="cambia('descripcion', $event)"></textarea></label>
          <label class="campo"><span class="campo__etiqueta">Texto alternativo (para lectores de pantalla)</span>
            <input class="campo__input" maxlength="200" [value]="a.alt" (input)="cambia('alt', $event)"></label>
          <label class="campo"><span class="campo__etiqueta">Categoría</span>
            <input class="campo__input" list="categorias" maxlength="30" [value]="a.categoria" (input)="cambia('categoria', $event)">
            <datalist id="categorias">@for (c of categorias; track c) { <option [value]="c"></option> }</datalist></label>
          @if (a.id) {
            <label class="interruptor"><input type="checkbox" [checked]="a.publicada" (change)="a.publicada = $any($event.target).checked">
              <span><strong>Visible en la web</strong></span></label>
            <fieldset class="lugar">
              <legend>Globo 3D del portfolio</legend>
              <label class="interruptor"><input type="checkbox" [checked]="a.enGlobo" (change)="a.enGlobo = $any($event.target).checked">
                <span><strong>Mostrar en el globo</strong>
                  <small>La web solo recibe la posición redondeada (~10 km) y el nombre del sitio; nunca el GPS exacto ni el de tu casa.</small></span></label>
              <div class="fila-buscar">
                <input class="campo__input" placeholder="Buscar una ciudad o sitio…" [value]="consulta()" (input)="consulta.set($any($event.target).value)" (keydown.enter)="buscar($event)">
                <button class="enlace" type="button" (click)="buscar($event)">Buscar</button>
                <button class="enlace" type="button" (click)="usarGps()">Usar el GPS del archivo</button>
              </div>
              @for (s of sitios(); track s.lat + ',' + s.lon) {
                <button class="enlace sitio" type="button" (click)="elegirSitio(s)">{{ s.nombre }}{{ s.region ? ' · ' + s.region : '' }}</button>
              }
              @if (a.lat != null && a.lon != null) { <p class="sub">{{ a.lugar || 'Sin nombre' }} · {{ a.lat!.toFixed(2) }}, {{ a.lon!.toFixed(2) }}</p> }
              <label class="campo"><span class="campo__etiqueta">Nombre que se enseña</span>
                <input class="campo__input" maxlength="80" [value]="a.lugar ?? ''" (input)="a.lugar = $any($event.target).value"></label>
            </fieldset>
          }
          @if (errorForm()) { <p class="error">{{ errorForm() }}</p> }
          <footer class="pie">
            @if (a.id) { <button class="enlace peligro" type="button" (click)="quitar(a)">Quitar de la galería</button> }
            <button class="enlace" type="button" (click)="cerrar()">Cancelar</button>
            <button class="button" type="submit" [disabled]="guardando()">{{ guardando() ? 'Guardando…' : 'Guardar' }}</button>
          </footer>
        </form>
      }
    </dialog>
  `,
  styleUrl: './galeria.css',
})
export class Galeria implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly dialogo = viewChild.required<ElementRef<HTMLDialogElement>>('dialogo');

  protected readonly categorias = CATEGORIAS;
  protected readonly estado = signal<Estado | null>(null);
  protected readonly publico = signal({ estado: false, cpu: false });
  protected readonly pestana = signal<'fotos' | 'web'>('fotos');
  protected readonly consulta = signal('');
  protected readonly sitios = signal<Sitio[]>([]);
  protected readonly consultaCasa = signal('');
  protected readonly sitiosCasa = signal<Sitio[]>([]);
  protected readonly casa = signal<{ lat: number | null; lon: number | null; radioKm: number }>({ lat: null, lon: null, radioKm: 0 });
  protected readonly fotos = signal<Foto[]>([]);
  protected readonly ruta = signal('');
  protected readonly elementos = signal<Elemento[]>([]);
  protected readonly fallo = signal(false);
  protected readonly cargando = signal(true);
  protected readonly esqueletos = [1, 2, 3, 4, 5, 6, 7, 8];
  protected readonly actual = signal<(Partial<Foto> & { ruta: string }) | null>(null);
  protected readonly guardando = signal(false);
  protected readonly errorForm = signal('');
  protected readonly migas = computed(() => {
    const partes = this.ruta().split('/').filter(Boolean);
    return partes.map((nombre, i) => ({ nombre, ruta: partes.slice(0, i + 1).join('/') }));
  });

  async ngOnInit(): Promise<void> {
    await Promise.all([this.cargar(), this.abrir('')]);
  }

  protected codificar(r: string): string { return encodeURIComponent(r); }

  // la rejilla pide miniaturas de 360 px (con la versión en la dirección, así el navegador las guarda para siempre) y no las fotos enteras
  protected miniatura(e: Elemento): string {
    return e.soportada && e.v
      ? `/api/galeria/carpeta/mini?ruta=${encodeURIComponent(e.ruta)}&v=${e.v}`
      : `/api/archivos/archivo?raiz=imagenes&ruta=${encodeURIComponent(e.ruta)}&ver=true`;
  }

  // mientras llega la miniatura se ve un barrido de luz; al llegar (o fallar) la foto aparece con un fundido
  protected lista(ev: Event): void { (ev.target as HTMLElement).parentElement?.classList.add('lista'); }

  private async cargar(): Promise<void> {
    const [e, f, p] = await Promise.all([
      firstValueFrom(this.http.get<Estado>('/api/galeria/estado')),
      firstValueFrom(this.http.get<Foto[]>('/api/galeria/fotos')),
      firstValueFrom(this.http.get<{ estado: boolean; cpu: boolean }>('/api/publico-ajustes')),
    ]);
    this.estado.set(e);
    this.fotos.set(f);
    this.publico.set(p);
    firstValueFrom(this.http.get<{ lat: number | null; lon: number | null; radioKm: number }>('/api/galeria/globo')).then((c) => this.casa.set(c)).catch(() => {});
  }

  private async sitiosDe(q: string): Promise<Sitio[]> {
    if (q.trim().length < 2) return [];
    try { return await firstValueFrom(this.http.get<Sitio[]>(`/api/tiempo/buscar?q=${encodeURIComponent(q.trim())}`)); } catch { return []; }
  }

  protected async buscar(e: Event): Promise<void> { e.preventDefault(); this.sitios.set(await this.sitiosDe(this.consulta())); }
  protected async buscarCasa(e: Event): Promise<void> { e.preventDefault(); this.sitiosCasa.set(await this.sitiosDe(this.consultaCasa())); }

  protected elegirSitio(s: Sitio): void {
    const a = this.actual();
    if (!a) return;
    // se guarda la ciudad (no un punto exacto): da igual dónde estuvieras dentro de ella
    this.actual.set({ ...a, lat: s.lat, lon: s.lon, lugar: s.nombre, enGlobo: true });
    this.sitios.set([]);
  }

  // el GPS que lleva el archivo (solo lo ves tú); pide el nombre del sitio a mano
  protected async usarGps(): Promise<void> {
    const a = this.actual();
    if (!a?.id) return;
    const r = await firstValueFrom(this.http.get<{ lat: number; lon: number } | null>(`/api/galeria/fotos/${a.id}/gps`));
    if (!r) { this.errorForm.set('Esta foto no lleva GPS: busca el sitio a mano.'); return; }
    this.errorForm.set('');
    this.actual.set({ ...a, lat: r.lat, lon: r.lon, enGlobo: true });
  }

  protected async elegirCasa(s: Sitio): Promise<void> {
    this.casa.set({ ...this.casa(), lat: s.lat, lon: s.lon, radioKm: this.casa().radioKm || 25 });
    this.sitiosCasa.set([]);
    await this.guardarCasa();
  }

  protected async radioCasa(v: string): Promise<void> {
    this.casa.set({ ...this.casa(), radioKm: +v });
    await this.guardarCasa();
  }

  private guardarCasa(): Promise<unknown> { return firstValueFrom(this.http.put('/api/galeria/globo', this.casa())); }

  protected async ajustarPublico(campo: 'estado' | 'cpu', e: Event): Promise<void> {
    const nuevo = { ...this.publico(), [campo]: (e.target as HTMLInputElement).checked };
    if (!nuevo.estado) nuevo.cpu = false;
    this.publico.set(nuevo);
    await firstValueFrom(this.http.put('/api/publico-ajustes', nuevo));
  }

  protected async abrir(ruta: string): Promise<void> {
    this.cargando.set(true);
    try {
      const r = await firstValueFrom(this.http.get<{ ruta: string; elementos: Elemento[] }>(`/api/galeria/carpeta?ruta=${encodeURIComponent(ruta)}`));
      this.ruta.set(r.ruta);
      this.elementos.set(r.elementos);
      this.fallo.set(false);
    } catch {
      this.fallo.set(true);
      this.elementos.set([]);
    } finally { this.cargando.set(false); }
  }

  protected async activar(e: Event): Promise<void> {
    const activa = (e.target as HTMLInputElement).checked;
    await firstValueFrom(this.http.put('/api/galeria/estado', { activa }));
    await this.cargar();
  }

  protected elegir(e: Elemento): void {
    const f = e.foto ? this.fotos().find((x) => x.id === e.foto!.id) : undefined;
    this.errorForm.set('');
    this.actual.set(f ? { ...f } : { ruta: e.ruta, titulo: e.nombre.replace(/\.[^.]+$/, ''), descripcion: '', alt: '', categoria: '', publicada: true });
    this.dialogo().nativeElement.showModal();
  }

  protected editar(f: Foto): void {
    this.errorForm.set('');
    this.actual.set({ ...f });
    this.dialogo().nativeElement.showModal();
  }

  protected cambia(campo: 'titulo' | 'descripcion' | 'alt' | 'categoria', e: Event): void {
    const a = this.actual();
    if (a) a[campo] = (e.target as HTMLInputElement).value;
  }

  protected cerrar(): void { this.dialogo().nativeElement.close(); }

  protected async guardar(ev: Event): Promise<void> {
    ev.preventDefault();
    const a = this.actual();
    if (!a || this.guardando()) return;
    this.guardando.set(true);
    this.errorForm.set('');
    try {
      if (a.id) {
        await firstValueFrom(this.http.put(`/api/galeria/fotos/${a.id}`, { titulo: a.titulo, descripcion: a.descripcion, alt: a.alt, categoria: a.categoria, publicada: a.publicada, enGlobo: !!a.enGlobo, lat: a.lat ?? null, lon: a.lon ?? null, lugar: a.lugar ?? '' }));
      } else {
        await firstValueFrom(this.http.post('/api/galeria/fotos', { ruta: a.ruta, titulo: a.titulo, descripcion: a.descripcion, alt: a.alt, categoria: a.categoria }));
      }
      this.cerrar();
      await Promise.all([this.cargar(), this.abrir(this.ruta())]);
    } catch (e) {
      const d = (e as { error?: { errors?: Record<string, string[]>; detail?: string } }).error;
      this.errorForm.set(Object.values(d?.errors ?? {})[0]?.[0] ?? d?.detail ?? 'No se pudo guardar.');
    } finally {
      this.guardando.set(false);
    }
  }

  protected async quitar(a: Partial<Foto>): Promise<void> {
    if (!a.id || !confirm('¿Quitar esta foto de la galería? El archivo original no se toca.')) return;
    await firstValueFrom(this.http.delete(`/api/galeria/fotos/${a.id}`));
    this.cerrar();
    await Promise.all([this.cargar(), this.abrir(this.ruta())]);
  }

  protected async mover(i: number, delta: number): Promise<void> {
    const l = [...this.fotos()];
    const j = i + delta;
    if (j < 0 || j >= l.length) return;
    [l[i], l[j]] = [l[j], l[i]];
    this.fotos.set(l);
    await firstValueFrom(this.http.put('/api/galeria/orden', { ids: l.map((f) => f.id) }));
  }
}
