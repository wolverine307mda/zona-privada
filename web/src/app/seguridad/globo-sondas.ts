import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, OnInit, afterNextRender, computed, effect, inject, signal, viewChild } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type * as THREE from 'three';
import { resolverColor } from '../core/color-css';
import { CieloConfig } from '../core/cielo-config';
import { SistemaService } from '../inicio/sistema.service';
import { puntosDeTierra } from './tierra';

export interface Punto { lat: number; lon: number; pais: string; ciudad: string; sondas: number; ips: number; ultima: string; rutas: string[]; }
interface AccesoGeo { id: number; fecha: string; exito: boolean; lugar: string | null; lat: number | null; lon: number | null; motivo: string; }
type TipoRayo = 'fallo' | 'sonda' | 'ok';
interface Resumen { dias: number; total: number; ips: number; sinSitio: number; geoIp: boolean; puntos: Punto[]; paises: { pais: string; sondas: number }[]; }

type Three = typeof import('three');

// Latitud y longitud → un punto sobre la esfera (Y arriba; longitud 0 mirando a +Z)
export function sobreEsfera(lat: number, lon: number, radio: number): [number, number, number] {
  const f = (lat * Math.PI) / 180, l = (lon * Math.PI) / 180;
  return [radio * Math.cos(f) * Math.sin(l), radio * Math.sin(f), radio * Math.cos(f) * Math.cos(l)];
}
// Un arco de círculo máximo entre dos sitios, algo levantado sobre la esfera en medio (como la trayectoria de un rayo): `n + 1` puntos de `desde` a `hasta`
export function arcoGrande(lat1: number, lon1: number, lat2: number, lon2: number, n: number, radio: number, elevacion: number): [number, number, number][] {
  const a = sobreEsfera(lat1, lon1, 1), b = sobreEsfera(lat2, lon2, 1);
  const dot = Math.min(1, Math.max(-1, a[0] * b[0] + a[1] * b[1] + a[2] * b[2]));
  const omega = Math.acos(dot), sen = Math.sin(omega);
  const salida: [number, number, number][] = [];
  for (let i = 0; i <= n; i++) {
    const t = i / n;
    const ka = sen < 1e-6 ? 1 - t : Math.sin((1 - t) * omega) / sen, kb = sen < 1e-6 ? t : Math.sin(t * omega) / sen;
    const v: [number, number, number] = [ka * a[0] + kb * b[0], ka * a[1] + kb * b[1], ka * a[2] + kb * b[2]];
    const largo = Math.hypot(...v) || 1;
    const k = (radio * (1 + elevacion * Math.sin(Math.PI * t))) / largo;
    salida.push([v[0] * k, v[1] * k, v[2] * k]);
  }
  return salida;
}

// el tamaño del punto crece con la raíz de las sondas, acotado
export const tamanoPunto = (sondas: number, maximo: number): number => 0.025 + 0.06 * Math.sqrt(sondas / Math.max(1, maximo));

// El globo de las sondas: dónde están (según su IP) los que han pedido rutas como /.env o /wp-login.php en los últimos días.
@Component({
  selector: 'app-globo-sondas',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="caja">
      <header class="caja__cab"><h3>Quién llama a la puerta</h3>
        <div class="selector" role="group" aria-label="Periodo">
          @for (d of periodos; track d) { <button type="button" [attr.aria-pressed]="dias() === d" (click)="cambiar(d)">{{ d }} días</button> }
        </div>
      </header>
      <div class="radar">
        <label class="vivo"><input type="checkbox" role="switch" [checked]="enVivo()" (change)="alternarVivo($any($event.target).checked)" aria-label="Radar en vivo"><span class="vivo__punto" [class.vivo__punto--on]="enVivo()" aria-hidden="true"></span> Radar en vivo</label>
        <button type="button" class="prueba" (click)="probar()">Probar un rayo</button>
        <span class="ultimo" role="status" aria-live="polite">{{ ultimoEvento() }}</span>
      </div>
      <div class="escena">
        <canvas #lienzo role="img" [attr.aria-label]="'Globo con ' + (datos()?.puntos?.length ?? 0) + ' orígenes de sondas'"></canvas>
        @if (fallo()) { <div class="fallo" role="status"><p>No se pudo dibujar el globo.</p><button type="button" (click)="arrancar()">Reintentar</button></div> }
      </div>
      @if (datos(); as d) {
        <p class="resumen"><b>{{ d.total }}</b> sondas de <b>{{ d.ips }}</b> IP en {{ d.dias }} días{{ d.sinSitio ? ' · ' + d.sinSitio + ' sin ubicar' : '' }}.</p>
        @if (!d.geoIp) { <p class="nota">Aún no tengo la base de geolocalización (se baja sola, unos minutos después de arrancar y una vez al mes): las sondas se guardan, pero sin punto en el mapa.</p> }
        @if (d.paises.length) { <ul class="paises">@for (p of d.paises; track p.pais) { <li><b>{{ p.pais }}</b> {{ p.sondas }}</li> }</ul> }
        @if (elegido(); as p) { <p class="detalle"><b>{{ p.ciudad || 'Sin ciudad' }}{{ p.pais ? ' (' + p.pais + ')' : '' }}</b> · {{ p.sondas }} sondas de {{ p.ips }} IP · buscaban {{ p.rutas.join(', ') }}</p> }
      }
    </section>
  `,
  styles: `
    :host { display: block; }
    .selector { display: inline-flex; gap: 4px; }
    .selector button { border: 1px solid var(--vidrio-borde); background: none; color: var(--soft); border-radius: 999px; padding: 6px 12px; font: 500 .78rem/1 var(--font-text); min-height: 32px; cursor: pointer; }
    .selector button[aria-pressed='true'] { background: var(--brand); border-color: var(--brand); color: var(--on-brand); }
    .radar { display: flex; flex-wrap: wrap; align-items: center; gap: 8px 14px; margin: 8px 0 4px; font-size: .82rem; color: var(--soft); }
    .vivo { display: inline-flex; align-items: center; gap: 8px; min-height: 36px; cursor: pointer; }
    .vivo input { position: absolute; opacity: 0; width: 1px; height: 1px; }
    .vivo__punto { width: 10px; height: 10px; border-radius: 50%; background: var(--soft); }
    .vivo__punto--on { background: #ff3b30; box-shadow: 0 0 0 4px rgb(255 59 48 / .25); animation: latido 1.6s ease-in-out infinite; }
    .vivo input:focus-visible + .vivo__punto { outline: 2px solid var(--brand); outline-offset: 3px; }
    .prueba { border: 1px solid var(--vidrio-borde); background: none; color: inherit; border-radius: 999px; padding: 0 12px; min-height: 36px; font: inherit; cursor: pointer; }
    .ultimo { flex: 1 1 100%; min-height: 1.2em; }
    @keyframes latido { 50% { box-shadow: 0 0 0 8px rgb(255 59 48 / 0); } }
    @media (prefers-reduced-motion: reduce) { .vivo__punto--on { animation: none; } }
    .escena { position: relative; height: clamp(260px, 52vw, 400px); }
    canvas { display: block; width: 100%; height: 100%; touch-action: pan-y; cursor: grab; }
    .fallo { position: absolute; inset: 0; display: grid; place-content: center; justify-items: center; gap: 8px; color: var(--soft); }
    .fallo button { border: 1px solid var(--vidrio-borde); border-radius: 999px; background: none; color: var(--acento); padding: 10px 16px; min-height: 40px; cursor: pointer; }
    .resumen { margin: 8px 0 0; }
    .nota, .detalle { margin: 6px 0 0; color: var(--soft); font-size: .8rem; }
    .paises { display: flex; flex-wrap: wrap; gap: 6px 14px; margin: 8px 0 0; padding: 0; list-style: none; font-size: .82rem; color: var(--soft); }
  `,
})
export class GloboSondas implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly lienzo = viewChild.required<ElementRef<HTMLCanvasElement>>('lienzo');
  protected readonly periodos = [7, 30, 90];
  protected readonly dias = signal(30);
  protected readonly datos = signal<Resumen | null>(null);
  protected readonly fallo = signal(false);
  protected readonly elegido = signal<Punto | null>(null);
  private readonly puntos = computed(() => this.datos()?.puntos ?? []);
  private pintar: ((p: Punto[]) => void) | null = null;
  private cierre: (() => void) | null = null;
  private vivo = true;
  private readonly casa = inject(CieloConfig);
  private readonly sistema = inject(SistemaService);
  protected readonly enVivo = signal(true);
  protected readonly ultimoEvento = signal('Aquí verás un rayo cada vez que alguien llame a la puerta.');
  private lanzar: ((lat: number, lon: number, tipo: TipoRayo) => void) | null = null;
  private temporizador: ReturnType<typeof setInterval> | undefined;
  private accesosVistos = new Set<number>(); private primerosAccesos = true;
  private sondasVistas = new Map<string, number>(); private primerasSondas = true;

  constructor() {
    inject(DestroyRef).onDestroy(() => { this.vivo = false; clearInterval(this.temporizador); this.cierre?.(); this.cierre = null; });
    afterNextRender(() => this.arrancar());
    effect(() => { const p = this.puntos(); this.pintar?.(p); });
    // el directo: en cuanto la API cuenta un acceso, su rayo sale sin esperar al repaso (el repaso lo reconoce por su número y no lo repite)
    effect(() => {
      const a = this.sistema.accesoNuevo();
      if (!a || !this.enVivo() || this.accesosVistos.has(a.id)) return;
      this.accesosVistos.add(a.id);
      this.mostrarAcceso(a);
    });
  }

  ngOnInit(): void { void this.leer(); void this.vigilar(); this.temporizador = setInterval(() => void this.vigilar(), 12_000); }

  protected alternarVivo(v: boolean): void { this.enVivo.set(v); }

  // un rayo de mentira desde una ciudad al azar, para ver cómo es
  protected probar(): void {
    const ciudades: [string, number, number][] = [['Moscú', 55.75, 37.62], ['Pekín', 39.9, 116.4], ['São Paulo', -23.55, -46.63], ['Lagos', 6.52, 3.38], ['Sídney', -33.87, 151.2], ['Nueva York', 40.71, -74.0]];
    const [nombre, lat, lon] = ciudades[Math.floor(Math.random() * ciudades.length)];
    this.lanzar?.(lat, lon, 'fallo');
    this.ultimoEvento.set(`Prueba: un intento falso desde ${nombre} (no se ha guardado nada).`);
  }

  private mostrarAcceso(a: { exito: boolean; lugar: string | null; lat: number | null; lon: number | null; motivo: string }): void {
    const donde = a.lugar ?? 'un sitio sin ubicar';
    this.ultimoEvento.set(a.exito ? `Entrada correcta desde ${donde}.` : `Intento de acceso fallido desde ${donde}${a.motivo ? ' (' + a.motivo + ')' : ''}.`);
    if (a.lat !== null && a.lon !== null) this.lanzar?.(a.lat, a.lon, a.exito ? 'ok' : 'fallo');
  }

  // Cada 12 s mira si hay accesos o sondas nuevos desde la última vez y lanza un rayo por cada uno. La primera vuelta solo apunta lo que ya había.
  private async vigilar(): Promise<void> {
    if (!this.vivo || !this.enVivo() || document.hidden) return;
    const [accesos, sondas] = await Promise.all([
      firstValueFrom(this.http.get<AccesoGeo[]>('/api/seguridad/accesos')).catch(() => null),
      firstValueFrom(this.http.get<Resumen>('/api/seguridad/sondas?dias=1')).catch(() => null),
    ]);
    if (accesos) {
      for (const a of [...accesos].sort((x, y) => x.id - y.id)) {
        if (this.accesosVistos.has(a.id)) continue;
        this.accesosVistos.add(a.id);
        if (this.primerosAccesos) continue;
        this.mostrarAcceso(a);
      }
      this.primerosAccesos = false;
    }
    if (sondas) {
      for (const p of sondas.puntos) {
        const clave = `${p.lat},${p.lon}`, antes = this.sondasVistas.get(clave) ?? 0;
        this.sondasVistas.set(clave, p.sondas);
        if (this.primerasSondas || p.sondas <= antes) continue;
        this.ultimoEvento.set(`Una sonda desde ${p.ciudad || p.pais || 'un sitio sin ubicar'} busca ${p.rutas[0] ?? 'rutas conocidas'}.`);
        for (let i = 0; i < Math.min(3, p.sondas - antes); i++) setTimeout(() => this.lanzar?.(p.lat, p.lon, 'sonda'), i * 450);
      }
      this.primerasSondas = false;
    }
  }

  protected cambiar(d: number): void { this.dias.set(d); this.elegido.set(null); void this.leer(); }

  private async leer(): Promise<void> {
    try { this.datos.set(await firstValueFrom(this.http.get<Resumen>(`/api/seguridad/sondas?dias=${this.dias()}`))); } catch { this.datos.set(null); }
  }

  protected arrancar(): void {
    this.fallo.set(false);
    import('three')
      .then((THREE) => { if (!this.vivo) return; this.cierre?.(); this.cierre = this.montar(THREE, this.lienzo().nativeElement); })
      .catch(() => { if (this.vivo) this.fallo.set(true); });
  }

  private montar(THREE: Three, canvas: HTMLCanvasElement): () => void {
    const reducido = matchMedia('(prefers-reduced-motion: reduce)').matches;
    const renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true });
    renderer.setClearColor(0x000000, 0);
    const scene = new THREE.Scene();
    const camera = new THREE.PerspectiveCamera(38, 1, 0.1, 50);
    camera.position.set(0, 0, 4.6);
    const css = getComputedStyle(document.documentElement);
    const marca = new THREE.Color(resolverColor(css.getPropertyValue('--brand'), '#ff7a1f'));

    const globo = new THREE.Group(); scene.add(globo);
    // día y noche como las maquetas: los colores del globo siguen el modo de la app
    const paleta = () => document.documentElement.dataset['mode'] === 'light'
      ? { bola: 0xe4ebf8, rejilla: 0x8d9cc4, tierra: 0x3a4f8c, opBola: 0.95, opRejilla: 0.55 }
      : { bola: 0x0b1020, rejilla: 0x4a5a82, tierra: 0x9fb3e6, opBola: 0.85, opRejilla: 0.45 };
    const matBola = new THREE.MeshBasicMaterial({ color: 0x0b1020, transparent: true, opacity: 0.85 });
    const bola = new THREE.Mesh(new THREE.SphereGeometry(1.5, 48, 32), matBola);
    bola.renderOrder = -1;   // primero la bola: así tapa los puntos del otro lado
    globo.add(bola);
    // retícula de paralelos y meridianos
    const lineas: number[] = [];
    for (let lat = -60; lat <= 60; lat += 30) for (let lon = 0; lon < 360; lon += 6) { lineas.push(...sobreEsfera(lat, lon, 1.505), ...sobreEsfera(lat, lon + 6, 1.505)); }
    for (let lon = 0; lon < 360; lon += 30) for (let lat = -90; lat < 90; lat += 6) { lineas.push(...sobreEsfera(lat, lon, 1.505), ...sobreEsfera(lat + 6, lon, 1.505)); }
    const geo = new THREE.BufferGeometry(); geo.setAttribute('position', new THREE.Float32BufferAttribute(lineas, 3));
    const matRejilla = new THREE.LineBasicMaterial({ color: 0x4a5a82, transparent: true, opacity: 0.45 });
    globo.add(new THREE.LineSegments(geo, matRejilla));
    // los continentes, como puntitos
    const tierra = puntosDeTierra(3).flatMap(([lat, lon]) => sobreEsfera(lat, lon, 1.51));
    const geoTierra = new THREE.BufferGeometry(); geoTierra.setAttribute('position', new THREE.Float32BufferAttribute(tierra, 3));
    const matTierra = new THREE.PointsMaterial({ color: 0x9fb3e6, size: 0.05, sizeAttenuation: true, transparent: true, opacity: 0.9 });
    globo.add(new THREE.Points(geoTierra, matTierra));
    const aplicarModo = () => {
      const c = paleta();
      matBola.color.set(c.bola); matBola.opacity = c.opBola; matRejilla.color.set(c.rejilla); matRejilla.opacity = c.opRejilla; matTierra.color.set(c.tierra);
    };
    aplicarModo();
    const observadorModo = new MutationObserver(() => { aplicarModo(); if (reducido) dibujar(0); });
    observadorModo.observe(document.documentElement, { attributes: true, attributeFilter: ['data-mode'] });
    globo.add(new THREE.Mesh(new THREE.SphereGeometry(1.62, 32, 24), new THREE.MeshBasicMaterial({ color: marca, transparent: true, opacity: 0.06, side: THREE.BackSide })));

    const marcas = new THREE.Group(); globo.add(marcas);
    const geoPunto = new THREE.SphereGeometry(1, 12, 10);
    const matPunto = new THREE.MeshBasicMaterial({ color: 0xff4d4d });
    const matHalo = new THREE.MeshBasicMaterial({ color: 0xff4d4d, transparent: true, opacity: 0.25 });
    let actuales: { malla: THREE.Mesh; punto: Punto }[] = [];
    const pintarPuntos = (ps: Punto[]) => {
      marcas.clear(); actuales = [];
      const max = Math.max(1, ...ps.map((p) => p.sondas));
      for (const p of ps) {
        const t = tamanoPunto(p.sondas, max), pos = sobreEsfera(p.lat, p.lon, 1.52);
        const m = new THREE.Mesh(geoPunto, matPunto); m.scale.setScalar(t); m.position.set(...pos);
        const h = new THREE.Mesh(geoPunto, matHalo); h.scale.setScalar(t * 2.2); h.position.set(...pos);
        marcas.add(m, h); actuales.push({ malla: m, punto: p });
      }
    };
    this.pintar = pintarPuntos; pintarPuntos(this.puntos());

    // tu casa (verde) y el radar: cada intento o sonda nuevo lanza un rayo desde su origen hasta aquí, con un anillo en cada extremo
    const hogar = this.casa.lugar();
    const posCasa = sobreEsfera(hogar.lat, hogar.lon, 1.53);
    const casaMalla = new THREE.Mesh(geoPunto, new THREE.MeshBasicMaterial({ color: 0x34c759 })); casaMalla.scale.setScalar(0.045); casaMalla.position.set(...posCasa);
    const casaHalo = new THREE.Mesh(geoPunto, new THREE.MeshBasicMaterial({ color: 0x34c759, transparent: true, opacity: 0.22 })); casaHalo.scale.setScalar(0.1); casaHalo.position.set(...posCasa);
    globo.add(casaMalla, casaHalo);
    const colorRayo: Record<TipoRayo, number> = { fallo: 0xff3b30, sonda: 0xffa21f, ok: 0x34c759 };
    const rayos: { linea: THREE.Line; anilloOrigen: THREE.Mesh; anilloCasa: THREE.Mesh; edad: number; n: number }[] = [];
    const geoAnillo = new THREE.RingGeometry(0.9, 1, 32);
    const colocarAnillo = (m: THREE.Mesh, pos: [number, number, number]) => { m.position.set(...pos); m.lookAt(0, 0, 0); m.rotateY(Math.PI); };
    this.lanzar = (lat, lon, tipo) => {
      if (rayos.length > 24) return;
      const pts = arcoGrande(lat, lon, hogar.lat, hogar.lon, 48, 1.52, 0.35);
      const geo = new THREE.BufferGeometry(); geo.setAttribute('position', new THREE.Float32BufferAttribute(pts.flat(), 3));
      const c = colorRayo[tipo];
      const linea = new THREE.Line(geo, new THREE.LineBasicMaterial({ color: c, transparent: true, opacity: 1 }));
      linea.geometry.setDrawRange(0, reducido ? pts.length : 2);
      const a1 = new THREE.Mesh(geoAnillo, new THREE.MeshBasicMaterial({ color: c, transparent: true, opacity: 0, side: THREE.DoubleSide }));
      const a2 = new THREE.Mesh(geoAnillo, new THREE.MeshBasicMaterial({ color: c, transparent: true, opacity: 0, side: THREE.DoubleSide }));
      colocarAnillo(a1, sobreEsfera(lat, lon, 1.525)); colocarAnillo(a2, posCasa);
      globo.add(linea, a1, a2);
      const medio = pts[Math.floor(pts.length / 2)];
      mirarY = -Math.atan2(medio[0], medio[2]); mirarX = Math.max(-0.9, Math.min(0.9, Math.asin(medio[1] / Math.hypot(...medio)) * 0.7));
      rayos.push({ linea, anilloOrigen: a1, anilloCasa: a2, edad: reducido ? 1.2 : 0, n: pts.length });
      if (reducido) dibujar(0);
    };
    const quitarRayo = (r: (typeof rayos)[number]) => {
      globo.remove(r.linea, r.anilloOrigen, r.anilloCasa);
      for (const o of [r.linea, r.anilloOrigen, r.anilloCasa]) { (o.material as THREE.Material).dispose(); }
      r.linea.geometry.dispose();
    };
    // 0–1,1 s el rayo avanza; luego se apaga en 1,6 s; los anillos nacen al salir y al llegar
    const animarRayos = (dt: number) => {
      for (let i = rayos.length - 1; i >= 0; i--) {
        const r = rayos[i]; r.edad += dt;
        const avance = Math.min(1, r.edad / 1.1), apagado = Math.max(0, 1 - Math.max(0, r.edad - 1.1) / 1.6);
        r.linea.geometry.setDrawRange(0, Math.max(2, Math.ceil(avance * r.n)));
        (r.linea.material as THREE.LineBasicMaterial).opacity = apagado;
        const pulso = (t: number) => Math.min(1, Math.max(0, t));
        const o = pulso(r.edad / 0.9), c = pulso((r.edad - 1.1) / 0.9);
        r.anilloOrigen.scale.setScalar(0.03 + o * 0.12); (r.anilloOrigen.material as THREE.MeshBasicMaterial).opacity = o > 0 && o < 1 ? 0.9 * (1 - o) : 0;
        r.anilloCasa.scale.setScalar(0.03 + c * 0.16); (r.anilloCasa.material as THREE.MeshBasicMaterial).opacity = c > 0 && c < 1 ? 0.9 * (1 - c) : 0;
        if (r.edad > 2.8) { quitarRayo(r); rayos.splice(i, 1); }
      }
    };

    // girar arrastrando; se toca un punto para ver su detalle; solo, gira despacio
    let rotY = 0.6, rotX = 0.25, arrastrando = false, movido = 0, ux = 0, uy = 0;
    // al lanzar un rayo, el globo gira suavemente para enseñar el arco entero
    let mirarY: number | null = null, mirarX = 0;
    const abajo = (e: PointerEvent) => { arrastrando = true; mirarY = null; movido = 0; ux = e.clientX; uy = e.clientY; canvas.setPointerCapture(e.pointerId); };
    const mueve = (e: PointerEvent) => { if (!arrastrando) return; rotY += (e.clientX - ux) * 0.008; rotX = Math.max(-1.2, Math.min(1.2, rotX + (e.clientY - uy) * 0.006)); movido += Math.abs(e.clientX - ux) + Math.abs(e.clientY - uy); ux = e.clientX; uy = e.clientY; if (reducido) dibujar(0); };
    const raycaster = new THREE.Raycaster(), ndc = new THREE.Vector2();
    const arriba = (e: PointerEvent) => {
      arrastrando = false;
      if (movido > 6) return;
      const r = canvas.getBoundingClientRect();
      ndc.set(((e.clientX - r.left) / r.width) * 2 - 1, -((e.clientY - r.top) / r.height) * 2 + 1);
      raycaster.setFromCamera(ndc, camera);
      const golpe = raycaster.intersectObjects(actuales.map((a) => a.malla), false)[0];
      this.elegido.set(golpe ? actuales.find((a) => a.malla === golpe.object)?.punto ?? null : null);
    };
    canvas.addEventListener('pointerdown', abajo); canvas.addEventListener('pointermove', mueve); canvas.addEventListener('pointerup', arriba); canvas.addEventListener('pointercancel', () => { arrastrando = false; });

    let cerrado = false, reajustes = 0;
    const ajustar = () => {
      if (cerrado) return;
      const w = canvas.clientWidth, h = canvas.clientHeight;
      if (!w || !h) { if (reajustes++ < 60) requestAnimationFrame(ajustar); return; }
      renderer.setPixelRatio(Math.min(devicePixelRatio, 2)); renderer.setSize(w, h, false);
      camera.aspect = w / h; camera.updateProjectionMatrix(); if (reducido) dibujar(0);
    };
    const observador = new ResizeObserver(ajustar); observador.observe(canvas);

    let t0 = performance.now(), marco = 0, visible = true;
    function dibujar(dt: number) {
      if (!arrastrando && !reducido) rotY += dt * 0.15;
      if (!reducido) animarRayos(dt);
      if (mirarY !== null && !arrastrando && !reducido) {
        const dif = Math.atan2(Math.sin(mirarY - rotY), Math.cos(mirarY - rotY));
        rotY += dif * Math.min(1, dt * 2.2); rotX += (mirarX - rotX) * Math.min(1, dt * 2.2);
        if (Math.abs(dif) < 0.01) mirarY = null;
      }
      globo.rotation.set(rotX, rotY, 0);
      renderer.render(scene, camera);
    }
    // el siguiente fotograma se pide antes de dibujar y un fallo se salta (si no, una excepción paraba la escena); dt nunca es negativo
    const bucle = (t: number) => { marco = requestAnimationFrame(bucle); const dt = Math.max(0, Math.min(0.05, (t - t0) / 1000)); t0 = t; if (visible && !document.hidden) { try { dibujar(dt); } catch { /* ese fotograma no se pinta */ } } };
    const io = new IntersectionObserver((e) => { visible = e[0]?.isIntersecting ?? true; }); io.observe(canvas);
    ajustar();
    if (reducido) dibujar(0); else marco = requestAnimationFrame(bucle);

    return () => {
      cerrado = true; this.pintar = null; this.lanzar = null; rayos.forEach(quitarRayo);
      cancelAnimationFrame(marco); observador.disconnect(); observadorModo.disconnect(); io.disconnect();
      canvas.removeEventListener('pointerdown', abajo); canvas.removeEventListener('pointermove', mueve); canvas.removeEventListener('pointerup', arriba);
      scene.traverse((o) => { const m = o as THREE.Mesh; m.geometry?.dispose?.(); const mt = m.material as THREE.Material | THREE.Material[] | undefined; (Array.isArray(mt) ? mt : mt ? [mt] : []).forEach((x) => x.dispose()); });
      renderer.dispose(); renderer.forceContextLoss();
    };
  }
}
