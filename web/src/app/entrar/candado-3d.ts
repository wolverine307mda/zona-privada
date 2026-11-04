import * as THREE from 'three';

// Escena de la pantalla de entrar: candado de líneas con anillos de partículas en órbita,
// rejilla que avanza hacia el horizonte y estrellas. intento() tira del arco; abrir() lo abre del todo.
export function crearCandado(canvas: HTMLCanvasElement) {
  const reduced = matchMedia('(prefers-reduced-motion: reduce)').matches;
  const renderer = new THREE.WebGLRenderer({ canvas, antialias: true });
  const scene = new THREE.Scene();
  const camera = new THREE.PerspectiveCamera(40, 1, 0.1, 120);
  camera.position.set(0, 1.2, 9);
  const small = () => innerWidth < 860;

  const lineMat = new THREE.LineBasicMaterial({ transparent: true });
  const softMat = new THREE.LineBasicMaterial({ transparent: true, opacity: 0.5 });
  const fillMat = new THREE.MeshBasicMaterial({ transparent: true, opacity: 0.55 });
  const lines = (g: THREE.BufferGeometry, m: THREE.Material = lineMat) => new THREE.LineSegments(new THREE.EdgesGeometry(g, 25), m);

  const grid = new THREE.GridHelper(80, 64);
  (grid.material as THREE.Material).transparent = true;
  (grid.material as THREE.Material).opacity = 0.35;
  grid.position.y = -2.6;
  scene.add(grid);

  // candado
  const lock = new THREE.Group();
  const bodyGeo = new THREE.BoxGeometry(2.2, 1.8, 0.8);
  lock.add(new THREE.Mesh(bodyGeo, fillMat), lines(bodyGeo), lines(new THREE.BoxGeometry(1.9, 1.5, 0.82), softMat));
  const shackle = new THREE.Group();
  const arc = lines(new THREE.TorusGeometry(0.72, 0.13, 10, 28, Math.PI));
  arc.position.y = 0.95;
  const legL = lines(new THREE.CylinderGeometry(0.13, 0.13, 0.7, 10)); legL.position.set(-0.72, 0.6, 0);
  const legR = lines(new THREE.CylinderGeometry(0.13, 0.13, 0.7, 10)); legR.position.set(0.72, 0.6, 0);
  shackle.add(arc, legL, legR);
  shackle.position.y = 0.55;
  lock.add(shackle);
  const hole = new THREE.Group();
  hole.add(lines(new THREE.CircleGeometry(0.2, 20)));
  const slot = lines(new THREE.PlaneGeometry(0.14, 0.42)); slot.position.y = -0.25; hole.add(slot);
  hole.position.set(0, -0.05, 0.41);
  lock.add(hole);
  scene.add(lock);

  // anillos de partículas
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
    const g = new THREE.BufferGeometry(); g.setAttribute('position', new THREE.BufferAttribute(pos, 3));
    const ring = new THREE.Points(g, new THREE.PointsMaterial({ size: 0.05, map: dotTex, transparent: true, alphaTest: 0.3, opacity: k ? 0.7 : 1 }));
    ring.rotation.set(tx, 0, tz);
    ring.userData['speed'] = 0.2 + k * 0.08;
    lock.add(ring); rings.push(ring);
  });

  const starN = small() ? 500 : 1100, starPos = new Float32Array(starN * 3);
  for (let i = 0; i < starN; i++) starPos.set([(Math.random() - 0.5) * 60, Math.random() * 18 - 2, -Math.random() * 40 - 4], i * 3);
  const starGeo = new THREE.BufferGeometry(); starGeo.setAttribute('position', new THREE.BufferAttribute(starPos, 3));
  const starMat = new THREE.PointsMaterial({ size: 0.06, map: dotTex, transparent: true, alphaTest: 0.3, opacity: 0.8 });
  scene.add(new THREE.Points(starGeo, starMat));

  const paint = () => {
    const cs = getComputedStyle(document.documentElement);
    const brand = new THREE.Color(cs.getPropertyValue('--brand').trim() || '#ff7a1f');
    const dark = new THREE.Color(cs.getPropertyValue('--dark').trim() || '#03080b');
    const light = new THREE.Color(cs.getPropertyValue('--on-dark').trim() || '#ffffff');
    renderer.setClearColor(dark, 1);
    scene.fog = new THREE.Fog(dark, 10, 34);
    lineMat.color.copy(brand); softMat.color.copy(brand);
    fillMat.color.copy(dark).lerp(brand, 0.08);
    (grid.material as THREE.LineBasicMaterial).color.copy(brand);
    rings.forEach((r, i) => (r.material as THREE.PointsMaterial).color.copy(i ? brand : light));
    starMat.color.copy(light);
    if (reduced && typeof render === 'function') render(4000);
  };

  const resize = () => {
    renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
    renderer.setSize(innerWidth, innerHeight, false);
    camera.aspect = innerWidth / innerHeight; camera.updateProjectionMatrix();
    lock.scale.setScalar(small() ? 0.6 : 1);
    lock.position.set(small() ? 0 : -2.2, small() ? 1.45 : 0.6, 0);
    if (reduced) render(4000); // cambiar el tamaño borra el canvas y sin animación nadie lo vuelve a pintar
  };

  const mouse = { x: 0, y: 0, tx: 0, ty: 0 };
  const onMove = (e: PointerEvent) => { mouse.tx = (e.clientX / innerWidth) * 2 - 1; mouse.ty = -((e.clientY / innerHeight) * 2 - 1); };

  let tryAt = -10, openAt = -10, raf = 0;
  const render = (time: number) => {
    const t = time * 0.001;
    mouse.x += (mouse.tx - mouse.x) * 0.05; mouse.y += (mouse.ty - mouse.y) * 0.05;
    grid.position.z = (t * 1.2) % (80 / 64);
    lock.rotation.y = Math.sin(t * 0.5) * 0.35 + mouse.x * 0.5;
    lock.rotation.x = -0.1 + mouse.y * -0.25;
    rings.forEach((r) => { r.rotation.y = t * (r.userData['speed'] as number); });
    const k = t - tryAt, o = t - openAt;
    if (o >= 0) {
      // abierto: el arco sube y gira, y los anillos se aceleran
      const e = Math.min(1, o / 0.7);
      shackle.position.y = 0.55 + e * 0.6;
      shackle.rotation.y = e * 0.9;
      rings.forEach((r) => { r.rotation.y += o * 2; });
    } else if (k >= 0 && k < 1.6) {
      const up = k < 0.6 ? Math.sin((k / 0.6) * Math.PI / 2) * 0.35 : Math.max(0, 0.35 - (k - 0.6) * 0.9);
      shackle.position.y = 0.55 + up;
      lock.rotation.z = k > 1 && k < 1.5 ? Math.sin(k * 60) * 0.12 * (1.5 - k) : 0;
    } else { shackle.position.y = 0.55; lock.rotation.z = 0; }
    camera.position.x = mouse.x * 0.4;
    camera.lookAt(0, 0.6, 0);
    renderer.render(scene, camera);
  };
  const loop = (time: number) => { render(time); raf = requestAnimationFrame(loop); };

  const tema = new MutationObserver(paint);
  tema.observe(document.documentElement, { attributes: true, attributeFilter: ['data-color', 'data-mode'] });
  addEventListener('resize', resize);
  addEventListener('pointermove', onMove, { passive: true });
  paint(); resize();
  if (reduced) render(4000); else raf = requestAnimationFrame(loop);

  return {
    intento: () => { tryAt = performance.now() * 0.001; },
    abrir: () => { openAt = performance.now() * 0.001; },
    destruir: () => {
      cancelAnimationFrame(raf);
      tema.disconnect();
      removeEventListener('resize', resize);
      removeEventListener('pointermove', onMove);
      renderer.dispose();
      renderer.forceContextLoss();
    },
  };
}
