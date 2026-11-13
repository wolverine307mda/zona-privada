// Las recetas de los atajos del iPhone y el texto que se pega en la app Atajos (con Apple Intelligence) para montarlas.
// Es lógica pura, sin Angular: se prueba en atajos-prompt.spec.ts.

export interface Campo {
  /** nombre del campo en el JSON */
  nombre: string;
  tipo: 'texto' | 'número';
  /** de dónde sale el valor en el atajo */
  entrada: string;
  ejemplo: string | number;
  /** si se puede dejar fuera */
  opcional?: boolean;
}

export interface Receta {
  id: string;
  titulo: string;
  /** permiso que necesita el token (null = cualquiera sirve) */
  permiso: string;
  metodo: 'GET' | 'POST';
  ruta: string;
  /** campos que rellena quien lo lanza */
  campos: Campo[];
  /** campos con valor fijo (p. ej. estado = llegada) */
  fijos?: Record<string, string>;
  /** cómo se enseña la respuesta */
  salida: 'notificacion' | 'voz';
  /** nombre del atajo / frase de Siri */
  nombre: string;
  /** cuándo se lanza: botón, Siri, automatización… */
  disparo: string;
  /** una advertencia que conviene leer antes */
  aviso?: string;
}

export const RECETAS: Receta[] = [
  { id: 'gasto', titulo: 'Gasto rápido', permiso: 'gasto', metodo: 'POST', ruta: '/api/atajos/movimiento', salida: 'notificacion', nombre: 'Apunta gasto',
    campos: [
      { nombre: 'importe', tipo: 'número', entrada: '«Pedir entrada» de tipo número, con el aviso «Importe»', ejemplo: 12.5 },
      { nombre: 'concepto', tipo: 'texto', entrada: '«Pedir entrada» de tipo texto, con el aviso «Concepto»', ejemplo: 'Café' },
      { nombre: 'tipo', tipo: 'texto', entrada: '«Elegir del menú» con las opciones gasto e ingreso (por defecto gasto)', ejemplo: 'gasto', opcional: true },
    ],
    disparo: 'Desde el Centro de control o el botón de acción.' },
  { id: 'voz', titulo: 'Gasto por voz', permiso: 'gasto', metodo: 'POST', ruta: '/api/atajos/frase', salida: 'notificacion', nombre: 'Apunta gasto por voz',
    campos: [{ nombre: 'texto', tipo: 'texto', entrada: '«Dictar texto» (idioma español)', ejemplo: '12,50 en el Mercadona' }],
    disparo: 'Con Siri: «Oye Siri, apunta gasto por voz».' },
  { id: 'pago', titulo: 'Apple Pay', permiso: 'pago', metodo: 'POST', ruta: '/api/atajos/pago', salida: 'notificacion', nombre: 'Pago con Apple Pay',
    campos: [
      { nombre: 'comercio', tipo: 'texto', entrada: 'la variable «Comercio» de la automatización de Transacción', ejemplo: 'Mercadona' },
      { nombre: 'importe', tipo: 'número', entrada: 'la variable «Cantidad» de la automatización de Transacción', ejemplo: 23.4 },
      { nombre: 'tarjeta', tipo: 'texto', entrada: 'la variable «Tarjeta o pase» de la automatización de Transacción', ejemplo: 'Visa', opcional: true },
    ],
    disparo: 'Es una automatización: Atajos › Automatización › Transacción, con «Ejecutar inmediatamente». Solo se prueba pagando de verdad.' },
  { id: 'llegada', titulo: 'Llegué a casa', permiso: 'casa', metodo: 'POST', ruta: '/api/atajos/casa', salida: 'notificacion', nombre: 'Llegué a casa',
    campos: [], fijos: { estado: 'llegada' },
    disparo: 'Es una automatización: Atajos › Automatización › Llegar a un lugar (casa), con «Ejecutar inmediatamente».' },
  { id: 'salida', titulo: 'Me voy de casa', permiso: 'casa', metodo: 'POST', ruta: '/api/atajos/casa', salida: 'notificacion', nombre: 'Me voy de casa',
    campos: [], fijos: { estado: 'salida' },
    disparo: 'Es una automatización: Atajos › Automatización › Salir de un lugar (casa), con «Ejecutar inmediatamente».' },
  { id: 'dormir', titulo: 'Me voy a dormir', permiso: 'casa', metodo: 'POST', ruta: '/api/atajos/dormir', salida: 'voz', nombre: 'Me voy a dormir',
    campos: [], fijos: { estado: 'duermo' },
    disparo: 'Automatización con la Concentración «Dormir» al activarse. Para «despierto», lo mismo con estado = despierto al desactivarla.' },
  { id: 'nota', titulo: 'Nota rápida', permiso: 'nota', metodo: 'POST', ruta: '/api/atajos/nota', salida: 'notificacion', nombre: 'Nota rápida',
    campos: [
      { nombre: 'texto', tipo: 'texto', entrada: '«Dictar texto» (idioma español)', ejemplo: 'Llamar al dentista' },
      { nombre: 'titulo', tipo: 'texto', entrada: 'un texto fijo o vacío', ejemplo: '', opcional: true },
    ],
    disparo: 'Con Siri: «Oye Siri, nota rápida».' },
  { id: 'despertar', titulo: 'Encender el PC', permiso: 'pc', metodo: 'POST', ruta: '/api/atajos/despertar', salida: 'notificacion', nombre: 'Enciende el PC',
    campos: [{ nombre: 'nombre', tipo: 'texto', entrada: 'un texto fijo con el nombre del aparato tal y como sale en Red', ejemplo: 'PC' }],
    disparo: 'Desde la pantalla de bloqueo o el botón de acción. Necesita la MAC del aparato y el agente del PC en marcha.' },
  { id: 'estado', titulo: 'Estado de un vistazo', permiso: 'lectura', metodo: 'GET', ruta: '/api/atajos/estado', salida: 'voz', nombre: 'Cómo va todo',
    campos: [], disparo: 'Con Siri: «Oye Siri, cómo va todo».' },
  { id: 'resumen', titulo: 'Saldo y agenda de hoy', permiso: 'lectura', metodo: 'GET', ruta: '/api/atajos/resumen', salida: 'voz', nombre: 'Qué tengo hoy',
    campos: [], disparo: 'Con Siri: «Oye Siri, qué tengo hoy».' },
  { id: 'persona', titulo: 'Añadir una persona', permiso: 'personas', metodo: 'POST', ruta: '/api/atajos/persona', salida: 'notificacion', nombre: 'Añadir a zona privada',
    campos: [
      { nombre: 'nombre', tipo: 'texto', entrada: 'el nombre del contacto recibido en la hoja de compartir', ejemplo: 'Ana López' },
      { nombre: 'dia', tipo: 'número', entrada: 'el día del cumpleaños del contacto', ejemplo: 14, opcional: true },
      { nombre: 'mes', tipo: 'número', entrada: 'el mes del cumpleaños del contacto', ejemplo: 5, opcional: true },
      { nombre: 'telefono', tipo: 'texto', entrada: 'el teléfono del contacto', ejemplo: '600123456', opcional: true },
    ],
    disparo: 'Desde la hoja de compartir de un contacto (activa «Mostrar en la hoja de compartir» en el atajo).' },
  { id: 'salud', titulo: 'Apple Salud', permiso: 'salud', metodo: 'POST', ruta: '/api/atajos/salud', salida: 'notificacion', nombre: 'Enviar salud de hoy',
    campos: [
      { nombre: 'pasos', tipo: 'número', entrada: '«Buscar muestras de Salud» (Pasos, hoy) sumadas', ejemplo: 8200, opcional: true },
      { nombre: 'peso', tipo: 'número', entrada: '«Buscar muestras de Salud» (Peso, la última) en kg', ejemplo: 72.4, opcional: true },
      { nombre: 'sueño', tipo: 'número', entrada: '«Buscar muestras de Salud» (Sueño, anoche) en horas', ejemplo: 7.5, opcional: true },
    ],
    disparo: 'Automatización diaria (por ejemplo a las 22:00).' },
  { id: 'panico', titulo: 'Pánico', permiso: 'panico', metodo: 'POST', ruta: '/api/atajos/panico', salida: 'notificacion', nombre: 'Pánico',
    campos: [],
    aviso: 'Cierra todas las sesiones y revoca todos los tokens, también este. Se levanta con /desbloquear en Telegram. Pon antes una acción «Mostrar alerta» que pida confirmar.',
    disparo: 'Solo a mano: botón de acción o pantalla de inicio.' },
];

/** Las recetas que puede hacer un token con esos permisos. */
export function recetasPara(alcances: readonly string[]): Receta[] {
  return RECETAS.filter((r) => alcances.includes(r.permiso));
}

/** Marca que se pone donde iría el token cuando no se tiene (un token ya creado no se puede volver a ver). */
export const TOKEN_PENDIENTE = 'PEGA_AQUI_TU_TOKEN';

/** Cuerpo JSON de ejemplo (los campos opcionales se dejan fuera). */
export function cuerpoDe(r: Receta): Record<string, string | number> | null {
  const cuerpo: Record<string, string | number> = { ...(r.fijos ?? {}) };
  for (const c of r.campos) if (!c.opcional) cuerpo[c.nombre] = c.ejemplo;
  return Object.keys(cuerpo).length > 0 ? cuerpo : null;
}

/** Quita la barra final y cualquier ruta: queda solo https://dominio. */
export function normalizarOrigen(origen: string): string {
  const t = origen.trim();
  try { return new URL(t).origin; } catch { return t.replace(/\/+$/, ''); }
}

/** Una dirección que el iPhone no puede alcanzar desde fuera de casa. */
export function esDireccionLocal(origen: string): boolean {
  try {
    const h = new URL(origen).hostname;
    return h === 'localhost' || h === '::1' || h === '[::1]' || h.endsWith('.local') || /^(127\.|10\.|192\.168\.|172\.(1[6-9]|2\d|3[01])\.)/.test(h);
  } catch { return true; }
}

/**
 * El texto para pegar en la app Atajos del iPhone. Describe el atajo paso a paso con las acciones que tiene Atajos
 * y lleva la dirección, el token y el cuerpo exactos; así sirve igual para que Apple Intelligence lo monte que para hacerlo a mano.
 */
export function generarPrompt(r: Receta, token: string | null, origen: string): string {
  const url = normalizarOrigen(origen) + r.ruta;
  const cuerpo = cuerpoDe(r);
  const clave = token ?? TOKEN_PENDIENTE;
  const l: string[] = [];
  let n = 0;
  const paso = (t: string) => l.push(`${++n}. ${t}`);

  l.push(`Crea en la app Atajos de este iPhone un atajo llamado «${r.nombre}». Hazlo con estas acciones, en este orden y sin añadir otras:`, '');
  for (const c of r.campos) paso(`Obtén ${c.tipo === 'número' ? 'un número' : 'un texto'} para «${c.nombre}»: ${c.entrada}${c.opcional ? ' (opcional)' : ''}.`);
  paso(`Añade «Obtener contenido de URL» con la dirección ${url}`);
  l.push(`   - Método: ${r.metodo}.`);
  l.push(`   - Cabeceras (dos): «Authorization» con el valor «Bearer ${clave}» (la palabra Bearer, un espacio y el token) y «Content-Type» con el valor «application/json».`);
  if (r.metodo === 'POST') {
    if (cuerpo) {
      l.push('   - Cuerpo de la solicitud: JSON, con estos campos:');
      for (const [k, v] of Object.entries(r.fijos ?? {})) l.push(`       · ${k} (texto) = «${v}», siempre ese valor`);
      for (const c of r.campos) l.push(`       · ${c.nombre} (${c.tipo}) = el valor de «${c.nombre}» obtenido antes${c.opcional ? ' (puede quedar fuera)' : ''}`);
    } else l.push('   - Cuerpo de la solicitud: JSON vacío ({}).');
  } else l.push('   - Sin cuerpo.');
  paso('Añade «Obtener valor del diccionario» para sacar la clave «mensaje» del resultado anterior.');
  paso(r.salida === 'voz' ? 'Añade «Pronunciar texto» con ese valor, para que Siri lo lea en voz alta.' : 'Añade «Mostrar notificación» con ese valor como texto.');
  l.push('');
  if (cuerpo) l.push(`Ejemplo del cuerpo que debe enviar: ${JSON.stringify(cuerpo)}`);
  l.push(`Cuándo se lanza: ${r.disparo}`);
  if (r.aviso) l.push(`Ojo: ${r.aviso}`);
  l.push('Si alguna de estas acciones no la puedes crear tú, deja el atajo montado hasta donde llegues y dime qué falta.');
  if (!token) l.push(`En la cabecera Authorization, sustituye ${TOKEN_PENDIENTE} por el token del atajo (empieza por mda_).`);
  return l.join('\n');
}
