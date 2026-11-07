// Bienvenida personal: la frase de debajo del saludo de Inicio. Se compone con lo que pasa hoy (cumpleaños, festivo, algo de ayer, la luna, el tiempo, el día, la estación y la hora)
// y con una semilla por fecha: la frase no cambia al recargar y no repite la de ayer. Todo puro, sin Angular, para poder probarlo.
export interface ContextoBienvenida {
  fecha: Date;                                         // el momento de ahora (hora local)
  nombre: string;
  cumples: string[];                                   // quién cumple años hoy
  gastoAyer?: { concepto: string; importe: string } | null;
  racha?: { nombre: string; dias: number } | null;
  entrenoAyer?: boolean | null;
  animoAyer?: number | null;                           // 1–5
  tiempo?: { temperatura: number; lluvia: boolean; texto: string } | null;
}
export interface Frase { id: string; texto: string; categoria: string; }

type Plantillas = Record<string, string[]>;
// Las frases, por situación. {n} = nombre, {x} = el dato de la situación (festivo, concepto, número…).
export const FRASES: Plantillas = {
  cumple: ['Hoy cumple años {x}: no se te olvide felicitar.', '🎂 {x} está de cumpleaños. Un mensaje sienta de maravilla.', 'Día de cumpleaños de {x}. Ya sabes qué toca.', 'Hoy es el cumple de {x}: un detalle, aunque sea pequeño.'],
  festivo: ['Hoy es fiesta: {x}. Sin prisas.', '{x}: día de descanso, aprovéchalo.', 'Festivo por {x}. Que el reloj mande lo justo.', 'Hoy no se trabaja: {x}.'],
  gasto: ['Ayer el gasto más grande fue {x}.', 'Lo más caro de ayer: {x}.', 'Ayer te llevaste el gasto de {x}.', 'Para tener en cuenta: ayer, {x}.'],
  racha: ['Llevas {x} días seguidos con «{n}». Así se hace.', 'Racha de {x} días en «{n}». Constancia pura.', '«{n}»: {x} días sin fallar.', 'Otro día más en la racha de «{n}» ({x}).'],
  entreno: ['Ayer entrenaste: hoy el cuerpo lo agradece.', 'Después del entreno de ayer, hoy toca cuidarse.', 'Ayer te moviste y se nota: sigue así sin pasarte.', 'Entrenaste ayer: mañana también cuenta un paseo.'],
  animoBajo: ['Ayer no fue el mejor día. Hoy empieza de cero.', 'Si ayer pesó, hoy va ligero: un paso cada vez.', 'Un día malo no hace una racha. Hoy es otro día.'],
  animoAlto: ['Ayer fue un buen día. A ver si hoy se repite.', 'Venías de un día genial: hoy, a mantenerlo.', 'Con el buen ánimo de ayer, hoy empieza bien.'],
  lunaLlena: ['Hay luna llena esta noche: mira al cielo si está despejado.', 'Luna llena hoy. Si no hay nubes, merece un vistazo.', 'Noche de luna llena: perfecta para un paseo.'],
  lunaNueva: ['Luna nueva: noche oscura, ideal para ver estrellas.', 'Hoy no hay luna: el cielo estará más negro y más estrellado.', 'Luna nueva hoy: buen día para mirar las estrellas.'],
  lluvia: ['Hoy llueve: paraguas y paciencia.', 'Día de lluvia. Buen plan: algo caliente y una buena lista de reproducción.', 'Va a mojarse el día: saca el paraguas.', 'Lluvia hoy. Si tienes ropa tendida, ya sabes.'],
  calor: ['Hoy hace calor ({x} °): agua a mano y sombra en las horas centrales.', 'Día caluroso, {x} °. Bebe a menudo.', '{x} ° ahora mismo: mejor lo pesado para la tarde.'],
  frio: ['Hoy hace frío ({x} °): abrígate bien.', 'Día frío, {x} °. Una capa más no sobra.', '{x} ° fuera: ropa de abrigo y algo caliente.'],
  lunes: ['Lunes: empieza suave y ya está medio hecho.', 'Es lunes. Una sola cosa importante hoy y vas sobrado.', 'Nuevo comienzo de semana: sin prisa, pero sin parar.'],
  viernes: ['Viernes: la semana ya casi está.', 'Es viernes. Remata lo pendiente y a disfrutar.', 'Último empujón de la semana.'],
  sabado: ['Sábado: el día es tuyo.', 'Sábado tranquilo, a tu ritmo.', 'Fin de semana: baja una marcha.'],
  domingo: ['Domingo: descansar también es productivo.', 'Domingo de calma. Mañana ya se verá.', 'Hoy se descansa, y se descansa bien.'],
  semana: ['Mitad de semana: lo peor ya pasó.', 'Un día más. Una cosa cada vez.', 'Semana en marcha: vas bien.'],
  primavera: ['Primavera: los días ganan luz.', 'Con la primavera, cada tarde dura un poco más.'],
  verano: ['Verano: aprovecha las horas de luz y las de sombra.', 'Días largos de verano: hay tiempo para todo, menos para agobiarse.'],
  otono: ['Otoño: luz suave y tardes más cortas.', 'Con el otoño, la manta y el té vuelven a tener sentido.'],
  invierno: ['Invierno: días cortos, mucha manta.', 'En invierno, la luz es un lujo: sal un rato a por ella.'],
  madrugada: ['A estas horas lo mejor es dormir. Si no puedes, que sea con calma.', 'Madrugada: todo lo importante puede esperar al sol.', 'Es muy tarde: lo que no se resuelva ahora, mañana se ve mejor.'],
  manana: ['Buenos días: empieza por lo que más pesa.', 'Mañana por delante. Un café y a por la primera cosa.', 'Con la mañana nueva, todo está por hacer.'],
  tarde: ['Tarde por delante: lo que queda del día es tuyo.', 'Buena hora para rematar lo pendiente.', 'Mediodía pasado: hay tiempo todavía.'],
  noche: ['Cerrando el día: lo que se hizo, hecho está.', 'Noche tranquila: baja las luces y las pantallas.', 'Ya es de noche: un buen momento para parar.'],
};

// Pura: 32 bits a partir de un texto (una semilla estable, sin Math.random)
export function hash(s: string): number {
  let h = 2166136261;
  for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 16777619); }
  return h >>> 0;
}
export const clave = (d: Date): string => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

// Pura: el domingo de Pascua (algoritmo de Gauss/Meeus) para sacar el Viernes Santo
export function pascua(anio: number): Date {
  const a = anio % 19, b = Math.floor(anio / 100), c = anio % 100, d = Math.floor(b / 4), e = b % 4, f = Math.floor((b + 8) / 25), g = Math.floor((b - f + 1) / 3);
  const h = (19 * a + b - d - g + 15) % 30, i = Math.floor(c / 4), k = c % 4, l = (32 + 2 * e + 2 * i - h - k) % 7, m = Math.floor((a + 11 * h + 22 * l) / 451);
  const mes = Math.floor((h + l - 7 * m + 114) / 31), dia = ((h + l - 7 * m + 114) % 31) + 1;
  return new Date(anio, mes - 1, dia);
}

// Pura: el festivo nacional de España de ese día (los de fecha fija y el Viernes Santo), o null
export function festivo(d: Date): string | null {
  const fijos: Record<string, string> = { '1-1': 'Año Nuevo', '6-1': 'Reyes', '1-5': 'el Día del Trabajo', '15-8': 'la Asunción', '12-10': 'la Fiesta Nacional', '1-11': 'Todos los Santos', '6-12': 'la Constitución', '8-12': 'la Inmaculada', '25-12': 'Navidad' };
  const f = fijos[`${d.getDate()}-${d.getMonth() + 1}`];
  if (f) return f;
  const vs = pascua(d.getFullYear()); vs.setDate(vs.getDate() - 2);
  return clave(vs) === clave(d) ? 'Viernes Santo' : null;
}

// Pura: la fase de la luna (0 = nueva, 0,5 = llena) a partir de la luna nueva del 6 de enero de 2000 y el mes sinódico de 29,53 días
export function faseLuna(d: Date): number {
  const dias = (Date.UTC(d.getFullYear(), d.getMonth(), d.getDate(), 12) - Date.UTC(2000, 0, 6, 18, 14)) / 86400000;
  return (((dias % 29.530588853) + 29.530588853) % 29.530588853) / 29.530588853;
}
export const esLunaLlena = (d: Date): boolean => Math.abs(faseLuna(d) - 0.5) < 0.017;     // ±0,5 días
export const esLunaNueva = (d: Date): boolean => { const f = faseLuna(d); return f < 0.017 || f > 0.983; };

const estacion = (d: Date): string => { const m = d.getMonth() + 1; return m >= 3 && m <= 5 ? 'primavera' : m >= 6 && m <= 8 ? 'verano' : m >= 9 && m <= 11 ? 'otono' : 'invierno'; };
const momento = (h: number): string => (h < 6 ? 'madrugada' : h < 14 ? 'manana' : h < 21 ? 'tarde' : 'noche');

// Las situaciones que aplican, de más a menos importante. La primera manda y si hay varias de la misma categoría se elige por semilla.
export function situaciones(c: ContextoBienvenida): { categoria: string; x: string }[] {
  const l: { categoria: string; x: string }[] = [];
  if (c.cumples.length) l.push({ categoria: 'cumple', x: c.cumples.slice(0, 2).join(' y ') });
  const fest = festivo(c.fecha);
  if (fest) l.push({ categoria: 'festivo', x: fest });
  if (c.gastoAyer) l.push({ categoria: 'gasto', x: `${c.gastoAyer.concepto} (${c.gastoAyer.importe})` });
  if (c.racha && c.racha.dias >= 3) l.push({ categoria: 'racha', x: String(c.racha.dias) });
  if (c.entrenoAyer) l.push({ categoria: 'entreno', x: '' });
  if (c.animoAyer != null && c.animoAyer <= 2) l.push({ categoria: 'animoBajo', x: '' });
  if (c.animoAyer != null && c.animoAyer >= 5) l.push({ categoria: 'animoAlto', x: '' });
  if (esLunaLlena(c.fecha)) l.push({ categoria: 'lunaLlena', x: '' });
  if (esLunaNueva(c.fecha)) l.push({ categoria: 'lunaNueva', x: '' });
  if (c.tiempo?.lluvia) l.push({ categoria: 'lluvia', x: '' });
  if (c.tiempo && !c.tiempo.lluvia && c.tiempo.temperatura >= 32) l.push({ categoria: 'calor', x: String(Math.round(c.tiempo.temperatura)) });
  if (c.tiempo && c.tiempo.temperatura <= 5) l.push({ categoria: 'frio', x: String(Math.round(c.tiempo.temperatura)) });
  const dia = c.fecha.getDay();
  l.push({ categoria: dia === 1 ? 'lunes' : dia === 5 ? 'viernes' : dia === 6 ? 'sabado' : dia === 0 ? 'domingo' : 'semana', x: '' });
  l.push({ categoria: estacion(c.fecha), x: '' });
  l.push({ categoria: momento(c.fecha.getHours()), x: '' });
  return l;
}

// Pura: elige la frase de hoy. Cumpleaños > festivo > datos > calendario. Dentro de una categoría el índice avanza con el número de día,
// así dos días seguidos con la misma situación nunca dan la misma frase; y misma fecha y contexto = misma frase.
export function componer(c: ContextoBienvenida): Frase {
  const sit = situaciones(c);
  const cumple = sit.filter((s) => s.categoria === 'cumple');
  const festivos = sit.filter((s) => s.categoria === 'festivo');
  const datos = sit.filter((s) => ['gasto', 'racha', 'entreno', 'animoBajo', 'animoAlto', 'lunaLlena', 'lunaNueva', 'lluvia', 'calor', 'frio'].includes(s.categoria));
  const calendario = sit.filter((s) => !['cumple', 'festivo'].includes(s.categoria) && !datos.includes(s));
  const semilla = hash(clave(c.fecha) + '|' + c.nombre);
  // algunos días mandan los datos y otros el calendario, para no repetir siempre el mismo tipo de frase
  const candidatas = cumple.length ? cumple : festivos.length ? festivos : datos.length && semilla % 3 !== 0 ? datos : calendario;
  const s = candidatas[semilla % candidatas.length];
  const lista = FRASES[s.categoria];
  const dia = Math.floor(Date.UTC(c.fecha.getFullYear(), c.fecha.getMonth(), c.fecha.getDate()) / 86400000);
  const i = (dia + hash(s.categoria)) % lista.length;
  return { id: `${s.categoria}:${i}`, categoria: s.categoria, texto: lista[i].replaceAll('{x}', s.x).replaceAll('{n}', c.racha?.nombre ?? c.nombre) };
}
