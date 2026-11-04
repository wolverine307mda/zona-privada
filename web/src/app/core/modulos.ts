// Secciones de la zona privada: las del menú lateral y lo que traerá cada una.
// «listo» indica si ya tiene pantalla; las demás muestran su página de «próximamente».
export interface Modulo {
  id: string;
  titulo: string;
  resumen: string;
  icono: string; // trazos SVG (viewBox 0 0 24 24)
  listo: boolean;
  pestana?: boolean; // sale en la barra de abajo del móvil
  planes?: string[];
}

export const INICIO: Modulo = {
  id: 'inicio', titulo: 'Inicio', resumen: 'El equipo de casa en directo.', listo: true,
  icono: 'M3 11l9-7 9 7M5 10v10h5v-6h4v6h5V10',
};

// Los submenús del lateral: así caben todas las secciones sin desplazarse dentro del menú
export interface Grupo {
  id: string;
  titulo: string;
  icono: string;
  modulos: Modulo[];
}

export const MODULOS: Modulo[] = [
  {
    id: 'fotos', titulo: 'Galería', resumen: 'Qué fotos se ven en el portfolio, con su descripción.', listo: true,
    icono: 'M4 7h3l2-3h6l2 3h3v12H4zM12 17a4 4 0 1 0 0-8 4 4 0 0 0 0 8z',
    planes: ['Elegir qué fotos de «Imágenes» se publican', 'Descripción, texto alternativo y orden de cada una', 'Miniaturas WebP sin los datos de la cámara ni el GPS'],
  },
  {
    id: 'fotografia', titulo: 'Fotografía', resumen: 'Cuándo salir a hacer fotografía: el cielo hora a hora, sol, luna, nubes y sombras.', listo: true,
    icono: 'M12 3v2M12 19v2M3 12h2M19 12h2M5.6 5.6l1.4 1.4M17 17l1.4 1.4M5.6 18.4L7 17M17 7l1.4-1.4M12 8a4 4 0 1 0 0 8 4 4 0 0 0 0-8z',
  },
  {
    id: 'musica', titulo: 'Música', resumen: 'Lo que suena, con estadísticas de Spotify.', listo: true,
    icono: 'M9 18V5l12-2v13M9 18a3 3 0 1 1-6 0 3 3 0 0 1 6 0zM21 16a3 3 0 1 1-6 0 3 3 0 0 1 6 0z',
    planes: ['Lo más escuchado del mes', '«Sonando ahora» también en la web pública', 'La clave de Spotify, a salvo en el servidor'],
  },
  {
    id: 'proyectos', titulo: 'Proyectos', resumen: 'Repos, despliegues e ideas.', listo: false,
    icono: 'M3 7l4-4h4l2 2h8v14H3zM8 13l-2 2 2 2M16 13l2 2-2 2M13 12l-2 6',
    planes: ['Mis repos de GitHub: commits y lenguajes', 'Estado de los despliegues', 'Tablero de ideas y tareas'],
  },
  {
    id: 'notas', titulo: 'Notas', resumen: 'Notas rápidas, listas y tareas.', listo: true, pestana: true,
    icono: 'M6 3h9l5 5v13H6zM14 3v6h6M9 13h8M9 17h5',
    planes: ['Notas con Markdown', 'Lista de tareas con fechas', 'Búsqueda al instante'],
  },
  {
    id: 'gastos', titulo: 'Cartera', resumen: 'Tu dinero: saldo, ingresos, gastos, huchas y ahorro.', listo: true, pestana: true,
    icono: 'M4 7a2 2 0 0 1 2-2h12v4M4 7v11a2 2 0 0 0 2 2h14V9H6a2 2 0 0 1-2-2zM16 14.5h.01',
    planes: ['Gastos e ingresos por categoría', 'Gráficas por mes', 'Exportar a Excel'],
  },
  {
    id: 'agenda', titulo: 'Agenda', resumen: 'Eventos, recordatorios y Google Calendar.', listo: true, pestana: true,
    icono: 'M4 5h16v15H4zM4 10h16M8 3v4M16 3v4M8 14h2M14 14h2M8 17h2',
  },
  {
    id: 'documentos', titulo: 'Documentos', resumen: 'Las carpetas del equipo, desde cualquier sitio.', listo: true,
    icono: 'M3 6.5A1.5 1.5 0 0 1 4.5 5H9l2 2.5h8.5A1.5 1.5 0 0 1 21 9v9.5a1.5 1.5 0 0 1-1.5 1.5h-15A1.5 1.5 0 0 1 3 18.5z',
    planes: ['Documentos, música, imágenes y vídeos del equipo', 'Subir y descargar desde el móvil', 'Papelera para recuperar lo borrado'],
  },
  {
    id: 'pc', titulo: 'Mi PC', resumen: 'Salud del equipo y órdenes: bloquear, mantener despierto, reiniciar.', listo: true,
    icono: 'M4 5h16v10H4zM9 19h6M12 15v4',
  },
  {
    id: 'seguridad', titulo: 'Seguridad', resumen: 'Sesiones abiertas, accesos y verificación en dos pasos.', listo: true,
    icono: 'M12 3l8 3v6c0 5-3.5 8-8 9-4.5-1-8-4-8-9V6zM9 12l2 2 4-4',
    planes: ['Activar la verificación en dos pasos con el móvil', 'Cambiar la contraseña', 'Ver y cerrar sesiones abiertas'],
  },
];

const GRUPOS_DEF = [
  { id: 'vida', titulo: 'Vida', icono: 'M12 21s-7-4.4-9-9a5 5 0 0 1 9-3 5 5 0 0 1 9 3c-2 4.6-9 9-9 9z', ids: ['gastos', 'agenda', 'notas'] },
  { id: 'medios', titulo: 'Medios', icono: 'M4 5h16v14H4zM10 9l5 3-5 3z', ids: ['fotos', 'fotografia', 'musica'] },
  { id: 'equipo', titulo: 'Mi equipo', icono: 'M9 3v3M15 3v3M9 18v3M15 18v3M3 9h3M3 15h3M18 9h3M18 15h3M7 6h10v12H7z', ids: ['pc', 'documentos', 'seguridad'] },
];

export const GRUPOS: Grupo[] = GRUPOS_DEF.map((g) => ({
  id: g.id, titulo: g.titulo, icono: g.icono,
  modulos: g.ids.map((id) => MODULOS.find((m) => m.id === id)).filter((m): m is Modulo => !!m && m.listo),
}));
