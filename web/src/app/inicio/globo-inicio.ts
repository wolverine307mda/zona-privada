import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, ViewEncapsulation, afterNextRender, computed, inject, signal, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import type * as THREE from 'three';
import { CieloConfig } from '../core/cielo-config';
import { resolverColor } from '../core/color-css';
import { arcoGrande, sobreEsfera } from '../seguridad/globo-sondas';
import { puntosDeTierra, texturaTierra } from '../seguridad/tierra';

type Three = typeof import('three');
interface PuntoSonda { lat: number; lon: number; pais: string; ciudad: string; sondas: number; }
interface ResumenSondas { dias: number; total: number; ips: number; puntos: PuntoSonda[]; paises: { pais: string; sondas: number }[]; }
interface AccesoGeo { id: number; exito: boolean; lat: number | null; lon: number | null; }

// La portada de Inicio: el globo de puntos con los continentes rellenos, las sondas de la última semana y los intentos de entrada fallidos,
// con un arco de cada origen hasta tu casa. Arrastra para girarlo; se descubre con un barrido al cargar.
@Component({
  selector: 'app-globo-inicio',
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  imports: [RouterLink],
  template: `
    <canvas #lienzo class="gi-lienzo" role="img" [attr.aria-label]="'Globo con ' + (datos()?.puntos?.length ?? 0) + ' orígenes de sondas en los últimos 7 días'"></canvas>
    <header class="gi-cab"><span class="gi-titulo">Perímetro</span><a routerLink="/seguridad">Ver en Seguridad →</a></header>
    <div class="gi-super"><strong>{{ intentos() }}</strong><p>intentos de entrada fallidos en 7 días · {{ datos()?.total ?? 0 }} sondas de {{ datos()?.ips ?? 0 }} IP</p></div>
    @if (paises().length) { <ul class="gi-paises" aria-label="Países con más sondas">@for (p of paises(); track p.pais) { <li><i></i><b>{{ p.pais }}</b> {{ p.sondas }} sondas</li> }</ul> }
    @if (fallo()) { <div class="gi-fallo" role="status"><p>No se pudo dibujar el globo.</p><button type="button" (click)="arrancar()">Reintentar</button></div> }
  `,
  styles: `
    app-globo-inicio { display: block; position: relative; overflow: hidden; height: clamp(420px, 52vw, 640px); border-radius: var(--radio); border: 1px solid var(--vidrio-borde); background: var(--brillo), var(--vidrio); box-shadow: var(--sombra); }
    .gi-lienzo { position: absolute; inset: 0; display: block; width: 100%; height: 100%; touch-action: pan-y; cursor: grab; } .gi-lienzo:active { cursor: grabbing; }
    .gi-cab { position: absolute; left: 22px; right: 22px; top: 18px; display: flex; justify-content: space-between; align-items: center; z-index: 2; pointer-events: none; }
    .gi-titulo { font: 700 .85rem var(--font-display); letter-spacing: -.01em; color: var(--tono-titulo, var(--brand)); } .gi-cab a { pointer-events: auto; font-size: .82rem; color: var(--soft); text-decoration: none; } .gi-cab a:hover { color: var(--brand); }
    .gi-super { position: absolute; left: 24px; bottom: 20px; z-index: 2; pointer-events: none; max-width: 280px; }
    .gi-super strong { display: block; font: var(--peso-ligero) clamp(3rem, 2rem + 4vw, 5.2rem)/1 var(--font-text); letter-spacing: -.05em; color: var(--tono-titulo, var(--brand)); } .gi-super p { margin: 4px 0 0; font-size: .82rem; color: var(--soft); }
    .gi-paises { position: absolute; right: 18px; bottom: 18px; z-index: 2; display: grid; gap: 6px; justify-items: end; margin: 0; padding: 0; list-style: none; font-size: .78rem; }
    .gi-paises li { display: inline-flex; align-items: center; gap: 7px; padding: 4px 11px; border-radius: 999px; background: color-mix(in srgb, var(--tarjeta) 85%, transparent); border: 1px solid var(--vidrio-borde); }
    .gi-paises i { width: 8px; height: 8px; border-radius: 50%; background: var(--brand); }
    .gi-fallo { position: absolute; inset: 0; display: grid; place-content: center; justify-items: center; gap: 8px; color: var(--soft); background: var(--tarjeta); z-index: 3; }
    .gi-fallo button { border: 1px solid var(--vidrio-borde); border-radius: 999px; background: none; color: var(--acento); padding: 10px 16px; min-height: 40px; cursor: pointer; }
  `,
})
export class GloboInicio {
  private readonly http = inject(HttpClient);
  private readonly casa = inject(CieloConfig);
  private readonly lienzo = viewChild.required<ElementRef<HTMLCanvasElement>>('lienzo');
  protected readonly datos = signal<ResumenSondas | null>(null);
  private readonly fallidos = signal<AccesoGeo[]>([]);
  protected readonly fallo = signal(false);
  protected readonly intentos = computed(() => this.fallidos().length);
  protected readonly paises = computed(() => (this.datos()?.paises ?? []).slice(0, 4));
  private cierre: (() => void) | null = null;
  private vivo = true;
  private pintar: (() => void) | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => { this.vivo = false; this.cierre?.(); this.cierre = null; });
    afterNextRender(() => { void this.leer(); this.arrancar(); });
  }

  private async leer(): Promise<void> {
    const [r, a] = await Promise.all([
      firstValueFrom(this.http.get<ResumenSondas>('/api/seguridad/sondas?dias=7')).catch(() => null),
      firstValueFrom(this.http.get<AccesoGeo[]>('/api/seguridad/accesos')).catch(() => null),
    ]);
    if (!this.vivo) return;
    this.datos.set(r);
    this.fallidos.set((a ?? []).filter((x) => !x.exito));
    this.pintar?.();
  }

  protected arrancar(): void {
    this.fallo.set(false);
    import('three')
      .then((THREE) => {
        if (!this.vivo) return;
        this.cierre?.();
        let actual = this.montar(THREE);
        // si cambia el tema se vuelve a montar con la paleta nueva, sin perder el contexto WebGL (el lienzo es el mismo)
        const obs = new MutationObserver(() => { if (!this.vivo) return; actual(false); actual = this.montar(THREE); });
        obs.observe(document.documentElement, { attributes: true, attributeFilter: ['data-mode', 'data-color', 'style'] });
        this.cierre = () => { obs.disconnect(); actual(true); };
      })
      .catch(() => { if (this.vivo) this.fallo.set(true); });
  }

  private montar(THREE: Three): (perderContexto?: boolean) => void {
    const canvas = this.lienzo().nativeElement;
    const reducido = matchMedia('(prefers-reduced-motion: reduce)').matches;
    const claro = document.documentElement.dataset['mode'] === 'light';
    const brand = new THREE.Color(resolverColor(getComputedStyle(document.documentElement).getPropertyValue('--brand'), '#ff7a1f'));
    const blanco = new THREE.Color(0xffffff);
    const colores = claro
      ? { esfera: blanco.clone().lerp(brand, 0.08), grat: blanco.clone().lerp(brand, 0.32), tierra: brand.clone().multiplyScalar(0.55), sonda: new THREE.Color(0x3355ff), casa: brand.clone().multiplyScalar(0.3), relleno: 0.16, costa: 0.62 }
      : { esfera: new THREE.Color(0x0b1118), grat: new THREE.Color(0x1d3340), tierra: new THREE.Color(0xc7edf9), sonda: new THREE.Color(0x6f9dff), casa: new THREE.Color(0xffffff), relleno: 0.1, costa: 0.4 };
    const rgba = (c: THREE.Color, a: number) => `rgba(${Math.round(c.r * 255)},${Math.round(c.g * 255)},${Math.round(c.b * 255)},${a})`;

    const renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true });
    renderer.setClearColor(0x000000, 0);
    const scene = new THREE.Scene();
    const camera = new THREE.PerspectiveCamera(30, 1, 0.1, 50); camera.position.set(0, 0.5, 4.7); camera.lookAt(0, 0, 0);
    const grupo = new THREE.Group(); scene.add(grupo);
    const geos: THREE.BufferGeometry[] = [], mats: THREE.Material[] = [], texs: THREE.Texture[] = [];
    const g = <T extends THREE.BufferGeometry>(x: T): T => { geos.push(x); return x; };
    const m = <T extends THREE.Material>(x: T): T => { mats.push(x); return x; };

    grupo.add(new THREE.Mesh(g(new THREE.SphereGeometry(0.985, 48, 48)), m(new THREE.MeshBasicMaterial({ color: colores.esfera }))));
    const lin: THREE.Vector3[] = [];
    const v3 = (p: [number, number, number]) => new THREE.Vector3(...p);
    for (let lon = -180; lon < 180; lon += 30) for (let la = -80; la < 80; la += 5) lin.push(v3(sobreEsfera(la, lon, 1.001)), v3(sobreEsfera(la + 5, lon, 1.001)));
    for (let la = -60; la <= 60; la += 30) for (let lon = -180; lon < 180; lon += 5) lin.push(v3(sobreEsfera(la, lon, 1.001)), v3(sobreEsfera(la, lon + 5, 1.001)));
    grupo.add(new THREE.LineSegments(g(new THREE.BufferGeometry().setFromPoints(lin)), m(new THREE.LineBasicMaterial({ color: colores.grat, transparent: true, opacity: 0.55 }))));

    // continentes: puntos densos solo en tierra y, encima, el relleno con la costa
    const pos: number[] = [], lonN: number[] = [];
    for (const [la, lo] of puntosDeTierra(1.1)) { const q = sobreEsfera(la, lo, 1); pos.push(...q); lonN.push((lo + 180) / 360); }
    const gt = g(new THREE.BufferGeometry()); gt.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3)); gt.setAttribute('aL', new THREE.Float32BufferAttribute(lonN, 1));
    const matT = m(new THREE.ShaderMaterial({
      transparent: true, depthWrite: false, uniforms: { uP: { value: reducido ? 1.3 : 0 }, uPR: { value: Math.min(devicePixelRatio, 2) }, uC: { value: colores.tierra } },
      vertexShader: 'attribute float aL; uniform float uP; uniform float uPR; varying float vA; void main(){ float k=uP-aL; float fr=clamp(1.-k*6.,0.,1.); vec3 n=normalize(normalMatrix*position); float cara=smoothstep(-.05,.35,n.z); vA=(k<0.?0.:(.5+fr*.5))*(.1+.9*cara); gl_PointSize=(k<0.?0.:(1.8+fr*3.))*uPR; gl_Position=projectionMatrix*modelViewMatrix*vec4(position,1.);}',
      fragmentShader: 'uniform vec3 uC; varying float vA; void main(){ float d=length(gl_PointCoord-.5); if(d>.5) discard; gl_FragColor=vec4(uC,vA*(1.-d*1.3)); }',
    }));
    grupo.add(new THREE.Points(gt, matT));
    const tex = new THREE.CanvasTexture(texturaTierra(rgba(colores.tierra, colores.relleno), rgba(colores.tierra, colores.costa))); texs.push(tex);
    grupo.add(new THREE.Mesh(g(new THREE.SphereGeometry(1.002, 64, 64)), m(new THREE.MeshBasicMaterial({ map: tex, transparent: true, depthWrite: false }))));

    // tu casa y los orígenes
    const hogar = this.casa.lugar();
    const casaM = new THREE.Mesh(g(new THREE.SphereGeometry(0.018, 16, 16)), m(new THREE.MeshBasicMaterial({ color: colores.casa }))); casaM.position.set(...sobreEsfera(hogar.lat, hogar.lon, 1.012)); grupo.add(casaM);
    const arcos: { linea: THREE.Line; n: number; i: number; ring: THREE.Mesh; marca: THREE.Mesh }[] = [];
    const geoRing = g(new THREE.RingGeometry(0.02, 0.024, 40));
    const poner = () => {
      arcos.splice(0).forEach((a) => { grupo.remove(a.linea, a.ring, a.marca); });
      const orig: { lat: number; lon: number; tam: number; color: THREE.Color }[] = [
        ...(this.datos()?.puntos ?? []).map((p) => ({ lat: p.lat, lon: p.lon, tam: Math.sqrt(p.sondas), color: colores.sonda })),
        ...this.fallidos().filter((a) => a.lat != null && a.lon != null).map((a) => ({ lat: a.lat as number, lon: a.lon as number, tam: 2, color: brand })),
      ].slice(0, 40);
      orig.forEach((o, i) => {
        const pts = arcoGrande(o.lat, o.lon, hogar.lat, hogar.lon, 64, 1.012, 0.16).map(v3);
        const lm = m(new THREE.LineBasicMaterial({ color: o.color, transparent: true, opacity: 0.8 }));
        const linea = new THREE.Line(g(new THREE.BufferGeometry().setFromPoints(pts)), lm); linea.geometry.setDrawRange(0, reducido ? pts.length : 0); grupo.add(linea);
        const marca = new THREE.Mesh(g(new THREE.SphereGeometry(0.011 + Math.min(0.02, o.tam * 0.0016), 12, 12)), m(new THREE.MeshBasicMaterial({ color: o.color }))); marca.position.copy(pts[0]); marca.visible = reducido; grupo.add(marca);
        const ring = new THREE.Mesh(geoRing, m(new THREE.MeshBasicMaterial({ color: o.color, transparent: true, side: THREE.DoubleSide }))); ring.position.copy(pts[0]); ring.lookAt(pts[0].clone().multiplyScalar(3)); ring.visible = !reducido && false; grupo.add(ring);
        arcos.push({ linea, n: pts.length, i, ring, marca });
      });
    };
    this.pintar = poner; poner();

    const rot0 = (-hogar.lon * Math.PI) / 180 + 0.5;
    grupo.rotation.x = 0.28; grupo.rotation.y = reducido ? rot0 : rot0 - 2.6;
    let arrastre = false, x0 = 0, manual = 0;
    const abajo = (e: PointerEvent) => { arrastre = true; x0 = e.clientX; canvas.setPointerCapture(e.pointerId); };
    const arriba = () => { arrastre = false; };
    const mueve = (e: PointerEvent) => { if (!arrastre) return; grupo.rotation.y += (e.clientX - x0) * 0.008; x0 = e.clientX; manual = performance.now(); };
    canvas.addEventListener('pointerdown', abajo); canvas.addEventListener('pointerup', arriba); canvas.addEventListener('pointermove', mueve);

    let w = 0, h = 0, visible = true;
    const ajustar = () => { const r = canvas.getBoundingClientRect(); w = r.width; h = r.height; if (w < 2 || h < 2) return; renderer.setPixelRatio(Math.min(devicePixelRatio, 2)); renderer.setSize(w, h, false); camera.aspect = w / h; camera.updateProjectionMatrix(); };
    const observador = new ResizeObserver(() => { ajustar(); if (reducido) dibujar(performance.now()); }); observador.observe(canvas); ajustar();
    const io = new IntersectionObserver((e) => { visible = e[0]?.isIntersecting ?? true; }); io.observe(canvas);

    const ease = (k: number) => 1 - Math.pow(1 - k, 3);
    const t0 = performance.now(); let marco = 0, cerrado = false;
    const dibujar = (t: number) => {
      const e = t - t0; matT.uniforms['uP'].value = reducido ? 1.3 : Math.min(1.3, e / 2600);
      if (!reducido) {
        if (!(manual && t - manual < 2500)) grupo.rotation.y += 0.0016;
        if (e < 3200) grupo.rotation.y += (rot0 - 2.6 + 2.6 * ease(Math.min(1, e / 3200)) - grupo.rotation.y) * 0.02;
        arcos.forEach((a) => { const k = Math.max(0, Math.min(1, (e - 1500 - a.i * 90) / 1400)); a.linea.geometry.setDrawRange(0, Math.round(k * a.n)); a.marca.visible = a.ring.visible = k > 0.95; const p = (e / 1500 + a.i * 0.3) % 1; a.ring.scale.setScalar(1 + p * 5); (a.ring.material as THREE.MeshBasicMaterial).opacity = (1 - p) * 0.9; });
      }
      renderer.render(scene, camera);
    };
    const bucle = (t: number) => { if (cerrado) return; marco = requestAnimationFrame(bucle); if (visible && w > 1 && h > 1 && !document.hidden) dibujar(t); };
    if (reducido) dibujar(performance.now()); else marco = requestAnimationFrame(bucle);

    return (perderContexto = true) => {
      cerrado = true; this.pintar = null; cancelAnimationFrame(marco); observador.disconnect(); io.disconnect();
      canvas.removeEventListener('pointerdown', abajo); canvas.removeEventListener('pointerup', arriba); canvas.removeEventListener('pointermove', mueve);
      geos.forEach((x) => x.dispose()); mats.forEach((x) => x.dispose()); texs.forEach((x) => x.dispose()); renderer.dispose(); if (perderContexto) renderer.forceContextLoss();
    };
  }
}
