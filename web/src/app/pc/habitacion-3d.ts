import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, afterNextRender, computed, effect, inject, signal, viewChild } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type * as THREE from 'three';
import { SistemaService } from '../inicio/sistema.service';

interface Aparato { id: number; nombre: string; icono: string; presencia: boolean; enLinea: boolean | null; latenciaMs: number | null; }
export interface DatosHabitacion { cpu: number; real: boolean; router: boolean | null; movil: boolean | null; otros: (boolean | null)[]; }

type Three = typeof import('three');

// El gemelo digital de tu habitación: tu escritorio con el monitor, la torre del PC, el router, el móvil y una estantería con los demás aparatos de la red.
// Todo refleja el estado de verdad: el monitor y el ventilador de la torre siguen la carga del PC, los LED del router se ponen verdes o rojos, el móvil se enciende
// si está en casa y cada cubo de la estantería es un aparato (verde en línea, rojo caído, gris sin comprobar). Arrastra para girarla.
@Component({
  selector: 'app-habitacion-3d',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrls: ['../gastos/comun.css'],
  template: `
    <section class="caja">
      <header class="caja__cab"><h3>Tu habitación en 3D</h3><span class="sub">el estado real de tu equipo y tu red · arrastra para girarla</span></header>
      <div class="escena">
        <canvas #lienzo role="img" aria-label="Tu escritorio en 3D: monitor, torre del PC, router, móvil y estantería con los aparatos de la red, con su estado real"></canvas>
        @if (fallo()) { <div class="fallo" role="status"><p>No se pudo dibujar la habitación en 3D.</p><button type="button" (click)="arrancar()">Reintentar</button></div> }
      </div>
      <ul class="leyenda" aria-label="Estado de lo que se ve">
        <li><i class="punto" [class.ok]="datos().real" [class.sin]="!datos().real"></i>PC {{ datos().real ? 'con agente · ' + datos().cpu + ' %' : 'sin agente' }}</li>
        <li><i class="punto" [class.ok]="datos().router === true" [class.mal]="datos().router === false" [class.sin]="datos().router === null"></i>Router {{ nombre(datos().router) }}</li>
        <li><i class="punto" [class.ok]="datos().movil === true" [class.mal]="datos().movil === false" [class.sin]="datos().movil === null"></i>Móvil {{ datos().movil === true ? 'en casa' : datos().movil === false ? 'fuera' : 'sin marcar' }}</li>
        <li><i class="punto ok"></i>{{ enLinea() }} de {{ datos().otros.length }} aparatos en línea</li>
      </ul>
    </section>
  `,
  styles: `
    :host { display: block; }
    .escena { position: relative; height: clamp(280px, 48vw, 440px); border-radius: 18px; overflow: hidden; background: radial-gradient(70% 70% at 50% 45%, color-mix(in srgb, var(--brand) 9%, transparent), transparent 75%); }
    canvas { display: block; width: 100%; height: 100%; touch-action: pan-y; cursor: grab; }
    canvas:active { cursor: grabbing; }
    .fallo { position: absolute; inset: 0; display: grid; place-content: center; gap: 8px; justify-items: center; color: var(--soft); text-align: center; }
    .fallo button { border: 1px solid var(--vidrio-borde); border-radius: 999px; background: none; color: var(--acento); font: 600 .84rem/1 var(--font-text); padding: 10px 16px; min-height: 40px; cursor: pointer; }
    .leyenda { display: flex; flex-wrap: wrap; gap: 6px 18px; margin: 12px 0 0; padding: 0; list-style: none; font-size: .8rem; color: var(--soft); }
    .leyenda li { display: inline-flex; align-items: center; gap: 7px; }
    .punto { width: 9px; height: 9px; border-radius: 50%; background: var(--soft); }
    .punto.ok { background: #3fb96a; box-shadow: 0 0 0 3px color-mix(in srgb, #3fb96a 25%, transparent); }
    .punto.mal { background: var(--bad); box-shadow: 0 0 0 3px color-mix(in srgb, var(--bad) 25%, transparent); }
  `,
})
export class Habitacion3d {
  private readonly http = inject(HttpClient);
  private readonly sistema = inject(SistemaService);
  private readonly lienzo = viewChild.required<ElementRef<HTMLCanvasElement>>('lienzo');
  protected readonly fallo = signal(false);
  private readonly aparatos = signal<Aparato[]>([]);
  private actualizar: ((d: DatosHabitacion) => void) | null = null;
  private cierre: (() => void) | null = null;
  private vivo = true;

  protected readonly datos = computed<DatosHabitacion>(() => {
    const l = this.aparatos();
    const router = l.find((a) => a.icono === 'router'), movil = l.find((a) => a.presencia) ?? l.find((a) => a.icono === 'movil');
    const otros = l.filter((a) => a !== router && a !== movil).slice(0, 6).map((a) => a.enLinea);
    const m = this.sistema.actual();
    return { cpu: Math.round(m?.cpu ?? 0), real: m?.origen === 'equipo', router: router?.enLinea ?? null, movil: movil?.enLinea ?? null, otros };
  });
  protected readonly enLinea = computed(() => this.datos().otros.filter((x) => x === true).length);

  constructor() {
    inject(DestroyRef).onDestroy(() => { this.vivo = false; this.cierre?.(); this.cierre = null; clearInterval(t); });
    const t = setInterval(() => { if (!document.hidden) void this.leer(); }, 30_000);
    afterNextRender(() => { void this.leer(); this.arrancar(); });
    effect(() => { const d = this.datos(); this.actualizar?.(d); });
  }

  protected nombre(v: boolean | null): string { return v === true ? 'en línea' : v === false ? 'caído' : 'sin añadir'; }

  private async leer(): Promise<void> {
    try { this.aparatos.set(await firstValueFrom(this.http.get<Aparato[]>('/api/red'))); } catch { /* se queda como estaba */ }
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
    const camera = new THREE.PerspectiveCamera(36, 1, 0.1, 100);
    const css = () => getComputedStyle(document.documentElement);
    let brand = new THREE.Color(css().getPropertyValue('--brand').trim() || '#ff7a1f');
    const VERDE = new THREE.Color('#3fb96a'), ROJO = new THREE.Color('#e5484d'), GRIS = new THREE.Color('#5a6572');
    const color = (v: boolean | null) => (v === true ? VERDE : v === false ? ROJO : GRIS);

    scene.add(new THREE.AmbientLight(0xffffff, 0.8));
    const sol = new THREE.DirectionalLight(0xffffff, 1.5); sol.position.set(6, 12, 8); scene.add(sol);
    const luzMonitor = new THREE.PointLight(brand, 14, 14); luzMonitor.position.set(-0.8, 3.6, 1.2); scene.add(luzMonitor);

    const sala = new THREE.Group(); scene.add(sala);
    const mat = (c: number, r = 0.6, m = 0.2) => new THREE.MeshStandardMaterial({ color: c, roughness: r, metalness: m });
    const emisivo = (c: THREE.Color, i = 1) => { const m = new THREE.MeshStandardMaterial({ color: 0x0a0d12, emissive: c, emissiveIntensity: i, roughness: 0.4 }); return m; };
    const caja = (w: number, h: number, d: number, m: THREE.Material, x: number, y: number, z: number, padre: THREE.Object3D = sala) => {
      const o = new THREE.Mesh(new THREE.BoxGeometry(w, h, d), m); o.position.set(x, y, z); padre.add(o); return o;
    };

    // suelo, pared y escritorio
    const suelo = new THREE.Mesh(new THREE.PlaneGeometry(18, 13), mat(0x131a22, 0.9, 0)); suelo.rotation.x = -Math.PI / 2; sala.add(suelo);
    caja(18, 7, 0.2, mat(0x17202a, 0.9, 0), 0, 3.5, -6.2);
    caja(7.6, 0.28, 3.2, mat(0x2b3440, 0.5, 0.3), 0, 2.0, 0);
    for (const [x, z] of [[-3.5, -1.4], [3.5, -1.4], [-3.5, 1.4], [3.5, 1.4]]) caja(0.2, 2.0, 0.2, mat(0x1c232c), x, 1.0, z);

    // monitor: la pantalla se enciende con la carga del PC y una barra la enseña
    caja(0.5, 0.9, 0.4, mat(0x20262e, 0.4, 0.6), -0.8, 2.6, -0.7);
    caja(3.4, 2.0, 0.14, mat(0x0d1117, 0.4, 0.6), -0.8, 3.75, -0.7);
    const pantalla = caja(3.15, 1.75, 0.04, emisivo(brand, 0.5), -0.8, 3.75, -0.62);
    const barra = caja(2.7, 0.18, 0.05, emisivo(brand, 1.6), -0.8, 3.15, -0.58);
    barra.geometry.translate(1.35, 0, 0); barra.position.x = -2.15;

    // torre del PC con su ventilador y su LED
    caja(1.1, 2.3, 2.1, mat(0x1b2129, 0.45, 0.6), 2.9, 3.28, -0.3);
    const ledTorre = new THREE.Mesh(new THREE.SphereGeometry(0.07, 16, 12), emisivo(brand, 2)); ledTorre.position.set(2.9, 4.2, 0.78); sala.add(ledTorre);
    const ventilador = new THREE.Mesh(new THREE.TorusGeometry(0.42, 0.05, 8, 24), emisivo(brand, 0.9)); ventilador.position.set(2.9, 3.2, 0.78); sala.add(ventilador);
    const aspas = new THREE.Group(); aspas.position.copy(ventilador.position);
    for (let i = 0; i < 5; i++) { const a = caja(0.34, 0.12, 0.02, mat(0x222a33), 0.17, 0, 0, aspas); a.geometry.translate(0.1, 0, 0); a.parent!.remove(a); const b = new THREE.Group(); b.rotation.z = (i / 5) * Math.PI * 2; b.add(a); a.position.set(0.12, 0, 0); aspas.add(b); }
    sala.add(aspas);

    // router con sus tres antenas y tres LED
    caja(1.7, 0.26, 1.05, mat(0x242c36, 0.5, 0.4), -3.0, 2.27, 0.3);
    for (const x of [-3.5, -3.0, -2.5]) { const a = new THREE.Mesh(new THREE.CylinderGeometry(0.04, 0.04, 1.1, 8), mat(0x15191f)); a.position.set(x, 2.95, 0.05); a.rotation.z = (x + 3) * 0.25; sala.add(a); }
    const ledsRouter = [0, 1, 2].map((i) => { const l = new THREE.Mesh(new THREE.SphereGeometry(0.06, 12, 10), emisivo(GRIS, 2)); l.position.set(-3.45 + i * 0.22, 2.3, 0.84); sala.add(l); return l; });

    // móvil apoyado: la pantalla se enciende si estás en casa
    const movil = new THREE.Group(); movil.position.set(1.15, 2.55, 1.0); movil.rotation.x = -0.35; sala.add(movil);
    caja(0.5, 0.95, 0.07, mat(0x0d1117, 0.3, 0.7), 0, 0, 0, movil);
    const pantallaMovil = caja(0.42, 0.82, 0.02, emisivo(GRIS, 0.2), 0, 0, 0.045, movil);

    // estantería con un cubo por aparato
    caja(6.4, 0.16, 1.0, mat(0x2b3440, 0.5, 0.3), 0, 5.0, -5.6);
    const cubos = Array.from({ length: 6 }, (_, i) => { const c = new THREE.Mesh(new THREE.BoxGeometry(0.7, 0.7, 0.7), emisivo(GRIS, 0.9)); c.position.set(-2.5 + i, 5.45, -5.6); c.visible = false; sala.add(c); return c; });

    camera.position.set(8.5, 7.2, 11.5); camera.lookAt(0, 3.1, -0.6);
    let cpu = 0, cpuSuave = 0, real = false, routerEn: boolean | null = null, movilEn: boolean | null = null;
    const actualizarColores = () => {
      (pantalla.material as THREE.MeshStandardMaterial).emissive.copy(brand); (barra.material as THREE.MeshStandardMaterial).emissive.copy(brand);
      (ledTorre.material as THREE.MeshStandardMaterial).emissive.copy(real ? brand : GRIS); (ventilador.material as THREE.MeshStandardMaterial).emissive.copy(real ? brand : GRIS);
      luzMonitor.color.copy(brand);
      ledsRouter.forEach((l) => (l.material as THREE.MeshStandardMaterial).emissive.copy(color(routerEn)));
      const pm = pantallaMovil.material as THREE.MeshStandardMaterial; pm.emissive.copy(movilEn ? brand : GRIS); pm.emissiveIntensity = movilEn ? 1.2 : 0.15;
    };
    this.actualizar = (d) => {
      cpu = d.cpu; real = d.real; routerEn = d.router; movilEn = d.movil;
      cubos.forEach((c, i) => { c.visible = i < d.otros.length; if (c.visible) (c.material as THREE.MeshStandardMaterial).emissive.copy(color(d.otros[i])); });
      actualizarColores(); if (reducido) dibujar(0);
    };
    const d0 = this.datos(); cpu = d0.cpu; real = d0.real; routerEn = d0.router; movilEn = d0.movil;
    cubos.forEach((c, i) => { c.visible = i < d0.otros.length; if (c.visible) (c.material as THREE.MeshStandardMaterial).emissive.copy(color(d0.otros[i])); });
    actualizarColores();

    const observadorTema = new MutationObserver(() => { brand = new THREE.Color(css().getPropertyValue('--brand').trim() || '#ff7a1f'); actualizarColores(); if (reducido) dibujar(0); });
    observadorTema.observe(document.documentElement, { attributes: true, attributeFilter: ['data-color', 'data-mode'] });

    // arrastrar para girar; sin tocar, se balancea despacio
    let guia = 0, arrastrando = false, ultimo = 0;
    const abajo = (e: PointerEvent) => { arrastrando = true; ultimo = e.clientX; canvas.setPointerCapture(e.pointerId); };
    const mueve = (e: PointerEvent) => { if (!arrastrando) return; guia += (e.clientX - ultimo) * 0.008; ultimo = e.clientX; if (reducido) dibujar(0); };
    const arriba = () => { arrastrando = false; };
    canvas.addEventListener('pointerdown', abajo); canvas.addEventListener('pointermove', mueve); canvas.addEventListener('pointerup', arriba); canvas.addEventListener('pointercancel', arriba);

    let cerrado = false, reajustes = 0;
    const ajustar = () => {
      if (cerrado) return;
      const w = canvas.clientWidth, h = canvas.clientHeight;
      if (!w || !h) { if (reajustes++ < 60) requestAnimationFrame(ajustar); return; }
      renderer.setPixelRatio(Math.min(devicePixelRatio, 2)); renderer.setSize(w, h, false);
      camera.aspect = w / h; camera.fov = w / h < 1.1 ? 52 : 36; camera.updateProjectionMatrix();
      if (reducido) dibujar(0);
    };
    const observador = new ResizeObserver(ajustar); observador.observe(canvas);

    let t0 = performance.now(), visible = true, marco = 0, giro = 0;
    function dibujar(dt: number) {
      cpuSuave += (cpu - cpuSuave) * Math.min(1, dt * 3 + (reducido ? 1 : 0));
      const carga = cpuSuave / 100;
      giro += dt;
      (pantalla.material as THREE.MeshStandardMaterial).emissiveIntensity = 0.35 + carga * 1.1;
      barra.scale.x = Math.max(0.02, carga);
      luzMonitor.intensity = 5 + carga * 22;
      aspas.rotation.z -= dt * (real ? 2 + carga * 16 : 0);
      ledsRouter.forEach((l, i) => { (l.material as THREE.MeshStandardMaterial).emissiveIntensity = routerEn === true ? 1.2 + 1.3 * Math.max(0, Math.sin(giro * (3 + i) + i)) : routerEn === false ? 1.6 : 0.3; });
      cubos.forEach((c, i) => { c.rotation.y += dt * 0.4; c.position.y = 5.45 + Math.sin(giro * 1.4 + i) * 0.05; });
      const objetivo = arrastrando ? guia : guia + Math.sin(performance.now() / 5200) * 0.28;
      sala.rotation.y += (objetivo - sala.rotation.y) * Math.min(1, dt * 6 + (reducido ? 1 : 0));
      renderer.render(scene, camera);
    }
    const bucle = (t: number) => { const dt = Math.min(0.05, (t - t0) / 1000); t0 = t; if (visible && !document.hidden) dibujar(dt); marco = requestAnimationFrame(bucle); };
    const io = new IntersectionObserver((e) => { visible = e[0]?.isIntersecting ?? true; });
    io.observe(canvas);
    ajustar();
    if (reducido) dibujar(0); else marco = requestAnimationFrame(bucle);

    return () => {
      cerrado = true; this.actualizar = null;
      cancelAnimationFrame(marco); observador.disconnect(); observadorTema.disconnect(); io.disconnect();
      canvas.removeEventListener('pointerdown', abajo); canvas.removeEventListener('pointermove', mueve); canvas.removeEventListener('pointerup', arriba); canvas.removeEventListener('pointercancel', arriba);
      scene.traverse((o) => {
        const m = o as THREE.Mesh;
        m.geometry?.dispose?.();
        const mt = m.material as THREE.Material | THREE.Material[] | undefined;
        (Array.isArray(mt) ? mt : mt ? [mt] : []).forEach((x) => x.dispose());
      });
      renderer.dispose();
      renderer.forceContextLoss();
    };
  }
}
