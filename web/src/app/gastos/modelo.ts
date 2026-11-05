// Tipos de la API de gastos, categorías con icono y color, y formato de dinero.
export type Tipo = 'Gasto' | 'Ingreso';
export type Frecuencia = 'Mensual' | 'Trimestral' | 'Anual';

export interface Movimiento {
  id: number;
  tipo: Tipo;
  fecha: string; // yyyy-MM-dd
  importe: number;
  categoria: string;
  concepto: string;
  porConfirmar?: boolean; // entró solo (Apple Pay) y espera a que lo des por bueno
  notas: string | null;
  recurrenteId: number | null;
  litros: number | null;
  precioLitro: number | null;
  kilometros: number | null;
  tieneTicket?: boolean;
  moneda?: string | null;          // si se pagó en otra moneda (el importe va siempre en euros)
  importeOriginal?: number | null;
}

export interface Recurrente {
  id: number;
  nombre: string;
  tipo: Tipo;
  categoria: string;
  importe: number;
  variable: boolean;
  dia: number;
  frecuencia: Frecuencia;
  inicio: string;
  fin: string | null;
  activo: boolean;
  mensual: number;
}

export interface TotalCategoria { categoria: string; total: number; cantidad: number; }
export interface Pendiente { recurrenteId: number; nombre: string; tipo: Tipo; categoria: string; importe: number; variable: boolean; fecha: string; }
export interface ResumenMes {
  anio: number; mes: number; ingresos: number; gastos: number; ahorro: number; tasaAhorro: number | null;
  anterior: { ingresos: number; gastos: number; ahorro: number };
  porCategoria: TotalCategoria[]; gastoPorDia: number[]; fijosDelMes: number; pendientes: Pendiente[];
  suscripciones: { mensual: number; anual: number; cantidad: number };
  movimientos: Movimiento[];
}
export interface PuntoMes { anio: number; mes: number; ingresos: number; gastos: number; ahorro: number; ahorroAcumulado: number; }
export interface Estadisticas {
  serie: PuntoMes[]; ingresos: number; gastos: number; ahorro: number; tasaAhorro: number | null;
  mediaGastos: number; mediaIngresos: number; mejorMes: PuntoMes | null; peorMes: PuntoMes | null; porCategoria: TotalCategoria[];
}
export interface Repostaje { id: number; fecha: string; importe: number; litros: number; precioLitro: number; kilometros: number | null; consumo: number | null; concepto: string; }
export interface PagoDeuda { id: number; fecha: string; importe: number; concepto: string; notas: string | null; }
export interface Deuda {
  id: number; nombre: string; total: number; categoria: string; inicio: string; notas: string | null;
  pagado: number; restante: number; porcentaje: number; numPagos: number; mediaMensual: number | null; finEstimado: string | null;
  porMes: { anio: number; mes: number; total: number }[]; pagos: PagoDeuda[];
}
export interface GrupoCoste { grupo: string; total: number; porCategoria: { categoria: string; total: number; cantidad: number }[]; }
export interface Deudas { deudas: Deuda[]; grupos: GrupoCoste[]; }
export interface Gasolina {
  repostajes: Repostaje[]; gastado: number; litros: number; precioMedio: number | null; ultimoPrecio: number | null;
  consumoMedio: number | null; kmRecorridos: number | null; costePorKm: number | null; gastoEsteAnio: number;
}

export interface Saldo { configurado: boolean; actual: number; inicioMes: number; finMes: number; previsto: number; enHuchas: number; libre: number; }
export interface Hucha { id: number; nombre: string; objetivo: number; ahorrado: number; color: string; emoji: string; fecha: string | null; porcentaje: number; alMes: number | null; }
export interface Presupuesto { categoria: string; limite: number; gastado: number; porcentaje: number; }
export interface Consejo { icono: string; titulo: string; texto: string; tono: 'bueno' | 'malo' | 'aviso' | 'info'; }
export interface Consejos {
  porDia: number; diasRestantes: number; disponible: number; proyeccion: number;
  regla: { ingresos: number; necesidades: number; caprichos: number; ahorro: number }; lista: Consejo[];
}
export interface PuntoPrevision { dia: number; central: number; bajo: number; alto: number; }
export interface PrevisionCierre {
  saldoActual: number; central: number; bajo: number; alto: number; ritmoDia: number; diasRestantes: number; diasDeDatos: number; fiable: boolean;
  fijosPendientes: number; ingresosPendientes: number; serie: PuntoPrevision[] | null;
}
export interface Mantenimiento {
  id: number; nombre: string; cadaKm: number | null; cadaMeses: number | null; ultimaFecha: string | null; ultimoKm: number | null; notas: string | null;
  proximoKm: number | null; faltanKm: number | null; proximaFecha: string | null; faltanDias: number | null; nivel: 'ok' | 'pronto' | 'vencido' | 'sin-datos'; progreso: number;
}

// ---- Categorías (los id son los de la API) ----
export interface Categoria { id: string; nombre: string; tipo: Tipo; grupo: string; color: string; icono: string; }

const I = {
  casa: 'M3 11l9-7 9 7M5 10v10h14V10',
  rayo: 'M13 2L4 14h7l-1 8 9-12h-7z',
  carro: 'M3 4h2l2.4 10.4a2 2 0 0 0 2 1.6h7.7a2 2 0 0 0 2-1.6L21 7H6M10 20h.01M17 20h.01',
  cubiertos: 'M7 2v20M4 2v6a3 3 0 0 0 6 0V2M17 2c-1.7 0-3 2-3 5v5h3v10',
  ticket: 'M3 7a2 2 0 0 0 2 2v6a2 2 0 0 0-2 2h18a2 2 0 0 0-2-2V9a2 2 0 0 0 2-2zM13 7v10',
  camiseta: 'M8 3l-5 3 2 5 3-1v11h8V10l3 1 2-5-5-3a4 4 0 0 1-8 0z',
  corazon: 'M20.8 5.6a5 5 0 0 0-7.6.6L12 7.6l-1.2-1.4a5 5 0 0 0-7.6 6.6L12 21l8.8-8.2a5 5 0 0 0 0-7.2z',
  portatil: 'M4 5h16v10H4zM2 19h20',
  bus: 'M5 4h14v12H5zM5 11h14M8 19v-3M16 19v-3M8 14h.01M16 14h.01',
  play: 'M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2zM10 8l6 4-6 4z',
  surtidor: 'M4 21V5a2 2 0 0 1 2-2h6a2 2 0 0 1 2 2v16M3 21h12M4 10h10M14 8h2a2 2 0 0 1 2 2v6a2 2 0 0 0 4 0V8l-3-3',
  escudo: 'M12 3l8 3v6c0 5-3.5 8-8 9-4.5-1-8-4-8-9V6z',
  llave: 'M14.7 6.3a4 4 0 0 0-5.4 5.4L3 18v3h3l6.3-6.3a4 4 0 0 0 5.4-5.4l-2.6 2.6-2.8-2.8z',
  coche: 'M5 17h14v-5l-2-5H7l-2 5zM5 12h14M7 17v2M17 17v2M8 14h.01M16 14h.01',
  puntos: 'M5 12h.01M12 12h.01M19 12h.01',
  maletin: 'M3 7h18v13H3zM8 7V5a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2M3 12h18',
  estrella: 'M12 3l2.7 5.6 6.1.9-4.4 4.3 1 6.1L12 17l-5.4 2.9 1-6.1-4.4-4.3 6.1-.9z',
  etiqueta: 'M20 12l-8 8-9-9V3h8zM7.5 7.5h.01',
  mas: 'M12 5v14M5 12h14',
  documento: 'M6 3h9l4 4v14H6zM14 3v5h5M9 13h6M9 17h4',
};

export const CATEGORIAS: Categoria[] = [
  { id: 'vivienda', nombre: 'Vivienda', tipo: 'Gasto', grupo: 'Casa', color: '#6c8cff', icono: I.casa },
  { id: 'suministros', nombre: 'Luz, agua e internet', tipo: 'Gasto', grupo: 'Casa', color: '#ffc93c', icono: I.rayo },
  { id: 'supermercado', nombre: 'Supermercado', tipo: 'Gasto', grupo: 'Día a día', color: '#3ddc84', icono: I.carro },
  { id: 'restaurantes', nombre: 'Restaurantes', tipo: 'Gasto', grupo: 'Día a día', color: '#ff8a5c', icono: I.cubiertos },
  { id: 'ocio', nombre: 'Ocio', tipo: 'Gasto', grupo: 'Día a día', color: '#c77dff', icono: I.ticket },
  { id: 'ropa', nombre: 'Ropa', tipo: 'Gasto', grupo: 'Día a día', color: '#ff5fa2', icono: I.camiseta },
  { id: 'salud', nombre: 'Salud', tipo: 'Gasto', grupo: 'Día a día', color: '#2ec4b6', icono: I.corazon },
  { id: 'tecnologia', nombre: 'Tecnología', tipo: 'Gasto', grupo: 'Día a día', color: '#4cc9f0', icono: I.portatil },
  { id: 'transporte', nombre: 'Transporte', tipo: 'Gasto', grupo: 'Día a día', color: '#90a4ae', icono: I.bus },
  { id: 'suscripciones', nombre: 'Suscripciones', tipo: 'Gasto', grupo: 'Suscripciones', color: '#e62429', icono: I.play },
  { id: 'gasolina', nombre: 'Gasolina', tipo: 'Gasto', grupo: 'Coche', color: '#ff7a1f', icono: I.surtidor },
  { id: 'seguro', nombre: 'Seguro', tipo: 'Gasto', grupo: 'Coche', color: '#8e9aaf', icono: I.escudo },
  { id: 'mecanico', nombre: 'Mecánico', tipo: 'Gasto', grupo: 'Coche', color: '#c08f62', icono: I.llave },
  { id: 'cuota-coche', nombre: 'Cuota del coche', tipo: 'Gasto', grupo: 'Coche', color: '#f4a261', icono: I.coche },
  { id: 'coche-extras', nombre: 'Papeleo y extras', tipo: 'Gasto', grupo: 'Coche', color: '#b5838d', icono: I.documento },
  { id: 'otros', nombre: 'Otros gastos', tipo: 'Gasto', grupo: 'Día a día', color: '#8d979e', icono: I.puntos },
  { id: 'nomina', nombre: 'Nómina', tipo: 'Ingreso', grupo: 'Ingresos', color: '#3ddc84', icono: I.maletin },
  { id: 'extra', nombre: 'Extra', tipo: 'Ingreso', grupo: 'Ingresos', color: '#a7e05a', icono: I.estrella },
  { id: 'ventas', nombre: 'Ventas', tipo: 'Ingreso', grupo: 'Ingresos', color: '#2ec4b6', icono: I.etiqueta },
  { id: 'otros-ingresos', nombre: 'Otros ingresos', tipo: 'Ingreso', grupo: 'Ingresos', color: '#7fc8a9', icono: I.mas },
];

export const ICONO_MAS = I.mas;
const POR_ID = new Map(CATEGORIAS.map((c) => [c.id, c]));
export const categoria = (id: string): Categoria => POR_ID.get(id) ?? { id, nombre: id, tipo: 'Gasto', grupo: 'Otros', color: '#8d979e', icono: I.puntos };

// ---- Formato ----
const fmtEuros = new Intl.NumberFormat('es-ES', { style: 'currency', currency: 'EUR' });
const fmtEurosCorto = new Intl.NumberFormat('es-ES', { style: 'currency', currency: 'EUR', maximumFractionDigits: 0 });
export const euros = (n: number | null | undefined) => (n == null ? '—' : fmtEuros.format(n));
export const eurosCorto = (n: number | null | undefined) => (n == null ? '—' : Math.abs(n) >= 1000 ? fmtEurosCorto.format(n) : fmtEuros.format(n));
export const numero = (n: number | null | undefined, decimales = 2) =>
  n == null ? '—' : n.toLocaleString('es-ES', { minimumFractionDigits: 0, maximumFractionDigits: decimales });

export const MESES = ['enero', 'febrero', 'marzo', 'abril', 'mayo', 'junio', 'julio', 'agosto', 'septiembre', 'octubre', 'noviembre', 'diciembre'];
export const mesLargo = (anio: number, mes: number) => `${MESES[mes - 1][0].toUpperCase()}${MESES[mes - 1].slice(1)} ${anio}`;
export const mesCorto = (anio: number, mes: number) => `${MESES[mes - 1].slice(0, 3)} ${String(anio).slice(2)}`;

// fecha ISO local (sin desfase de zona horaria)
export const hoyIso = () => {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
};
export const diaLargo = (iso: string) =>
  new Date(`${iso}T12:00:00`).toLocaleDateString('es-ES', { weekday: 'long', day: 'numeric', month: 'long' });
export const diaCorto = (iso: string) => new Date(`${iso}T12:00:00`).toLocaleDateString('es-ES', { day: 'numeric', month: 'short' });

export const FRECUENCIAS: { id: Frecuencia; nombre: string; cada: string }[] = [
  { id: 'Mensual', nombre: 'Mensual', cada: 'cada mes' },
  { id: 'Trimestral', nombre: 'Trimestral', cada: 'cada 3 meses' },
  { id: 'Anual', nombre: 'Anual', cada: 'cada año' },
];
