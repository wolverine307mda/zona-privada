import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, OnInit, afterNextRender, computed, effect, inject, signal, viewChild } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type * as THREE from 'three';

export interface Punto { lat: number; lon: number; pais: string; ciudad: string; sondas: number; ips: number; ultima: string; rutas: string[]; }
interface Resumen { dias: number; total: number; ips: number; sinSitio: number; geoIp: boolean; puntos: Punto[]; paises: { pais: string; sondas: number }[]; }

type Three = typeof import('three');

// Latitud y longitud → un punto sobre la esfera (Y arriba; longitud 0 mirando a +Z)
export function sobreEsfera(lat: number, lon: number, radio: number): [number, number, number] {
  const f = (lat * Math.PI) / 180, l = (lon * Math.PI) / 180;
  return [radio * Math.cos(f) * Math.sin(l), radio * Math.sin(f), radio * Math.cos(f) * Math.cos(l)];
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

  constructor() {
    inject(DestroyRef).onDestroy(() => { this.vivo = false; this.cierre?.(); this.cierre = null; });
    afterNextRender(() => this.arrancar());
    effect(() => { const p = this.puntos(); this.pintar?.(p); });
  }

  ngOnInit(): void { void this.leer(); }

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
    const marca = new THREE.Color(css.getPropertyValue('--brand').trim() || '#ff7a1f');

    const globo = new THREE.Group(); scene.add(globo);
    globo.add(new THREE.Mesh(new THREE.SphereGeometry(1.5, 48, 32), new THREE.MeshBasicMaterial({ color: 0x0b1020, transparent: true, opacity: 0.85 })));
    // retícula de paralelos y meridianos
    const lineas: number[] = [];
    for (let lat = -60; lat <= 60; lat += 30) for (let lon = 0; lon < 360; lon += 6) { lineas.push(...sobreEsfera(lat, lon, 1.505), ...sobreEsfera(lat, lon + 6, 1.505)); }
    for (let lon = 0; lon < 360; lon += 30) for (let lat = -90; lat < 90; lat += 6) { lineas.push(...sobreEsfera(lat, lon, 1.505), ...sobreEsfera(lat + 6, lon, 1.505)); }
    const geo = new THREE.BufferGeometry(); geo.setAttribute('position', new THREE.Float32BufferAttribute(lineas, 3));
    globo.add(new THREE.LineSegments(geo, new THREE.LineBasicMaterial({ color: 0x4a5a82, transparent: true, opacity: 0.45 })));
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

    // girar arrastrando; se toca un punto para ver su detalle; solo, gira despacio
    let rotY = 0.6, rotX = 0.25, arrastrando = false, movido = 0, ux = 0, uy = 0;
    const abajo = (e: PointerEvent) => { arrastrando = true; movido = 0; ux = e.clientX; uy = e.clientY; canvas.setPointerCapture(e.pointerId); };
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
      globo.rotation.set(rotX, rotY, 0);
      renderer.render(scene, camera);
    }
    const bucle = (t: number) => { const dt = Math.min(0.05, (t - t0) / 1000); t0 = t; if (visible && !document.hidden) dibujar(dt); marco = requestAnimationFrame(bucle); };
    const io = new IntersectionObserver((e) => { visible = e[0]?.isIntersecting ?? true; }); io.observe(canvas);
    ajustar();
    if (reducido) dibujar(0); else marco = requestAnimationFrame(bucle);

    return () => {
      cerrado = true; this.pintar = null;
      cancelAnimationFrame(marco); observador.disconnect(); io.disconnect();
      canvas.removeEventListener('pointerdown', abajo); canvas.removeEventListener('pointermove', mueve); canvas.removeEventListener('pointerup', arriba);
      scene.traverse((o) => { const m = o as THREE.Mesh; m.geometry?.dispose?.(); const mt = m.material as THREE.Material | THREE.Material[] | undefined; (Array.isArray(mt) ? mt : mt ? [mt] : []).forEach((x) => x.dispose()); });
      renderer.dispose(); renderer.forceContextLoss();
    };
  }
}
