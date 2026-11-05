import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, afterNextRender, effect, inject, input, signal, viewChild } from '@angular/core';
import type * as THREE from 'three';
import { resolverColor } from '../core/color-css';

export interface DatosPc { cpu: number; memoria: number; disco: number; real: boolean; }

type Three = typeof import('three');

// «Tu PC por dentro»: una caja abierta con la placa base, el procesador con su ventilador, la memoria, la gráfica, las unidades y la fuente
// de alimentación con sus cables. Se mueve con el equipo de verdad: el procesador brilla y gira más con la carga, la memoria y el disco
// se llenan, y por los cables corren pulsos de corriente más rápidos cuanto más trabaja el PC. Arrastra para girarlo.
@Component({
  selector: 'app-pc-3d',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <canvas #lienzo role="img" aria-label="Dibujo en 3D de tu PC por dentro: placa base, procesador, memoria, gráfica, discos y fuente de alimentación"></canvas>
    @if (fallo()) {
      <div class="fallo" role="status">
        <svg viewBox="0 0 120 120" aria-hidden="true"><rect x="22" y="12" width="76" height="96" rx="10"/><circle cx="46" cy="40" r="12"/><rect x="66" y="30" width="22" height="40" rx="3"/><rect x="34" y="80" width="52" height="10" rx="3"/></svg>
        <p>No se pudo dibujar el PC en 3D.</p>
        <button type="button" (click)="reintentar()">Reintentar</button>
      </div>
    }
  `,
  styles: `
    :host { display: block; position: relative; }
    canvas { display: block; width: 100%; height: 100%; touch-action: pan-y; cursor: grab; }
    canvas:active { cursor: grabbing; }
    .fallo { position: absolute; inset: 0; display: grid; place-content: center; justify-items: center; gap: 8px; text-align: center; color: var(--soft); }
    .fallo svg { width: 84px; fill: none; stroke: var(--brand); stroke-width: 2; opacity: .6; }
    .fallo p { margin: 0; font-size: .9rem; }
    .fallo button { border: 1px solid var(--vidrio-borde); border-radius: 999px; background: none; color: var(--acento); font: 600 .84rem/1 var(--font-text); padding: 10px 16px; min-height: 40px; cursor: pointer; }
  `,
})
export class Pc3d {
  readonly datos = input.required<DatosPc>();
  protected readonly fallo = signal(false);
  private readonly lienzo = viewChild.required<ElementRef<HTMLCanvasElement>>('lienzo');
  private actualizar: ((d: DatosPc) => void) | null = null;
  private cierre: (() => void) | null = null;
  private vivo = true;

  constructor() {
    // el cierre se registra ya, no cuando llega three: así salir de Inicio antes de que cargue no deja el bucle ni el contexto WebGL vivos
    inject(DestroyRef).onDestroy(() => { this.vivo = false; this.cierre?.(); this.cierre = null; });
    afterNextRender(() => this.arrancar());
    effect(() => { const d = this.datos(); this.actualizar?.(d); });
  }

  protected reintentar(): void { this.arrancar(); }

  private arrancar(): void {
    this.fallo.set(false);
    import('three')
      .then((THREE) => {
        if (!this.vivo) return;
        this.cierre?.();
        this.cierre = this.montar(THREE, this.lienzo().nativeElement);
      })
      .catch(() => { if (this.vivo) this.fallo.set(true); }); // sin WebGL, contexto agotado o chunk viejo en la caché
  }

  private montar(THREE: Three, canvas: HTMLCanvasElement): () => void {
    const reducido = matchMedia('(prefers-reduced-motion: reduce)').matches;
    const renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true });
    renderer.setClearColor(0x000000, 0);
    const scene = new THREE.Scene();
    const camera = new THREE.PerspectiveCamera(38, 1, 0.1, 100);
    camera.position.set(0, 1.2, 25);

    // colores del tema (se leen al empezar y cuando cambia)
    const cs = () => getComputedStyle(document.documentElement);
    const color = (v: string, d: string) => new THREE.Color(resolverColor(cs().getPropertyValue(v), d));
    let brand = color('--brand', '#ff7a1f');
    const marco = new THREE.LineBasicMaterial({ color: brand, transparent: true, opacity: 0.55 });
    const trazas = new THREE.LineBasicMaterial({ color: brand, transparent: true, opacity: 0.32 });
    const emisivo = (c: THREE.Color, i: number) => {
      const m = new THREE.MeshStandardMaterial({ color: 0x0b0f14, emissive: c, emissiveIntensity: i, roughness: 0.4, metalness: 0.3 });
      m.userData['brillo'] = true; // solo estos se repintan al cambiar el color del tema
      return m;
    };

    scene.add(new THREE.AmbientLight(0xffffff, 0.75));
    const sol = new THREE.DirectionalLight(0xffffff, 1.4); sol.position.set(6, 10, 14); scene.add(sol);
    const luzCpu = new THREE.PointLight(brand, 20, 14); luzCpu.position.set(-1.5, 2.2, 1.2); scene.add(luzCpu);

    const pc = new THREE.Group(); pc.position.y = 1.3; scene.add(pc);
    const aristas = (g: THREE.BufferGeometry, m: THREE.Material = marco) => new THREE.LineSegments(new THREE.EdgesGeometry(g, 20), m);
    const caja = (w: number, h: number, d: number, mat: THREE.Material, x: number, y: number, z: number, linea = false) => {
      const g = new THREE.BoxGeometry(w, h, d);
      const m = new THREE.Mesh(g, mat); m.position.set(x, y, z);
      if (linea) m.add(aristas(g));
      pc.add(m);
      return m;
    };

    // la caja abierta: solo el marco y un fondo oscuro
    const marcoGeo = new THREE.BoxGeometry(12, 14, 6);
    const carcasa = aristas(marcoGeo); pc.add(carcasa);
    const fondo = new THREE.Mesh(new THREE.PlaneGeometry(12, 14), new THREE.MeshBasicMaterial({ color: 0x05080c, transparent: true, opacity: 0.55 }));
    fondo.position.z = -3; pc.add(fondo);

    // placa base con sus pistas y chips
    const placaMat = new THREE.MeshStandardMaterial({ color: 0x0f2a22, roughness: 0.6, metalness: 0.2 });
    caja(10.4, 11.6, 0.22, placaMat, 0, 0.6, -2.4);
    const pts: number[] = [];
    let semilla = 11; const azar = () => ((semilla = (semilla * 16807) % 2147483647) / 2147483647);
    for (let i = 0; i < 70; i++) {
      let x = (azar() - 0.5) * 9.6, y = 0.6 + (azar() - 0.5) * 10.6;
      for (let k = 0; k < 3; k++) {
        const horizontal = k % 2 === 0, largo = (0.6 + azar() * 1.8) * (azar() < 0.5 ? -1 : 1);
        const nx = horizontal ? x + largo : x, ny = horizontal ? y : y + largo;
        pts.push(x, y, -2.27, nx, ny, -2.27); x = nx; y = ny;
      }
    }
    const geoTrazas = new THREE.BufferGeometry(); geoTrazas.setAttribute('position', new THREE.Float32BufferAttribute(pts, 3));
    pc.add(new THREE.LineSegments(geoTrazas, trazas));
    const chip = new THREE.MeshStandardMaterial({ color: 0x151b24, roughness: 0.5, metalness: 0.6 });
    for (let i = 0; i < 14; i++) caja(0.5 + azar() * 0.7, 0.4 + azar() * 0.6, 0.12, chip, (azar() - 0.5) * 8.6, 0.6 + (azar() - 0.5) * 9.4, -2.22);
    // conector de 24 pines y los de la gráfica
    caja(0.5, 2.4, 0.5, new THREE.MeshStandardMaterial({ color: 0xe9eef5, roughness: 0.7 }), 4.4, 0.4, -2.05);

    // procesador: disipador, aletas y ventilador
    const cpuMat = emisivo(brand, 0.3);
    const disipador = caja(2.6, 2.6, 0.9, new THREE.MeshStandardMaterial({ color: 0x2a323d, roughness: 0.35, metalness: 0.85 }), -1.6, 2.7, -1.7, true);
    void disipador;
    for (let i = 0; i < 7; i++) caja(2.5, 0.06, 0.7, new THREE.MeshStandardMaterial({ color: 0x3b4552, metalness: 0.9, roughness: 0.3 }), -1.6, 1.7 + i * 0.33, -1.25);
    const nucleo = caja(1.2, 1.2, 0.1, cpuMat, -1.6, 2.7, -1.2);
    const ventilador = (r: number, x: number, y: number, z: number, palas: number) => {
      const g = new THREE.Group(); g.position.set(x, y, z);
      const anillo = new THREE.Mesh(new THREE.TorusGeometry(r, 0.07, 8, 40), emisivo(brand, 0.9)); g.add(anillo);
      const aspas = new THREE.Group();
      for (let i = 0; i < palas; i++) {
        const pala = new THREE.Mesh(new THREE.BoxGeometry(r * 0.78, r * 0.3, 0.04), new THREE.MeshStandardMaterial({ color: 0x1b222b, roughness: 0.4, metalness: 0.4 }));
        pala.position.x = r * 0.42; pala.rotation.z = 0.35;
        const brazo = new THREE.Group(); brazo.rotation.z = (i / palas) * Math.PI * 2; brazo.add(pala); aspas.add(brazo);
      }
      aspas.add(new THREE.Mesh(new THREE.CylinderGeometry(r * 0.18, r * 0.18, 0.08, 16).rotateX(Math.PI / 2), emisivo(brand, 0.5)));
      g.add(aspas); pc.add(g);
      return aspas;
    };
    const aspasCpu = ventilador(1.15, -1.6, 2.7, -0.95, 9);

    // memoria RAM: cuatro módulos que se llenan con el uso
    const modulos: THREE.Mesh[] = [];
    for (let i = 0; i < 4; i++) {
      const x = 1.2 + i * 0.62;
      caja(0.34, 3.6, 0.5, new THREE.MeshStandardMaterial({ color: 0x1a2029, roughness: 0.4, metalness: 0.5 }), x, 3.1, -2.0, true);
      const barra = new THREE.Mesh(new THREE.BoxGeometry(0.2, 3.3, 0.12), emisivo(brand, 1.2));
      barra.position.set(x, 3.1, -1.7); pc.add(barra); modulos.push(barra);
    }

    // tarjeta gráfica con dos ventiladores
    const gpu = caja(8.4, 1.5, 2.6, new THREE.MeshStandardMaterial({ color: 0x161c24, roughness: 0.35, metalness: 0.7 }), 0, -1.2, -0.9, true);
    void gpu;
    caja(8.5, 0.07, 2.65, emisivo(brand, 1.4), 0, -0.45, -0.9);
    const aspasGpu = [ventilador(0.62, -2.1, -1.2, 0.42, 7), ventilador(0.62, 2.1, -1.2, 0.42, 7)];

    // unidades de almacenamiento: dos SSD con una tira de luz que sube con el uso del disco
    const lucesDisco: THREE.Mesh[] = [];
    for (let i = 0; i < 2; i++) {
      const y = -3.3 + i * 0.5;
      caja(4.2, 0.28, 2.2, new THREE.MeshStandardMaterial({ color: 0x232b36, roughness: 0.4, metalness: 0.7 }), 1.8, y, -1.0, true);
      const t = new THREE.Mesh(new THREE.BoxGeometry(3.8, 0.06, 0.1), emisivo(brand, 1.3));
      t.position.set(1.8, y + 0.16, 0.12); pc.add(t); lucesDisco.push(t);
    }

    // fuente de alimentación abajo del todo
    const fuente = caja(8.2, 2.1, 3.6, new THREE.MeshStandardMaterial({ color: 0x10151b, roughness: 0.5, metalness: 0.6 }), 0, -5.6, -0.9, true);
    void fuente;
    const aspasFuente = ventilador(0.85, -2.2, -5.6, 0.95, 11);
    caja(2.4, 0.5, 0.06, emisivo(brand, 0.7), 2.2, -5.6, 0.92);

    // cables de la fuente a la placa, al procesador y a la gráfica; por ellos corren los pulsos de corriente
    const rutas: THREE.CatmullRomCurve3[] = [
      new THREE.CatmullRomCurve3([[2.8, -4.7, 0.6], [4.6, -3.2, 0.9], [5.0, -1.0, 0.2], [4.5, 0.2, -1.6]].map(([x, y, z]) => new THREE.Vector3(x, y, z))),
      new THREE.CatmullRomCurve3([[-3.4, -4.7, 0.7], [-5.2, -1.0, 0.2], [-5.0, 3.8, -0.6], [-2.8, 4.4, -1.6]].map(([x, y, z]) => new THREE.Vector3(x, y, z))),
      new THREE.CatmullRomCurve3([[0.4, -4.7, 1.0], [2.2, -2.4, 1.5], [4.4, -1.0, 1.0], [4.2, -1.1, 0.3]].map(([x, y, z]) => new THREE.Vector3(x, y, z))),
    ];
    const cableMat = new THREE.MeshStandardMaterial({ color: 0x0a0d12, roughness: 0.6 });
    rutas.forEach((r) => pc.add(new THREE.Mesh(new THREE.TubeGeometry(r, 40, 0.07, 8), cableMat)));
    const pulsosMat = new THREE.MeshBasicMaterial({ color: brand });
    const pulsos = rutas.flatMap((r, i) => Array.from({ length: 5 }, (_, k) => {
      const m = new THREE.Mesh(new THREE.SphereGeometry(0.14, 10, 10), pulsosMat); pc.add(m);
      return { m, r, fase: k / 5 + i * 0.13 };
    }));

    // polvillo de luz alrededor
    const polvoGeo = new THREE.BufferGeometry();
    const polvo = new Float32Array(160 * 3);
    for (let i = 0; i < 160; i++) polvo.set([(azar() - 0.5) * 26, (azar() - 0.5) * 18, (azar() - 0.5) * 12], i * 3);
    polvoGeo.setAttribute('position', new THREE.BufferAttribute(polvo, 3));
    const polvoMat = new THREE.PointsMaterial({ color: brand, size: 0.07, transparent: true, opacity: 0.55 });
    scene.add(new THREE.Points(polvoGeo, polvoMat));

    // ---- estado vivo ----
    let cpu = 0, mem = 0, disco = 0, real = false;
    let cpuSuave = 0;
    this.actualizar = (d) => { cpu = d.cpu; mem = d.memoria; disco = d.disco; real = d.real; if (reducido) dibujar(0); };
    // el effect de datos ya corrió antes de que existiera `actualizar`: se parte de los valores de ahora
    const d0 = this.datos(); cpu = d0.cpu; mem = d0.memoria; disco = d0.disco; real = d0.real;

    const repintar = () => {
      brand = color('--brand', '#ff7a1f');
      [marco, trazas, polvoMat].forEach((m) => m.color.copy(brand));
      pulsosMat.color.copy(brand);
      luzCpu.color.copy(brand);
      scene.traverse((o) => {
        const m = (o as THREE.Mesh).material as THREE.MeshStandardMaterial | undefined;
        if (m?.userData?.['brillo']) m.emissive.copy(brand);
      });
    };
    const observadorTema = new MutationObserver(() => { repintar(); if (reducido) dibujar(0); });
    observadorTema.observe(document.documentElement, { attributes: true, attributeFilter: ['data-color', 'data-mode'] });
    repintar();

    // arrastrar para girar; sin tocar, se balancea despacio
    let guiaY = 0, guiaX = 0, arrastrando = false, ultimoX = 0, ultimoY = 0;
    const abajo = (e: PointerEvent) => { arrastrando = true; ultimoX = e.clientX; ultimoY = e.clientY; canvas.setPointerCapture(e.pointerId); };
    const mueve = (e: PointerEvent) => {
      if (!arrastrando) return;
      guiaY += (e.clientX - ultimoX) * 0.008; guiaX = Math.max(-0.5, Math.min(0.5, guiaX + (e.clientY - ultimoY) * 0.006));
      ultimoX = e.clientX; ultimoY = e.clientY;
      if (reducido) dibujar(0);
    };
    const arriba = () => { arrastrando = false; };
    canvas.addEventListener('pointerdown', abajo); canvas.addEventListener('pointermove', mueve); canvas.addEventListener('pointerup', arriba); canvas.addEventListener('pointercancel', arriba);

    let cerrado = false, reajustes = 0;
    const ajustar = () => {
      if (cerrado) return;
      const w = canvas.clientWidth, h = canvas.clientHeight;
      // aún sin tamaño (la pantalla entra animada): se reintenta en el siguiente fotograma, hasta ~1 s
      if (!w || !h) { if (reajustes++ < 60) requestAnimationFrame(ajustar); return; }
      renderer.setPixelRatio(Math.min(devicePixelRatio, 2)); renderer.setSize(w, h, false);
      camera.aspect = w / h;
      camera.position.z = w / h < 1.1 ? 33 : 25;
      camera.updateProjectionMatrix();
      if (reducido) dibujar(0);
    };
    const observador = new ResizeObserver(ajustar); observador.observe(canvas);

    let t0 = performance.now(), visible = true, marcoId = 0, giro = 0;
    function dibujar(dt: number) {
      cpuSuave += (cpu - cpuSuave) * Math.min(1, dt * 3 + (reducido ? 1 : 0));
      const carga = cpuSuave / 100;
      giro += dt * (1.2 + carga * 14);
      aspasCpu.rotation.z -= dt * (2 + carga * 16);
      aspasGpu.forEach((a) => (a.rotation.z -= dt * (1.5 + carga * 10)));
      aspasFuente.rotation.z -= dt * (1 + carga * 6);
      (nucleo.material as THREE.MeshStandardMaterial).emissiveIntensity = 0.25 + carga * 2.8;
      luzCpu.intensity = 6 + carga * 60;
      modulos.forEach((m, i) => {
        const lleno = Math.min(1, Math.max(0.04, mem / 100 * 1.15 - i * 0.05));
        m.scale.y = lleno; m.position.y = 3.1 - 1.65 * (1 - lleno);
        (m.material as THREE.MeshStandardMaterial).emissiveIntensity = 0.5 + lleno * 1.2;
      });
      lucesDisco.forEach((l, i) => { const f = Math.min(1, Math.max(0.05, disco / 100 + (i ? -0.1 : 0.1))); l.scale.x = f; l.position.x = 1.8 - 1.9 * (1 - f); });
      pulsos.forEach((p) => {
        p.fase = (((p.fase + dt * (0.12 + carga * 0.9 + (real ? 0.08 : 0))) % 1) + 1) % 1; // siempre entre 0 y 1: la curva no admite menos
        p.m.position.copy(p.r.getPoint(p.fase));
        p.m.scale.setScalar(0.7 + Math.sin(p.fase * Math.PI) * 0.8);
      });
      const objetivoY = arrastrando ? guiaY : guiaY + Math.sin(performance.now() / 4200) * 0.5;
      pc.rotation.y += (objetivoY - pc.rotation.y) * Math.min(1, dt * 6 + (reducido ? 1 : 0));
      pc.rotation.x += (guiaX - 0.08 - pc.rotation.x) * Math.min(1, dt * 6 + (reducido ? 1 : 0));
      renderer.render(scene, camera);
    }
    // el siguiente fotograma se pide ANTES de dibujar y un fallo se salta: si no, una sola excepción paraba la escena para siempre (y quedaba en blanco).
    // dt nunca es negativo: la marca del primer fotograma puede ser anterior a t0.
    const bucle = (t: number) => {
      marcoId = requestAnimationFrame(bucle);
      const dt = Math.max(0, Math.min(0.05, (t - t0) / 1000)); t0 = t;
      if (visible && !document.hidden) { try { dibujar(dt); } catch { /* ese fotograma no se pinta */ } }
    };
    const io = new IntersectionObserver((e) => { visible = e[0]?.isIntersecting ?? true; });
    io.observe(canvas);
    ajustar();
    if (reducido) dibujar(0); else marcoId = requestAnimationFrame(bucle);

    return () => {
      cerrado = true; this.actualizar = null;
      cancelAnimationFrame(marcoId); observador.disconnect(); observadorTema.disconnect(); io.disconnect();
      canvas.removeEventListener('pointerdown', abajo); canvas.removeEventListener('pointermove', mueve); canvas.removeEventListener('pointerup', arriba); canvas.removeEventListener('pointercancel', arriba);
      scene.traverse((o) => {
        const m = o as THREE.Mesh;
        m.geometry?.dispose?.();
        const mat = m.material as THREE.Material | THREE.Material[] | undefined;
        (Array.isArray(mat) ? mat : mat ? [mat] : []).forEach((x) => x.dispose());
      });
      renderer.dispose();
      renderer.forceContextLoss(); // en móvil los contextos WebGL son pocos: entrar y salir de Inicio no puede agotarlos
    };
  }
}
