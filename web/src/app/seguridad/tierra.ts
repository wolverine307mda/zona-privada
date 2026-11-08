// Contornos muy simplificados de los continentes e islas grandes, como [longitud, latitud]. No son un mapa exacto:
// sirven para rellenar el globo de puntos y que se reconozca dónde está cada cosa, sin bajar ningún dato de fuera.
type Contorno = readonly (readonly [number, number])[];

const CONTORNOS: readonly Contorno[] = [
  // Norteamérica y Centroamérica
  [[-168, 66], [-162, 70], [-156, 71.5], [-141, 70], [-128, 70], [-115, 68], [-95, 72], [-85, 69], [-82, 64], [-93, 59], [-94, 57], [-85, 55], [-80, 52], [-78, 58], [-77, 62], [-70, 60], [-65, 60], [-61, 56], [-56, 52], [-60, 48], [-66, 44], [-70, 42], [-74, 40], [-76, 35], [-81, 31], [-80, 26], [-82, 26], [-84, 30], [-90, 29], [-94, 29.5], [-97, 26], [-97, 21], [-94, 18], [-91, 19], [-87, 21], [-88, 16], [-84, 15], [-83, 10], [-79, 9], [-77, 8], [-80, 7], [-84, 9], [-87, 13], [-92, 15], [-96, 16], [-105, 20], [-106, 23], [-110, 27], [-113, 31], [-117, 32.5], [-121, 35], [-124, 40], [-124, 47], [-127, 51], [-132, 55], [-138, 59], [-146, 61], [-152, 59], [-158, 57], [-164, 55], [-158, 58], [-162, 60], [-166, 62]],
  // Groenlandia
  [[-73, 78], [-60, 82], [-30, 83], [-20, 80], [-20, 72], [-24, 68], [-40, 65], [-44, 60], [-50, 62], [-54, 67], [-58, 75]],
  // Sudamérica
  [[-77, 8], [-72, 12], [-63, 10.5], [-60, 8], [-52, 5], [-50, 0], [-44, -2], [-35, -5], [-35, -9], [-39, -14], [-41, -22], [-48, -26], [-53, -34], [-58, -35], [-57, -38], [-62, -39], [-65, -42], [-68, -47], [-69, -52], [-68, -55], [-72, -54], [-75, -48], [-73, -40], [-71, -30], [-70, -18], [-76, -14], [-81, -6], [-80, -2], [-78, 2]],
  // Eurasia
  [[-9, 37], [-9, 43], [-2, 43.5], [-1, 46], [-4, 48], [2, 51], [5, 53], [8, 54], [9, 57], [11, 55], [14, 54], [20, 55], [22, 58], [24, 60], [30, 60], [22, 65], [25, 66], [21, 63], [17, 61], [18, 59], [16, 56], [12, 56], [8, 58], [5, 62], [14, 68], [22, 70.5], [30, 70], [40, 67], [44, 68], [60, 69], [70, 73], [80, 73], [90, 76], [105, 78], [113, 74], [130, 71], [140, 72], [160, 70], [170, 70], [180, 68], [178, 64], [170, 60], [163, 58], [156, 51], [156, 57], [163, 62], [155, 60], [142, 59], [136, 54], [141, 52], [140, 48], [135, 43], [130, 42], [129, 36], [126, 35], [126, 38], [122, 40], [121, 37], [119, 35], [122, 31], [120, 26], [117, 23], [110, 21], [108, 17], [109, 12], [105, 9], [101, 13], [100, 7], [103, 2], [100, 3], [98, 8], [98, 16], [94, 17], [92, 22], [88, 22], [85, 20], [80, 15], [80, 10], [77, 8], [73, 16], [72, 21], [67, 24], [62, 25], [57, 26], [56, 27], [50, 30], [48, 30], [50, 26], [56, 25], [59, 23], [55, 17], [44, 13], [43, 16], [39, 22], [35, 28], [34.5, 31], [36, 36], [30, 36.5], [27, 37], [26, 40], [29, 41], [35, 42], [41, 41], [40, 44], [37, 45], [34, 45], [31, 46.5], [29, 45], [28, 42], [24, 40], [22, 37], [21, 40], [19, 42], [14, 45], [12, 44], [16, 41], [18, 40], [16, 38], [12, 42], [8, 44], [3, 43], [0, 39], [-2, 37], [-5, 36]],
  // África
  [[-17, 21], [-16, 28], [-10, 30], [-6, 36], [10, 37], [11, 33], [20, 31], [25, 32], [32, 31], [34, 28], [38, 20], [43, 12], [51, 12], [48, 5], [42, -2], [40, -10], [40, -16], [35, -24], [32, -29], [27, -34], [20, -35], [18, -32], [15, -26], [12, -17], [13, -10], [9, -1], [9, 4], [4, 6], [-5, 5], [-12, 7], [-17, 14]],
  // Madagascar
  [[44, -25], [47, -25], [50, -15], [49, -12], [44, -17]],
  // Australia
  [[114, -22], [122, -18], [130, -12], [137, -12], [136, -15], [141, -17], [142, -11], [146, -19], [153, -26], [153, -30], [151.5, -34], [150, -37], [141, -38], [135, -34], [130, -32], [115, -34]],
  // Reino Unido, Irlanda e Islandia
  [[-5, 50], [1, 51], [2, 53], [-2, 57], [-5, 58.5], [-6, 56], [-3, 54]],
  [[-10, 52], [-6, 52], [-6, 55], [-10, 54]],
  [[-24, 65], [-14, 66], [-14, 64.5], [-22, 63.5]],
  // Japón
  [[130, 31], [135, 33.5], [140, 34.8], [141, 36.5], [142, 40], [141, 45], [145, 44], [142, 42], [140, 38], [136, 36], [131, 34]],
  // Sumatra, Borneo, Java y Nueva Guinea
  [[95, 5], [98, 4], [106, -3], [104, -6], [100, -1]],
  [[109, 1], [117, 7], [119, 1], [116, -4], [110, -3]],
  [[105, -6], [114, -7], [114, -8.5], [106, -7.5]],
  [[131, -1], [141, -2.5], [150, -10], [141, -9], [137, -5]],
  // Nueva Zelanda y Cuba
  [[172, -34], [178, -38], [175, -41], [172, -41]],
  [[167, -46], [174, -41], [172, -44], [170, -46.5]],
  [[-85, 22], [-74, 20], [-78, 21.5]],
];

// punto dentro de un polígono (trazado de rayos sobre longitud y latitud)
function dentro(lon: number, lat: number, c: Contorno): boolean {
  let d = false;
  for (let i = 0, j = c.length - 1; i < c.length; j = i++) {
    const [xi, yi] = c[i], [xj, yj] = c[j];
    if (yi > lat !== yj > lat && lon < ((xj - xi) * (lat - yi)) / (yj - yi) + xi) d = !d;
  }
  return d;
}

export const esTierra = (lat: number, lon: number): boolean => CONTORNOS.some((c) => dentro(lon, lat, c));

// la rejilla de puntos de tierra, cada `paso` grados; en los polos los puntos se espacian más para que no se amontonen
export function puntosDeTierra(paso = 3): [number, number][] {
  const r: [number, number][] = [];
  for (let lat = -58 + paso / 2; lat < 84; lat += paso) {
    const salto = paso / Math.max(0.35, Math.cos((lat * Math.PI) / 180));
    for (let lon = -180; lon < 180; lon += salto) if (esTierra(lat, lon)) r.push([lat, lon]);
  }
  return r;
}

// La posición horizontal (0–1) de una longitud en la textura de los continentes: la esfera de Three empieza en el meridiano −90°, no en el −180°
export const columnaTextura = (lon: number): number => (((lon + 90) / 360) % 1 + 1) % 1;

// Los continentes como una imagen (relleno y costa), para pegarla sobre una esfera; se dibuja tres veces para que lo que cruza el borde de la textura no se corte
export function texturaTierra(relleno: string, costa: string, ancho = 2048): HTMLCanvasElement {
  const c = document.createElement('canvas'); c.width = ancho; c.height = ancho / 2;
  const x = c.getContext('2d');
  if (!x) return c;
  x.fillStyle = relleno; x.strokeStyle = costa; x.lineWidth = Math.max(1.5, ancho / 900); x.lineJoin = 'round';
  for (const desfase of [-ancho, 0, ancho]) {
    for (const poly of CONTORNOS) {
      x.beginPath();
      poly.forEach(([lon, lat], i) => { const px = ((lon + 90) / 360) * ancho + desfase, py = ((90 - lat) / 180) * (ancho / 2); if (i) x.lineTo(px, py); else x.moveTo(px, py); });
      x.closePath(); x.fill(); x.stroke();
    }
  }
  return c;
}
