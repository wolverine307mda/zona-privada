// Iconos de línea sueltos (trazo de 24×24, se pintan con la clase global `.icon`): sustituyen a los emojis de las etiquetas.
// Uso en una plantilla:  <svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path [attr.d]="iconos.gota" /></svg>
export const ICONOS = {
  pin: 'M12 21s-7-6.2-7-11a7 7 0 0 1 14 0c0 4.8-7 11-7 11zM12 12a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5z',
  gota: 'M12 3s6 6.5 6 11a6 6 0 0 1-12 0c0-4.5 6-11 6-11z',
  termometro: 'M14 14.8V5a2 2 0 0 0-4 0v9.8a4 4 0 1 0 4 0z',
  viento: 'M3 8h11a3 3 0 1 0-3-3M3 12h15a3 3 0 1 1-3 3M3 16h8',
  sol: 'M12 16a4 4 0 1 0 0-8 4 4 0 0 0 0 8zM12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4',
  camiseta: 'M8 3 3 6l2 4 3-1v12h8V9l3 1 2-4-5-3a4 4 0 0 1-8 0z',
  tarta: 'M4 20h16v-6a2 2 0 0 0-2-2H6a2 2 0 0 0-2 2zM12 12V8M12 4.5v.5M4 16.5c2 1.3 4 1.3 6 0s4-1.3 6 0 3 1 4 .4',
  euro: 'M18 7a6 6 0 1 0 0 10M4 10h9M4 14h9',
  camara: 'M4 8h3l2-3h6l2 3h3v11H4zM12 17a3.5 3.5 0 1 0 0-7 3.5 3.5 0 0 0 0 7z',
  monitor: 'M3 5h18v11H3zM9 20h6M12 16v4',
  candado: 'M6 11h12v9H6zM8 11V8a4 4 0 0 1 7.5-2',
  alerta: 'M12 3 2 20h20zM12 10v4.5M12 17.5v.5',
  punto: 'M12 12.5v.01',
} as const;
export type Icono = keyof typeof ICONOS;
