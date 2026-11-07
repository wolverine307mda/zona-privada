import type * as Leaflet from 'leaflet';

// Leaflet es CommonJS: en el servidor de desarrollo `import('leaflet')` devuelve sus funciones sueltas, pero en la versión de producción solo trae `default`
// (y `L.map` no existe). Así se coge el objeto bueno en los dos casos. Se baja solo al abrir un mapa.
export async function cargarLeaflet(): Promise<typeof Leaflet> {
  const m = (await import('leaflet')) as unknown as { default?: typeof Leaflet };
  return (m.default ?? m) as typeof Leaflet;
}
