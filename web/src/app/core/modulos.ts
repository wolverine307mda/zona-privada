// Secciones de la zona privada: las del menú lateral y lo que traerá cada una.
// «listo» indica si ya tiene pantalla; las demás muestran su página de «próximamente».
export interface Modulo {
  id: string;
  titulo: string;
  resumen: string;
  icono: string; // trazos SVG (viewBox 0 0 24 24)
  listo: boolean;
  planes?: string[];
}

export const INICIO: Modulo = {
  id: 'inicio', titulo: 'Inicio', resumen: 'El equipo de casa en directo.', listo: true,
  icono: 'M3 11l9-7 9 7M5 10v10h5v-6h4v6h5V10',
};

export const MODULOS: Modulo[] = [
  {
    id: 'fotos', titulo: 'Fotos', resumen: 'Mi laboratorio de fotografía.', listo: false,
    icono: 'M4 7h3l2-3h6l2 3h3v12H4zM12 17a4 4 0 1 0 0-8 4 4 0 0 0 0 8z',
    planes: ['Subir fotos desde el móvil', 'Convertir a WebP y sacar los datos de la cámara', 'Publicar en la galería de la web con un botón'],
  },
  {
    id: 'musica', titulo: 'Música', resumen: 'Lo que suena, con estadísticas de Spotify.', listo: false,
    icono: 'M9 18V5l12-2v13M9 18a3 3 0 1 1-6 0 3 3 0 0 1 6 0zM21 16a3 3 0 1 1-6 0 3 3 0 0 1 6 0z',
    planes: ['Lo más escuchado del mes', '«Sonando ahora» también en la web pública', 'La clave de Spotify, a salvo en el servidor'],
  },
  {
    id: 'proyectos', titulo: 'Proyectos', resumen: 'Repos, despliegues e ideas.', listo: false,
    icono: 'M3 7l4-4h4l2 2h8v14H3zM8 13l-2 2 2 2M16 13l2 2-2 2M13 12l-2 6',
    planes: ['Mis repos de GitHub: commits y lenguajes', 'Estado de los despliegues', 'Tablero de ideas y tareas'],
  },
  {
    id: 'notas', titulo: 'Notas', resumen: 'Notas rápidas y tareas.', listo: false,
    icono: 'M6 3h9l5 5v13H6zM14 3v6h6M9 13h8M9 17h5',
    planes: ['Notas con Markdown', 'Lista de tareas con fechas', 'Búsqueda al instante'],
  },
  {
    id: 'gastos', titulo: 'Gastos', resumen: 'Ingresos, gastos, fijos y ahorro.', listo: true,
    icono: 'M3 6h18v12H3zM3 10h18M7 15h3',
    planes: ['Gastos e ingresos por categoría', 'Gráficas por mes', 'Exportar a Excel'],
  },
  {
    id: 'documentos', titulo: 'Documentos', resumen: 'Bóveda de documentos cifrada.', listo: false,
    icono: 'M5 11h14v10H5zM8 11V7a4 4 0 0 1 8 0v4',
    planes: ['DNI, certificados y nóminas cifrados en el equipo', 'Nunca en la nube', 'Acceso con verificación en dos pasos'],
  },
  {
    id: 'seguridad', titulo: 'Seguridad', resumen: 'Verificación en dos pasos y sesiones.', listo: false,
    icono: 'M12 3l8 3v6c0 5-3.5 8-8 9-4.5-1-8-4-8-9V6zM9 12l2 2 4-4',
    planes: ['Activar la verificación en dos pasos con el móvil', 'Cambiar la contraseña', 'Ver y cerrar sesiones abiertas'],
  },
];
