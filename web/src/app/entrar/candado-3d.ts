import * as THREE from 'three';
import { RoundedBoxGeometry } from 'three/examples/jsm/geometries/RoundedBoxGeometry.js';
import { RoomEnvironment } from 'three/examples/jsm/environments/RoomEnvironment.js';
import { resolverColor } from '../core/color-css';

const VERDE = new THREE.Color('#3ddc84');
const ROJO = new THREE.Color('#ff4438');
const ease = (x: number) => 1 - Math.pow(1 - Math.min(1, Math.max(0, x)), 3);

// Escena de la pantalla de entrar: un candado de metal (cuerpo pulido con reflejos, arco de acero, cilindro con su llave y un LED de estado),
// anillos holográficos y de partículas en órbita, rejilla que avanza hacia el horizonte y estrellas.
//   intento(): el LED parpadea y un anillo escanea el candado mientras se comprueba;
//   abrir():   la llave gira, el LED pasa a verde, el arco sube y se abre girando sobre su pata;
//   rechazar(): el LED se pone rojo, el candado se sacude y el arco intenta subir y cae.
export function crearCandado(canvas: HTMLCanvasElement) {
  const reduced = matchMedia('(prefers-reduced-motion: reduce)').matches;
  const renderer = new THREE.WebGLRenderer({ canvas, antialias: true });
  renderer.toneMapping = THREE.ACESFilmicToneMapping;
  renderer.toneMappingExposure = 1.05;
  const scene = new THREE.Scene();
  const camera = new THREE.PerspectiveCamera(40, 1, 0.1, 120);
  camera.position.set(0, 1.2, 9);
  const small = () => innerWidth < 860;

  // los reflejos del metal salen de un cuarto de estudio generado al vuelo (no hay que bajar ninguna imagen)
  const pmrem = new THREE.PMREMGenerator(renderer);
  const sala = new RoomEnvironment();
  const entorno = pmrem.fromScene(sala, 0.04).texture;
  scene.environment = entorno;
  scene.environmentIntensity = 0.6;
  sala.dispose();

  const acero = new THREE.MeshPhysicalMaterial({ color: 0xd6dce6, metalness: 1, roughness: 0.14, clearcoat: 1, clearcoatRoughness: 0.08 });
  const cuerpoMat = new THREE.MeshPhysicalMaterial({ color: 0x232d3d, metalness: 0.95, roughness: 0.3, clearcoat: 0.7, clearcoatRoughness: 0.25 });
  const placaMat = new THREE.MeshPhysicalMaterial({ color: 0x3b475c, metalness: 1, roughness: 0.42 });
  const oscuroMat = new THREE.MeshStandardMaterial({ color: 0x05080c, roughness: 0.6, metalness: 0.2 });
  const brilloMat = new THREE.MeshBasicMaterial({ transparent: true, opacity: 0.9 });
  const ledMat = new THREE.MeshBasicMaterial();
  const lineMat = new THREE.LineBasicMaterial({ transparent: true, opacity: 0.35 });

  const grid = new THREE.GridHelper(80, 64);
  (grid.material as THREE.Material).transparent = true;
  (grid.material as THREE.Material).opacity = 0.3;
  grid.position.y = -2.6;
  scene.add(grid);

  // ---- candado ----
  const lock = new THREE.Group();
  lock.add(new THREE.Mesh(new RoundedBoxGeometry(2.4, 1.9, 1, 5, 0.2), cuerpoMat));
  const placa = new THREE.Mesh(new RoundedBoxGeometry(2.0, 1.5, 0.08, 4, 0.12), placaMat); placa.position.z = 0.5; lock.add(placa);
  const aro = new THREE.Mesh(new THREE.TorusGeometry(0.46, 0.022, 12, 64), brilloMat); aro.position.set(0, -0.05, 0.545); lock.add(aro);
  for (const [x, y] of [[-0.88, 0.62], [0.88, 0.62], [-0.88, -0.62], [0.88, -0.62]]) {
    const tornillo = new THREE.Mesh(new THREE.SphereGeometry(0.045, 16, 12), acero); tornillo.position.set(x, y, 0.55); lock.add(tornillo);
  }
  for (let i = 0; i < 3; i++) { // tres rayitas de adorno
    const raya = new THREE.Mesh(new THREE.BoxGeometry(0.34 - i * 0.07, 0.018, 0.01), brilloMat); raya.position.set(-0.62 + (0.34 - i * 0.07) / 2 - 0.17, -0.62 - i * 0.05, 0.545); lock.add(raya);
  }
  // cilindro de la cerradura con su ranura: gira al abrir
  const llave = new THREE.Group(); llave.position.set(0, -0.05, 0.56);
  const cilindro = new THREE.Mesh(new THREE.CylinderGeometry(0.34, 0.34, 0.1, 48).rotateX(Math.PI / 2), acero);
  const ranura = new THREE.Mesh(new THREE.BoxGeometry(0.075, 0.27, 0.04), oscuroMat); ranura.position.z = 0.05;
  const punto = new THREE.Mesh(new THREE.CircleGeometry(0.06, 20), oscuroMat); punto.position.set(0, 0.17, 0.052);
  llave.add(cilindro, ranura, punto); lock.add(llave);
  // LED de estado con su halo
  const led = new THREE.Mesh(new THREE.SphereGeometry(0.055, 20, 14), ledMat); led.position.set(0.82, -0.66, 0.56); lock.add(led);
  const halo = document.createElement('canvas'); halo.width = halo.height = 64;
  const hc = halo.getContext('2d')!; const g = hc.createRadialGradient(32, 32, 0, 32, 32, 32); g.addColorStop(0, 'rgba(255,255,255,1)'); g.addColorStop(1, 'rgba(255,255,255,0)'); hc.fillStyle = g; hc.fillRect(0, 0, 64, 64);
  const haloTex = new THREE.CanvasTexture(halo);
  const haloMat = new THREE.SpriteMaterial({ map: haloTex, transparent: true, blending: THREE.AdditiveBlending, depthWrite: false });
  const haloLed = new THREE.Sprite(haloMat); haloLed.position.set(0.82, -0.66, 0.62); haloLed.scale.setScalar(0.45); lock.add(haloLed);

  // arco: un tubo en U con la pata derecha más corta, como en un candado de verdad; gira sobre la pata izquierda
  const pts: THREE.Vector3[] = [];
  for (let i = 0; i <= 24; i++) pts.push(new THREE.Vector3(-0.72, -0.5 + (i / 24) * 1.2, 0));
  for (let i = 1; i < 48; i++) { const a = Math.PI - (i / 48) * Math.PI; pts.push(new THREE.Vector3(Math.cos(a) * 0.72, 0.7 + Math.sin(a) * 0.72, 0)); }
  for (let i = 0; i <= 20; i++) pts.push(new THREE.Vector3(0.72, 0.7 - (i / 20) * 0.95, 0));
  const arco = new THREE.Mesh(new THREE.TubeGeometry(new THREE.CatmullRomCurve3(pts, false, 'centripetal'), 220, 0.13, 20), acero);
  arco.position.x = 0.72;
  const pivote = new THREE.Group(); pivote.position.set(-0.72, 0.95, 0); pivote.add(arco);
  lock.add(pivote);
  scene.add(lock);

  // anillos holográficos y de partículas
  const aros = [[2.5, 1.2, 0.2], [3.0, -0.5, 0.9]].map(([r, tx, tz], i) => {
    const m = new THREE.Mesh(new THREE.TorusGeometry(r, 0.008, 8, 200), new THREE.MeshBasicMaterial({ transparent: true, opacity: 0.55, blending: THREE.AdditiveBlending, depthWrite: false }));
    m.rotation.set(tx, 0, tz); m.userData['v'] = 0.25 + i * 0.12; lock.add(m); return m;
  });
  const escaner = new THREE.Mesh(new THREE.TorusGeometry(1.45, 0.012, 8, 120), new THREE.MeshBasicMaterial({ transparent: true, opacity: 0, blending: THREE.AdditiveBlending, depthWrite: false }));
  escaner.rotation.x = Math.PI / 2; lock.add(escaner);

  const dot = document.createElement('canvas'); dot.width = dot.height = 32;
  const dc = dot.getContext('2d')!; dc.beginPath(); dc.arc(16, 16, 14, 0, Math.PI * 2); dc.fillStyle = '#fff'; dc.fill();
  const dotTex = new THREE.CanvasTexture(dot);
  const rings: THREE.Points[] = [];
  ([[2.3, 0.4, 0.2], [2.8, -0.6, 0.9], [3.3, 1.1, -0.5]] as const).forEach(([r, tx, tz], k) => {
    const n = 220, pos = new Float32Array(n * 3);
    for (let i = 0; i < n; i++) {
      const a = (i / n) * Math.PI * 2, j = (Math.random() - 0.5) * 0.12;
      pos.set([Math.cos(a) * (r + j), (Math.random() - 0.5) * 0.08, Math.sin(a) * (r + j)], i * 3);
    }
    const geo = new THREE.BufferGeometry(); geo.setAttribute('position', new THREE.BufferAttribute(pos, 3));
    const ring = new THREE.Points(geo, new THREE.PointsMaterial({ size: 0.05, map: dotTex, transparent: true, alphaTest: 0.3, opacity: k ? 0.7 : 1 }));
    ring.rotation.set(tx, 0, tz);
    ring.userData['speed'] = 0.2 + k * 0.08;
    lock.add(ring); rings.push(ring);
  });

  const starN = small() ? 500 : 1100, starPos = new Float32Array(starN * 3);
  for (let i = 0; i < starN; i++) starPos.set([(Math.random() - 0.5) * 60, Math.random() * 18 - 2, -Math.random() * 40 - 4], i * 3);
  const starGeo = new THREE.BufferGeometry(); starGeo.setAttribute('position', new THREE.BufferAttribute(starPos, 3));
  const starMat = new THREE.PointsMaterial({ size: 0.06, map: dotTex, transparent: true, alphaTest: 0.3, opacity: 0.8 });
  scene.add(new THREE.Points(starGeo, starMat));

  // luces: una principal, un contraluz del color de acento y un relleno frío
  const sol = new THREE.DirectionalLight(0xffffff, 1.7); sol.position.set(3, 4, 6); scene.add(sol);
  const contra = new THREE.PointLight(0xffffff, 45, 14); contra.position.set(-3.2, 1.2, 3); scene.add(contra);
  const frio = new THREE.PointLight(0x5b7bff, 22, 14); frio.position.set(3.2, -1.2, 2.5); scene.add(frio);

  let brand = new THREE.Color('#ff7a1f');
  const paint = () => {
    const cs = getComputedStyle(document.documentElement);
    brand = new THREE.Color(resolverColor(cs.getPropertyValue('--brand'), '#ff7a1f'));
    const dark = new THREE.Color(resolverColor(cs.getPropertyValue('--dark'), '#03080b'));
    const light = new THREE.Color(resolverColor(cs.getPropertyValue('--on-dark'), '#ffffff'));
    renderer.setClearColor(dark, 1);
    scene.fog = new THREE.Fog(dark, 10, 34);
    lineMat.color.copy(brand); brilloMat.color.copy(brand); contra.color.copy(brand);
    aros.forEach((a) => (a.material as THREE.MeshBasicMaterial).color.copy(brand));
    (escaner.material as THREE.MeshBasicMaterial).color.copy(brand);
    (grid.material as THREE.LineBasicMaterial).color.copy(brand);
    rings.forEach((r, i) => (r.material as THREE.PointsMaterial).color.copy(i ? brand : light));
    starMat.color.copy(light);
    if (reduced && typeof render === 'function') render(4000);
  };

  const resize = () => {
    renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
    renderer.setSize(innerWidth, innerHeight, false);
    camera.aspect = innerWidth / innerHeight; camera.updateProjectionMatrix();
    // composición vertical a cualquier ancho: el candado centrado arriba y la tarjeta (entrar.css) centrada debajo
    lock.scale.setScalar(small() ? 0.6 : 0.85);
    lock.position.set(0, small() ? 1.45 : 1.15, 0);
    if (reduced) render(4000); // cambiar el tamaño borra el canvas y sin animación nadie lo vuelve a pintar
  };

  const mouse = { x: 0, y: 0, tx: 0, ty: 0 };
  const onMove = (e: PointerEvent) => { mouse.tx = (e.clientX / innerWidth) * 2 - 1; mouse.ty = -((e.clientY / innerHeight) * 2 - 1); };

  let tryAt = -100, openAt = -100, failAt = -100, raf = 0;
  const ahora = () => performance.now() * 0.001;
  const colorLed = new THREE.Color();
  const render = (time: number) => {
    const t = time * 0.001;
    mouse.x += (mouse.tx - mouse.x) * 0.05; mouse.y += (mouse.ty - mouse.y) * 0.05;
    grid.position.z = (t * 1.2) % (80 / 64);
    lock.rotation.y = Math.sin(t * 0.5) * 0.3 + mouse.x * 0.5;
    lock.rotation.x = -0.08 + mouse.y * -0.25;
    lock.rotation.z = 0;
    aros.forEach((a) => { a.rotation.y = t * (a.userData['v'] as number); });
    rings.forEach((r) => { r.rotation.y = t * (r.userData['speed'] as number); });

    const o = t - openAt, f = t - failAt, k = t - tryAt;
    let subida = 0, giro = 0, llaveZ = 0, escan = 0, resplandor = 0.5 + 0.3 * Math.sin(t * 3);
    colorLed.copy(brand);
    if (o >= 0) {
      // abierto: la llave gira, el arco sube y se abre sobre su pata, los anillos se aceleran
      llaveZ = -ease(o / 0.4) * (Math.PI / 2);
      subida = ease((o - 0.35) / 0.4) * 0.42;
      giro = -ease((o - 0.7) / 0.7) * 1.3;
      rings.forEach((r) => { r.rotation.y += o * 2; });
      aros.forEach((a) => { a.rotation.y += o * 1.5; });
      colorLed.copy(VERDE); resplandor = 1;
      camera.position.z = 9 - ease(o / 1.4) * 0.8;
    } else if (f >= 0 && f < 1) {
      // rechazado: rojo, sacudida, y el arco intenta subir y cae
      const q = 1 - f;
      lock.rotation.z = Math.sin(f * 55) * 0.1 * q;
      llaveZ = Math.sin(f * 40) * 0.12 * q;
      subida = Math.sin(Math.min(1, f / 0.35) * Math.PI) * 0.1;
      colorLed.copy(ROJO); resplandor = 0.4 + 0.6 * Math.abs(Math.sin(f * 14));
    } else if (k >= 0 && k < 6) {
      // comprobando: el LED parpadea, la llave tiembla y un anillo recorre el candado de arriba abajo
      llaveZ = Math.sin(k * 38) * 0.05;
      escan = 1 - Math.abs(((k * 0.9) % 1) * 2 - 1);
      (escaner.material as THREE.MeshBasicMaterial).opacity = 0.7 * Math.sin(Math.PI * ((k * 0.9) % 1));
      escaner.position.y = -1 + 2.2 * ((k * 0.9) % 1);
      resplandor = Math.sin(k * 16) > 0 ? 1 : 0.2;
    }
    if (escan === 0) (escaner.material as THREE.MeshBasicMaterial).opacity = 0;
    llave.rotation.z = llaveZ;
    pivote.position.y = 0.95 + subida;
    pivote.rotation.y = giro;
    ledMat.color.copy(colorLed).multiplyScalar(0.55 + 0.45 * resplandor);
    haloMat.color.copy(colorLed); haloMat.opacity = 0.25 + 0.6 * resplandor;
    if (o >= 0) contra.color.lerp(VERDE, 0.04); else if (f >= 0 && f < 1) contra.color.lerp(ROJO, 0.2); else contra.color.lerp(brand, 0.1);
    camera.position.x = mouse.x * 0.4;
    camera.lookAt(0, 0.6, 0);
    renderer.render(scene, camera);
  };
  const loop = (time: number) => { raf = requestAnimationFrame(loop); try { render(time); } catch { /* ese fotograma no se pinta */ } };

  const tema = new MutationObserver(paint);
  tema.observe(document.documentElement, { attributes: true, attributeFilter: ['data-color', 'data-mode'] });
  addEventListener('resize', resize);
  addEventListener('pointermove', onMove, { passive: true });
  paint(); resize();
  if (reduced) render(4000); else raf = requestAnimationFrame(loop);

  return {
    intento: () => { tryAt = ahora(); failAt = -100; },
    // con «reducir movimiento» el resultado se ve ya hecho, sin la animación
    abrir: () => { openAt = reduced ? ahora() - 5 : ahora(); if (reduced) render(performance.now()); },
    rechazar: () => { tryAt = -100; failAt = ahora(); if (reduced) render(performance.now()); },
    destruir: () => {
      cancelAnimationFrame(raf);
      tema.disconnect();
      removeEventListener('resize', resize);
      removeEventListener('pointermove', onMove);
      scene.traverse((o) => {
        const m = o as THREE.Mesh;
        m.geometry?.dispose?.();
        const mat = m.material as THREE.Material | THREE.Material[] | undefined;
        (Array.isArray(mat) ? mat : mat ? [mat] : []).forEach((x) => x.dispose());
      });
      dotTex.dispose(); haloTex.dispose(); entorno.dispose(); pmrem.dispose();
      renderer.dispose();
      renderer.forceContextLoss();
    },
  };
}
