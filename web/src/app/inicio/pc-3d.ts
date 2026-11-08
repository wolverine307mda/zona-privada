import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, ViewEncapsulation, afterNextRender, computed, inject, input, signal, viewChild } from '@angular/core';
import type * as THREE from 'three';
import { resolverColor } from '../core/color-css';

export interface DatosPc { cpu: number; memoria: number; disco: number; real: boolean; }

type Three = typeof import('three');
type Vista = 'normal' | 'uso' | 'calor' | 'aire';
type Id = 'bat' | 'placa' | 'cpu' | 'gpu' | 'ram' | 'ssd' | 'vent' | 'tapa';
interface Parte { id: Id; g: THREE.Group; nivel: number; lift: number; base: number; a: number; mats: Set<THREE.Material>; centro: THREE.Vector3; }

const ETIQUETADAS: Id[] = ['bat', 'ssd', 'ram', 'cpu', 'vent'];
const NOMBRES: Record<string, string> = { bat: 'Batería', ssd: 'Disco', ram: 'Memoria', cpu: 'Procesador', gpu: 'Gráfica', vent: 'Refrigeración' };

// «Tu PC por dentro»: un despiece del equipo con materiales y luz. La placa lleva circuitos; la memoria, el disco y la batería se iluminan según lo que usan;
// el procesador se calienta con su temperatura; los ventiladores giran más con la carga. Tres vistas (uso, calor, aire), un deslizador de despiece,
// y al pasar el ratón o tocar una pieza se resalta y se ve su detalle. Arrastra para girarlo.
@Component({
  selector: 'app-pc-3d',
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  template: `
    <canvas #lienzo class="p3-lienzo" role="img" aria-label="Dibujo en 3D de tu PC por dentro: batería, placa base, procesador, memoria, disco y ventiladores"></canvas>
    <svg class="p3-lineas" #lineas aria-hidden="true"></svg>
    <div class="p3-etqs" #etqs></div>
    <div class="p3-detalle" aria-live="polite">
      @if (detalle(); as d) {
        <h4>{{ d.nombre }}</h4><strong>{{ d.linea }}</strong>
        <dl>@for (f of d.filas; track f[0]) { <div><dt>{{ f[0] }}</dt><dd>{{ f[1] }}</dd></div> }</dl>
      } @else { <p>Toca una pieza para ver su detalle</p> }
    </div>
    <div class="p3-barra">
      <div class="p3-seg" role="group" aria-label="Vista">
        @for (v of vistas; track v.id) { <button type="button" [class.on]="vista() === v.id" [attr.aria-pressed]="vista() === v.id" (click)="vista.set(v.id)">{{ v.texto }}</button> }
      </div>
      <label class="p3-desp">Despiece <input type="range" min="0" max="100" [value]="despiece()" (input)="despiece.set(+$any($event.target).value)" aria-label="Despiece"></label>
      <button type="button" class="p3-gira" [class.on]="gira()" [attr.aria-pressed]="gira()" (click)="gira.set(!gira())">Girar</button>
    </div>
    @if (fallo()) {
      <div class="p3-fallo" role="status">
        <svg viewBox="0 0 120 120" aria-hidden="true"><rect x="22" y="12" width="76" height="96" rx="10"/><circle cx="46" cy="40" r="12"/><rect x="66" y="30" width="22" height="40" rx="3"/><rect x="34" y="82" width="52" height="10" rx="3"/></svg>
        <p>No se pudo dibujar el PC en 3D.</p>
        <button type="button" (click)="reintentar()">Reintentar</button>
      </div>
    }
  `,
  styles: `
    app-pc-3d { display: block; position: relative; overflow: hidden; }
    .p3-lienzo { position: absolute; inset: 0; display: block; width: 100%; height: 100%; touch-action: pan-y; cursor: grab; }
    .p3-lienzo:active { cursor: grabbing; }
    .p3-lineas { position: absolute; inset: 0; width: 100%; height: 100%; pointer-events: none; }
    .p3-lineas line { stroke: var(--tono-titulo, var(--brand)); stroke-width: 1; opacity: .7; } .p3-lineas circle { fill: var(--brand); }
    .p3-etqs { position: absolute; inset: 0; pointer-events: none; }
    .p3-et { position: absolute; left: 0; top: 0; font-size: 12px; line-height: 1.3; white-space: nowrap; color: var(--texto); padding: 3px 9px; border-radius: 10px; border: 1px solid var(--vidrio-borde);
      background: color-mix(in srgb, var(--tarjeta) 82%, transparent); pointer-events: auto; cursor: pointer; border-width: 1px; }
    .p3-et b { display: block; font: 500 10.5px var(--font-text); letter-spacing: .08em; text-transform: uppercase; color: var(--tono-titulo, var(--brand)); }
    .p3-et.activo { border-color: var(--brand); font-weight: 600; } .p3-et.activo b { color: var(--brand); }
    .p3-detalle { position: absolute; left: 14px; top: 14px; width: 200px; padding: 12px 14px; border-radius: 18px; font-size: 13px; background: color-mix(in srgb, var(--tarjeta) 88%, transparent); border: 1px solid var(--vidrio-borde); backdrop-filter: blur(8px); }
    .p3-detalle p { margin: 0; color: var(--soft); } .p3-detalle h4 { margin: 0 0 4px; font: 700 11px var(--font-display); letter-spacing: .02em; color: var(--tono-titulo, var(--brand)); text-transform: uppercase; }
    .p3-detalle strong { display: block; margin-bottom: 8px; font: 300 1.4rem/1.15 var(--font-text); letter-spacing: -.03em; } .p3-detalle dl { display: grid; gap: 4px; margin: 0; }
    .p3-detalle dl div { display: flex; justify-content: space-between; gap: 10px; } .p3-detalle dt { color: var(--soft); } .p3-detalle dd { margin: 0; font-weight: 500; }
    .p3-barra { position: absolute; left: 14px; right: 14px; bottom: 12px; display: flex; align-items: center; gap: 10px; flex-wrap: wrap; }
    .p3-seg { display: inline-grid; grid-auto-flow: column; gap: 3px; padding: 3px; border-radius: 999px; background: color-mix(in srgb, var(--tarjeta) 85%, transparent); border: 1px solid var(--vidrio-borde); backdrop-filter: blur(8px); }
    .p3-seg button, .p3-gira { border: 0; background: none; color: var(--texto); border-radius: 999px; padding: 7px 12px; font: 500 12.5px var(--font-text); cursor: pointer; min-height: 32px; }
    .p3-seg button.on { background: var(--tarjeta-2); color: var(--tono-titulo, var(--brand)); font-weight: 600; }
    .p3-gira { border: 1px solid var(--vidrio-borde); background: color-mix(in srgb, var(--tarjeta) 85%, transparent); } .p3-gira.on { background: var(--brand); color: var(--on-brand); border-color: transparent; }
    .p3-desp { display: inline-flex; align-items: center; gap: 8px; font-size: 12px; color: var(--soft); padding: 4px 12px; border-radius: 999px; background: color-mix(in srgb, var(--tarjeta) 85%, transparent); border: 1px solid var(--vidrio-borde); }
    .p3-desp input { width: 90px; accent-color: var(--brand); }
    .p3-fallo { position: absolute; inset: 0; display: grid; place-content: center; justify-items: center; gap: 8px; text-align: center; color: var(--soft); background: var(--tarjeta); }
    .p3-fallo svg { width: 84px; fill: none; stroke: var(--brand); stroke-width: 2; opacity: .6; } .p3-fallo p { margin: 0; font-size: .9rem; }
    .p3-fallo button { border: 1px solid var(--vidrio-borde); border-radius: 999px; background: none; color: var(--acento); font: 600 .84rem/1 var(--font-text); padding: 10px 16px; min-height: 40px; cursor: pointer; }
    @media (max-width: 560px) { .p3-detalle { display: none; } .p3-et { font-size: 11px; } }
  `,
})
export class Pc3d {
  readonly datos = input.required<DatosPc>();
  /** carga de la batería (0–100) y temperaturas en °C; sin dato, la pieza se enseña sin cifra */
  readonly bateria = input<number | null>(null);
  readonly tempCpu = input<number | null>(null);
  readonly tempDisco = input<number | null>(null);

  protected readonly vistas: { id: Vista; texto: string }[] = [{ id: 'normal', texto: 'Normal' }, { id: 'uso', texto: 'Uso' }, { id: 'calor', texto: 'Calor' }, { id: 'aire', texto: 'Aire' }];
  protected readonly vista = signal<Vista>('normal');
  protected readonly despiece = signal(100);
  protected readonly gira = signal(true);
  protected readonly sel = signal<Id | null>(null);
  protected readonly fallo = signal(false);

  private readonly lienzo = viewChild.required<ElementRef<HTMLCanvasElement>>('lienzo');
  private readonly lineas = viewChild.required<ElementRef<SVGSVGElement>>('lineas');
  private readonly etqs = viewChild.required<ElementRef<HTMLDivElement>>('etqs');
  private cierre: (() => void) | null = null;
  private vivo = true;

  /** las cifras de cada pieza, con lo que haya medido */
  private readonly cifras = computed(() => {
    const d = this.datos(), b = this.bateria(), tc = this.tempCpu(), td = this.tempDisco();
    const t = (x: number | null) => (x != null ? ` · ${Math.round(x)} °C` : '');
    return {
      bat: { linea: b != null ? `${Math.round(b)} %` : 'sin batería', filas: [['Carga', b != null ? `${Math.round(b)} %` : '—']] as [string, string][] },
      ssd: { linea: `${d.disco} %${t(td)}`, filas: [['Ocupado', `${d.disco} %`], ['Temperatura', td != null ? `${Math.round(td)} °C` : '—']] as [string, string][] },
      ram: { linea: `${d.memoria} %`, filas: [['En uso', `${d.memoria} %`], ['Libre', `${100 - d.memoria} %`]] as [string, string][] },
      cpu: { linea: `${d.cpu} %${t(tc)}`, filas: [['Carga', `${d.cpu} %`], ['Temperatura', tc != null ? `${Math.round(tc)} °C` : '—']] as [string, string][] },
      gpu: { linea: 'Gráfica', filas: [['Estado', 'sin medir']] as [string, string][] },
      vent: { linea: tc != null ? `${Math.round(tc)} °C` : `${d.cpu} % de carga`, filas: [['Procesador', `${d.cpu} %`], ['Temperatura', tc != null ? `${Math.round(tc)} °C` : '—']] as [string, string][] },
    } as Record<string, { linea: string; filas: [string, string][] }>;
  });
  protected readonly detalle = computed(() => { const s = this.sel(); return s && this.cifras()[s] ? { nombre: NOMBRES[s], ...this.cifras()[s] } : null; });

  constructor() {
    inject(DestroyRef).onDestroy(() => { this.vivo = false; this.cierre?.(); this.cierre = null; });
    afterNextRender(() => this.arrancar());
  }

  protected reintentar(): void { this.arrancar(); }

  private arrancar(): void {
    this.fallo.set(false);
    import('three')
      .then((THREE) => {
        if (!this.vivo) return;
        this.cierre?.();
        let actual = this.montar(THREE);
        // si cambia el tema (claro/oscuro o el color), se vuelve a montar con la paleta nueva; sin perder el contexto WebGL, que el lienzo sigue siendo el mismo
        const obs = new MutationObserver(() => { if (!this.vivo) return; actual(false); actual = this.montar(THREE); });
        obs.observe(document.documentElement, { attributes: true, attributeFilter: ['data-mode', 'data-color', 'data-tema', 'style'] });
        this.cierre = () => { obs.disconnect(); actual(true); };
      })
      .catch(() => { if (this.vivo) this.fallo.set(true); }); // sin WebGL, contexto agotado o chunk viejo en la caché
  }

  private montar(THREE: Three): (perderContexto?: boolean) => void {
    const canvas = this.lienzo().nativeElement, svg = this.lineas().nativeElement, cont = this.etqs().nativeElement;
    svg.replaceChildren(); cont.replaceChildren();
    const reducido = matchMedia('(prefers-reduced-motion: reduce)').matches;
    const cs = getComputedStyle(document.documentElement);
    const claro = document.documentElement.dataset['mode'] === 'light';
    const brand = new THREE.Color(resolverColor(cs.getPropertyValue('--brand'), '#ff7a1f'));
    const blanco = new THREE.Color(0xffffff), azul = new THREE.Color(0x4f7cff), verde = new THREE.Color(claro ? 0x0f7a42 : 0x3ddc84);
    const mezcla = (a: THREE.Color, b: THREE.Color, k: number) => a.clone().lerp(b, k);
    const hex = (c: THREE.Color) => '#' + c.getHexString();
    const P = claro ? {
      capa: mezcla(blanco, brand, .06), gris: mezcla(blanco, brand, .12), placa: mezcla(blanco, brand, .05), borde: brand.clone().multiplyScalar(.55), metal: new THREE.Color(0xcfd3d8),
      apagado: mezcla(blanco, brand, .2), pcb: hex(mezcla(blanco, brand, .06)), cobre: new THREE.Color(0xc7774f), sombra: hex(brand.clone().multiplyScalar(.3)), luz: 1,
    } : {
      capa: new THREE.Color(0x141b22), gris: new THREE.Color(0x242d36), placa: new THREE.Color(0x0f171d), borde: mezcla(brand, blanco, .25), metal: new THREE.Color(0x8d98a3),
      apagado: new THREE.Color(0x2a323a), pcb: '#0e151b', cobre: new THREE.Color(0xd08a5c), sombra: '#000000', luz: .8,
    };

    const renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true });
    renderer.setClearColor(0x000000, 0);
    const scene = new THREE.Scene();
    const camera = new THREE.PerspectiveCamera(34, 1, 0.1, 80);
    camera.position.set(4, 6.2, 11.2); camera.lookAt(0, 2.95, 0);
    scene.add(new THREE.HemisphereLight(0xffffff, claro ? 0xd9b5b6 : 0x222a33, claro ? 3 : 2.4)); // three 0.187: las luces ya no llevan el factor π de antes
    const sol = new THREE.DirectionalLight(0xffffff, claro ? 2.7 : 3.1); sol.position.set(4, 8, 5); scene.add(sol);
    const luzCpu = new THREE.PointLight(brand, 6, 5, 2); scene.add(luzCpu);
    const grp = new THREE.Group(); scene.add(grp); grp.rotation.y = -0.55;

    const materiales: THREE.Material[] = [], geos: THREE.BufferGeometry[] = [], texturas: THREE.Texture[] = [];
    const mat = (color: THREE.Color | number, op: THREE.MeshStandardMaterialParameters = {}) => {
      const m = new THREE.MeshStandardMaterial({ color, roughness: 0.55, metalness: 0.15, transparent: true, ...op });
      m.userData['op0'] = op.opacity ?? 1; materiales.push(m); return m;
    };
    const geo = <T extends THREE.BufferGeometry>(g: T): T => { geos.push(g); return g; };
    const placa = (w: number, d: number, h: number, rad: number, m: THREE.Material) => {
      const s = new THREE.Shape(), x = -w / 2, y = -d / 2;
      s.moveTo(x + rad, y); s.lineTo(x + w - rad, y); s.quadraticCurveTo(x + w, y, x + w, y + rad); s.lineTo(x + w, y + d - rad); s.quadraticCurveTo(x + w, y + d, x + w - rad, y + d);
      s.lineTo(x + rad, y + d); s.quadraticCurveTo(x, y + d, x, y + d - rad); s.lineTo(x, y + rad); s.quadraticCurveTo(x, y, x + rad, y);
      const g = geo(new THREE.ExtrudeGeometry(s, { depth: h, bevelEnabled: true, bevelThickness: 0.01, bevelSize: 0.01, bevelSegments: 2 })); g.rotateX(-Math.PI / 2);
      return new THREE.Mesh(g, m);
    };
    const caja = (w: number, h: number, d: number, m: THREE.Material, x = 0, y = 0, z = 0) => { const b = new THREE.Mesh(geo(new THREE.BoxGeometry(w, h, d)), m); b.position.set(x, y + h / 2, z); return b; };
    const filo = <T extends THREE.Mesh>(mesh: T, op = 0.4): T => {
      const lm = new THREE.LineBasicMaterial({ color: P.borde, transparent: true, opacity: op }); lm.userData['op0'] = op; materiales.push(lm);
      mesh.add(new THREE.LineSegments(geo(new THREE.EdgesGeometry(mesh.geometry, 28)), lm)); return mesh;
    };

    const partes = {} as Record<Id, Parte>, picks: THREE.Mesh[] = [];
    const parte = (id: Id, g: THREE.Group, nivel: number, lift: number, base: number, centro: [number, number, number] = [0, 0.1, 0]) => {
      const p: Parte = { id, g, nivel, lift, base, a: 1, mats: new Set(), centro: new THREE.Vector3(...centro) };
      g.traverse((n) => {
        const m = (n as THREE.Mesh).material as THREE.Material | undefined;
        if (m) { p.mats.add(m); if (m.userData['op0'] === undefined) m.userData['op0'] = m.opacity; if ((n as THREE.Mesh).isMesh) { n.userData['parte'] = id; picks.push(n as THREE.Mesh); } }
      });
      partes[id] = p; grp.add(g); return p;
    };

    // —— batería: cinco celdas que se llenan según la carga ——
    const barrasBat: THREE.Mesh[] = [];
    { const g = new THREE.Group(); g.add(filo(placa(4.3, 2.8, 0.1, 0.2, mat(P.gris))));
      for (let i = 0; i < 5; i++) {
        g.add(filo(caja(0.74, 0.08, 2.25, mat(P.apagado, { opacity: 0.55 }), -1.62 + i * 0.81, 0.1, 0), 0.25));
        const gb = geo(new THREE.BoxGeometry(0.66, 0.09, 1)); gb.translate(0, 0, 0.5);
        const b = new THREE.Mesh(gb, mat(brand, { emissive: brand, emissiveIntensity: 0.35 })); b.position.set(-1.62 + i * 0.81, 0.15, -1.075); g.add(b); barrasBat.push(b);
      }
      parte('bat', g, 0, 0, 0.02); }
    // —— placa base con circuitos ——
    { const g = new THREE.Group(); g.add(filo(placa(4.2, 2.7, 0.08, 0.16, mat(P.placa))));
      const c = document.createElement('canvas'); c.width = 840; c.height = 540; const x = c.getContext('2d')!; x.fillStyle = P.pcb; x.fillRect(0, 0, 840, 540);
      x.strokeStyle = hex(brand); x.globalAlpha = 0.55; x.lineWidth = 2.2; let sd = 7; const rnd = () => (sd = (sd * 16807) % 2147483647) / 2147483647;
      for (let i = 0; i < 90; i++) {
        let px = 420 + (rnd() - 0.5) * 120, py = 270 + (rnd() - 0.5) * 120; x.beginPath(); x.moveTo(px, py);
        for (let k = 0; k < 4; k++) { const dir = Math.floor(rnd() * 4), len = 40 + rnd() * 150; px += [len, 0, -len, 0][dir]; py += [0, len, 0, -len][dir]; x.lineTo(px, py); if (rnd() > 0.5) { px += (rnd() > 0.5 ? 1 : -1) * 30; py += (rnd() > 0.5 ? 1 : -1) * 30; x.lineTo(px, py); } }
        x.stroke(); x.fillStyle = hex(brand); x.beginPath(); x.arc(px, py, 4, 0, 6.283); x.fill();
      }
      const tex = new THREE.CanvasTexture(c); texturas.push(tex);
      const pm = new THREE.MeshStandardMaterial({ map: tex, roughness: 0.8, transparent: true }); pm.userData['op0'] = 1; materiales.push(pm);
      const pl = new THREE.Mesh(geo(new THREE.PlaneGeometry(4.15, 2.65)), pm); pl.rotation.x = -Math.PI / 2; pl.position.y = 0.092; g.add(pl);
      parte('placa', g, 1, 0, 0.16); }
    // —— procesador ——
    const dieCpu = mat(brand, { emissive: brand, emissiveIntensity: 0.8, metalness: 0.3 });
    { const g = new THREE.Group(); g.add(filo(caja(1.05, 0.05, 1.05, mat(P.gris))));
      g.add(filo(caja(0.85, 0.07, 0.85, mat(P.metal, { metalness: 0.5, roughness: 0.35 }), 0, 0.05, 0)));
      g.add(filo(caja(0.5, 0.06, 0.5, dieCpu, 0, 0.12, 0), 0.6));
      for (let i = 0; i < 6; i++) g.add(caja(0.05, 0.03, 0.85, mat(P.borde, { opacity: 0.45 }), -0.4 + i * 0.16, 0.12, 0));
      g.position.set(-0.25, 0, 0); parte('cpu', g, 2, 0, 0.26, [0, 0.15, 0]); }
    // —— gráfica ——
    { const g = new THREE.Group(); g.add(filo(caja(1.35, 0.05, 0.95, mat(P.gris))));
      g.add(filo(caja(0.5, 0.08, 0.5, mat(azul, { emissive: azul, emissiveIntensity: 0.45, metalness: 0.3 }), -0.1, 0.05, 0), 0.6));
      ([[0.5, 0.25], [0.5, -0.25], [0.5, 0], [-0.55, 0.3]] as [number, number][]).forEach(([x, z]) => g.add(caja(0.2, 0.05, 0.22, mat(P.metal, { metalness: 0.5 }), x, 0.05, z)));
      g.position.set(1.45, 0, -0.45); parte('gpu', g, 2, 0.3, 0.26); }
    // —— memoria: dieciséis chips que se encienden según el uso ——
    const chipsRam: THREE.Mesh[] = [];
    { const g = new THREE.Group();
      for (let mdl = 0; mdl < 2; mdl++) {
        const z = mdl * 0.44; g.add(filo(caja(1.6, 0.035, 0.34, mat(verde), 0, 0, z), 0.3));
        for (let i = 0; i < 8; i++) { const ch = caja(0.15, 0.05, 0.2, mat(0x30343a, { roughness: 0.4 }), -0.67 + i * 0.19, 0.035, z); g.add(ch); chipsRam.push(ch); }
      }
      g.position.set(-1.35, 0, -0.9); parte('ram', g, 2, 0.55, 0.26, [0, 0.1, 0.2]); }
    // —— disco: veinte segmentos de capacidad ——
    const segsSsd: THREE.Mesh[] = [];
    { const g = new THREE.Group(); g.add(filo(caja(1.45, 0.035, 0.36, mat(claro ? 0x6f86a3 : 0x23384f), 0, 0, 0), 0.3));
      for (let i = 0; i < 20; i++) { const sg = caja(0.055, 0.05, 0.1, mat(0x3b4755), -0.66 + i * 0.068, 0.035, 0.11); g.add(sg); segsSsd.push(sg); }
      [-0.4, 0.1].forEach((x) => g.add(caja(0.4, 0.05, 0.17, mat(0x1b2733, { metalness: 0.4 }), x, 0.035, -0.07)));
      g.position.set(-1.35, 0, 0.55); parte('ssd', g, 2, 0.12, 0.26); }
    // —— refrigeración: ventiladores con aspas, tubos de calor, aletas y aire ——
    const ventis: { rot: THREE.Group; borr: THREE.Mesh; v: number }[] = [];
    const cobreMats: THREE.MeshStandardMaterial[] = [];
    let aire!: { pts: THREE.Points; meta: { k: number; t: number; z: number; y: number }[]; ps: Float32Array; pm: THREE.PointsMaterial };
    { const g = new THREE.Group();
      ([[-1.5, 0.55], [1.5, 0.55]] as [number, number][]).forEach(([x, z], k) => {
        const f = new THREE.Group(); f.position.set(x, 0.05, z);
        const aro = new THREE.Mesh(geo(new THREE.TorusGeometry(0.66, 0.035, 10, 56)), mat(P.metal, { metalness: 0.6, roughness: 0.3 })); aro.rotation.x = Math.PI / 2; f.add(aro);
        const marco = placa(1.5, 1.5, 0.06, 0.18, mat(P.gris, { opacity: 0.55 })); marco.position.y = -0.06; f.add(filo(marco, 0.3));
        const rot = new THREE.Group();
        for (let i = 0; i < 9; i++) { const b = caja(0.4, 0.018, 0.17, mat(brand, { opacity: 0.85, roughness: 0.4 })); b.rotation.x = 0.5; b.position.x = 0.3; const h = new THREE.Group(); h.add(b); h.rotation.y = i * 6.283 / 9; rot.add(h); }
        f.add(rot);
        const hub = new THREE.Mesh(geo(new THREE.CylinderGeometry(0.12, 0.12, 0.07, 24)), mat(P.borde, { metalness: 0.5 })); hub.position.y = 0.03; f.add(hub);
        const borr = new THREE.Mesh(geo(new THREE.CircleGeometry(0.6, 40)), mat(brand, { opacity: 0.12 })); borr.rotation.x = -Math.PI / 2; borr.position.y = 0.045; f.add(borr);
        ventis.push({ rot, borr, v: k ? 0.22 : 0.27 }); g.add(f);
        for (let i = 0; i < 10; i++) g.add(caja(0.02, 0.16, 0.95, mat(P.metal, { metalness: 0.6, roughness: 0.3 }), x + (x < 0 ? -0.95 : 0.95) + (x < 0 ? -1 : 1) * i * 0.055, 0, z - 0.05));
      });
      ([[-1, 0.12], [-1, -0.12], [1, 0.12], [1, -0.12]] as [number, number][]).forEach(([s, dz]) => {
        const cu = new THREE.CatmullRomCurve3([new THREE.Vector3(-0.25, 0.09, dz), new THREE.Vector3(-0.25 + s * 0.6, 0.09, dz * 2 + 0.2 * (s > 0 ? -1 : 1)), new THREE.Vector3(s * 1.6, 0.09, dz + 0.45), new THREE.Vector3(s * 2.15, 0.09, 0.5 + dz)]);
        const mc = mat(P.cobre, { metalness: 0.85, roughness: 0.28 }); cobreMats.push(mc); g.add(new THREE.Mesh(geo(new THREE.TubeGeometry(cu, 36, 0.042, 10)), mc));
      });
      const n = 120, ps = new Float32Array(n * 3), meta: { k: number; t: number; z: number; y: number }[] = [];
      for (let i = 0; i < n; i++) meta.push({ k: i % 2, t: Math.random(), z: (Math.random() - 0.5) * 0.8, y: Math.random() * 0.12 });
      const gp = geo(new THREE.BufferGeometry()); gp.setAttribute('position', new THREE.BufferAttribute(ps, 3));
      const pm = new THREE.PointsMaterial({ color: azul, size: 0.07, transparent: true, opacity: 0.35, depthWrite: false }); pm.userData['op0'] = 0.35; materiales.push(pm);
      aire = { pts: new THREE.Points(gp, pm), meta, ps, pm }; g.add(aire.pts);
      parte('vent', g, 3, 0, 0.46, [1.5, 0.1, 0.5]); }
    // —— tapa transparente ——
    { const g = new THREE.Group(); g.add(filo(placa(4.5, 2.95, 0.05, 0.22, mat(0xffffff, { opacity: claro ? 0.12 : 0.06, roughness: 0.1 })), 0.55)); parte('tapa', g, 4.1, 0, 0.62); }
    // sombra suave en el suelo
    const sc2 = document.createElement('canvas'); sc2.width = sc2.height = 256; const sx = sc2.getContext('2d')!, gr = sx.createRadialGradient(128, 128, 10, 128, 128, 128);
    const sombra = new THREE.Color(P.sombra), rgb = `${Math.round(sombra.r * 255)},${Math.round(sombra.g * 255)},${Math.round(sombra.b * 255)}`;
    gr.addColorStop(0, `rgba(${rgb},${claro ? 0.34 : 0.5})`); gr.addColorStop(1, `rgba(${rgb},0)`); sx.fillStyle = gr; sx.fillRect(0, 0, 256, 256);
    const texSombra = new THREE.CanvasTexture(sc2); texturas.push(texSombra);
    const suelo = new THREE.Mesh(geo(new THREE.PlaneGeometry(8, 8)), new THREE.MeshBasicMaterial({ map: texSombra, transparent: true, depthWrite: false })); suelo.rotation.x = -Math.PI / 2; suelo.position.y = -0.4; scene.add(suelo); materiales.push(suelo.material as THREE.Material);
    // anillo de escaneo al montarse
    const anillo = new THREE.Mesh(geo(new THREE.TorusGeometry(3.1, 0.018, 8, 96)), new THREE.MeshBasicMaterial({ color: brand, transparent: true })); anillo.rotation.x = Math.PI / 2; grp.add(anillo); materiales.push(anillo.material as THREE.Material);
    // guías verticales desde cada pieza hasta su sitio
    const guias = (['cpu', 'gpu', 'ram', 'ssd'] as Id[]).map((id) => {
      const lm = new THREE.LineDashedMaterial({ color: P.borde, dashSize: 0.08, gapSize: 0.06, transparent: true, opacity: 0.45 }); materiales.push(lm);
      const g = new THREE.Line(geo(new THREE.BufferGeometry().setFromPoints([new THREE.Vector3(), new THREE.Vector3(0, 1, 0)])), lm); grp.add(g); return { id, g, lm };
    });

    // etiquetas
    const etqs = ETIQUETADAS.map((k) => {
      const el = document.createElement('div'); el.className = 'p3-et'; el.addEventListener('click', () => this.sel.set(this.sel() === k ? null : k)); cont.appendChild(el);
      const ln = document.createElementNS('http://www.w3.org/2000/svg', 'line'), pt = document.createElementNS('http://www.w3.org/2000/svg', 'circle'); pt.setAttribute('r', '3'); svg.append(ln, pt);
      return { k, el, ln, pt, texto: '' };
    });

    // —— interacción ——
    let hover: Id | null = null, arrastre = false, x0 = 0, movido = 0, manual = 0;
    const ray = new THREE.Raycaster(), ratón = new THREE.Vector2();
    const abajo = (e: PointerEvent) => { arrastre = true; x0 = e.clientX; movido = 0; canvas.setPointerCapture(e.pointerId); };
    const arriba = () => { arrastre = false; if (movido < 4 && hover && hover !== 'placa' && hover !== 'tapa') this.sel.set(this.sel() === hover ? null : hover); };
    const mueve = (e: PointerEvent) => {
      if (arrastre) { grp.rotation.y += (e.clientX - x0) * 0.008; movido += Math.abs(e.clientX - x0); x0 = e.clientX; manual = performance.now(); return; }
      const b = canvas.getBoundingClientRect(); ratón.set(((e.clientX - b.left) / b.width) * 2 - 1, -((e.clientY - b.top) / b.height) * 2 + 1); ray.setFromCamera(ratón, camera);
      const h = ray.intersectObjects(picks.filter((m) => m.userData['parte'] !== 'tapa'), false)[0];
      hover = h ? (h.object.userData['parte'] as Id) : null; canvas.style.cursor = hover ? 'pointer' : 'grab';
    };
    const fuera = () => { hover = null; };
    canvas.addEventListener('pointerdown', abajo); canvas.addEventListener('pointerup', arriba); canvas.addEventListener('pointermove', mueve); canvas.addEventListener('pointerleave', fuera);

    // —— tamaño y visibilidad ——
    let w = 0, h = 0, visible = true;
    const ajustar = () => { const r = canvas.getBoundingClientRect(); w = r.width; h = r.height; if (w < 2 || h < 2) return; renderer.setPixelRatio(Math.min(devicePixelRatio, 2)); renderer.setSize(w, h, false); camera.aspect = w / h; camera.updateProjectionMatrix(); };
    const observador = new ResizeObserver(ajustar); observador.observe(canvas); ajustar();
    const visibilidad = new IntersectionObserver((es) => { visible = es.some((x) => x.isIntersecting); }); visibilidad.observe(canvas);

    // —— el bucle ——
    const OBJ: Record<Vista, Partial<Record<Id, number>>> = { normal: {}, uso: { cpu: 0.5, gpu: 0.5, vent: 0.45, placa: 0.6, tapa: 0.5 }, calor: { ram: 0.22, ssd: 0.22, bat: 0.22, placa: 0.35, tapa: 0.3 }, aire: { bat: 0.25, ram: 0.35, ssd: 0.35, gpu: 0.4, cpu: 0.55, placa: 0.3, tapa: 0.3 } };
    const ease = (k: number) => 1 - Math.pow(1 - k, 3), calor = (t: number) => new THREE.Color().lerpColors(azul, new THREE.Color(0xff5a1f), Math.min(1, Math.max(0, t)));
    const v = new THREE.Vector3(), GAP = 1.05, t0 = performance.now(), colEt = 150;
    let marcoId = 0, cerrado = false, cpuS = 0, memS = 0, discoS = 0, batS = 0, tempS = 0.5;
    const bucle = (t: number) => {
      if (cerrado) return;
      marcoId = requestAnimationFrame(bucle);
      if (!visible || w < 2 || h < 2) return;
      const e = t - t0, d = this.datos();
      const ex = reducido ? this.despiece() / 100 : ease(Math.max(0, Math.min(1, (e - 700) / 2400))) * (this.despiece() / 100), pulso = 0.5 + 0.5 * Math.sin(e / 320);
      // niveles suavizados
      const k = reducido ? 1 : 0.06; cpuS += (d.cpu / 100 - cpuS) * k; memS += (d.memoria / 100 - memS) * k; discoS += (d.disco / 100 - discoS) * k;
      const bat = this.bateria(); batS += (((bat ?? 100) / 100) - batS) * k; const tc = this.tempCpu(); tempS += (((tc != null ? (tc - 35) / 55 : cpuS * 0.8 + 0.1)) - tempS) * k;
      barrasBat.forEach((b, i) => { const f = Math.max(0, Math.min(1, batS * 5 - i)); b.scale.z = Math.max(0.0001, 2.15 * f); b.visible = f > 0.001; });
      chipsRam.forEach((c, i) => { const lit = i < memS * 16, m = (c.material as THREE.MeshStandardMaterial); m.color.setHex(lit ? 0x4f7cff : 0x30343a); m.emissive.setHex(lit ? 0x4f7cff : 0x000000); m.emissiveIntensity = lit ? (this.vista() === 'uso' ? 1.2 : 0.6) + 0.3 * Math.sin(e / 300 + i) : 0; });
      segsSsd.forEach((s, i) => { const lit = i < discoS * 20, m = (s.material as THREE.MeshStandardMaterial); m.color.copy(lit ? brand : new THREE.Color(0x3b4755)); m.emissive.copy(lit ? brand : new THREE.Color(0x000000)); m.emissiveIntensity = lit ? 0.7 : 0; });
      const vista = this.vista(), act = hover ?? this.sel();
      Object.values(partes).forEach((p, i) => {
        const ap = reducido ? 1 : ease(Math.max(0, Math.min(1, (e - 150 - i * 140) / 700)));
        p.g.position.y = p.base + (p.nivel * GAP + p.lift) * ex; p.g.scale.setScalar(0.9 + 0.1 * ap);
        let objetivo = (OBJ[vista][p.id] ?? 1) * ap; if (act) objetivo *= p.id === act ? 1 : 0.42;
        p.a += (objetivo - p.a) * 0.12;
        p.mats.forEach((m) => { m.opacity += ((m.userData['op0'] as number) * p.a - m.opacity) * 0.35; });
      });
      dieCpu.emissive.copy(vista === 'calor' ? calor(tempS) : brand); dieCpu.emissiveIntensity = (vista === 'calor' ? 1.4 : 0.5 + cpuS * 0.9) + pulso * 0.4 * (act === 'cpu' ? 2 : 1);
      cobreMats.forEach((m) => m.emissive.copy(vista === 'calor' ? new THREE.Color(0xff5a1f) : new THREE.Color(0x000000)));
      luzCpu.position.copy(partes.cpu.g.getWorldPosition(v)).add(new THREE.Vector3(0, 0.5, 0)); luzCpu.intensity = (vista === 'calor' ? 14 : 6) * (0.6 + cpuS * 0.8 + pulso * 0.3);
      if (!reducido) ventis.forEach((a) => { a.rot.rotation.y -= a.v * (vista === 'aire' ? 1.8 : 1) * (0.4 + cpuS * 1.2 + tempS * 0.4); });
      ventis.forEach((a) => { (a.borr.material as THREE.MeshStandardMaterial).opacity = Math.min(0.3, 0.1 + a.v * 0.5) * partes.vent.a; });
      // aire
      aire.pm.opacity = vista === 'aire' ? 0.9 : 0.3; aire.pm.size = vista === 'aire' ? 0.09 : 0.06;
      for (let i = 0; i < aire.meta.length; i++) {
        const m = aire.meta[i]; if (!reducido) m.t += 0.006 * (vista === 'aire' ? 1.8 : 1); if (m.t > 1) { m.t = 0; m.z = (Math.random() - 0.5) * 0.8; }
        const s = m.k ? 1 : -1, dd = m.t * 1.6; aire.ps[i * 3] = s * (1.7 + dd * 1.2); aire.ps[i * 3 + 1] = 0.1 + m.y + Math.sin(m.t * 6 + i) * 0.03; aire.ps[i * 3 + 2] = 0.55 + m.z;
      }
      aire.pts.geometry.attributes['position'].needsUpdate = true;
      // anillo de escaneo, sombra y guías
      const kk = reducido ? 1 : Math.max(0, Math.min(1, (e - 500) / 2200)); anillo.position.y = kk * (4.6 * GAP) + 0.05; (anillo.material as THREE.MeshBasicMaterial).opacity = kk > 0 && kk < 1 ? 0.85 * Math.sin(kk * Math.PI) : 0;
      suelo.scale.setScalar(0.85 + 0.25 * ex);
      guias.forEach(({ id, g, lm }) => { const p = partes[id], a = g.geometry.attributes['position']; p.g.getWorldPosition(v); grp.worldToLocal(v); a.setXYZ(0, v.x, partes.placa.g.position.y + 0.1, v.z); a.setXYZ(1, v.x, p.g.position.y, v.z); a.needsUpdate = true; g.computeLineDistances(); lm.opacity = 0.45 * ex; });
      if (!reducido && this.gira() && !(manual && t - manual < 2500)) grp.rotation.y += 0.0014;
      renderer.render(scene, camera);
      // etiquetas en columna a la derecha, ordenadas por altura
      const cifras = this.cifras();
      const pts = etqs.map((q) => { const p = partes[q.k]; v.copy(p.centro); p.g.localToWorld(v); const pr = v.project(camera); return { q, x: (pr.x * 0.5 + 0.5) * w, y: (-pr.y * 0.5 + 0.5) * h }; }).sort((a, b) => a.y - b.y);
      let minY = 70; const ex2 = w - colEt;
      pts.forEach(({ q, x, y }) => {
        const ey = Math.max(y, minY); minY = ey + 44;
        const texto = `<b>${NOMBRES[q.k]}</b>${cifras[q.k].linea}`; if (q.texto !== texto) { q.texto = texto; q.el.innerHTML = texto; }
        q.el.style.transform = `translate(${ex2 + 10}px, ${ey - 14}px)`; q.el.classList.toggle('activo', q.k === act);
        q.ln.setAttribute('x1', String(x)); q.ln.setAttribute('y1', String(y)); q.ln.setAttribute('x2', String(ex2)); q.ln.setAttribute('y2', String(ey)); q.pt.setAttribute('cx', String(x)); q.pt.setAttribute('cy', String(y));
      });
    };
    marcoId = requestAnimationFrame(bucle);

    return (perderContexto = true) => {
      cerrado = true; cancelAnimationFrame(marcoId); observador.disconnect(); visibilidad.disconnect();
      canvas.removeEventListener('pointerdown', abajo); canvas.removeEventListener('pointerup', arriba); canvas.removeEventListener('pointermove', mueve); canvas.removeEventListener('pointerleave', fuera);
      geos.forEach((g) => g.dispose()); materiales.forEach((m) => m.dispose()); texturas.forEach((x) => x.dispose());
      renderer.dispose(); if (perderContexto) renderer.forceContextLoss(); svg.replaceChildren(); cont.replaceChildren();
    };
  }
}
