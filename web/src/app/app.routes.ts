import { Routes } from '@angular/router';
import { conSesion, sinSesion } from './core/guards';

// «entrar» suelta; el resto va dentro del marco con menú lateral (shell).
// Cada pantalla se carga aparte: Three.js solo en «entrar» y ECharts solo en «inicio».
export const routes: Routes = [
  { path: 'entrar', title: 'Entrar · MDA', canActivate: [sinSesion], loadComponent: () => import('./entrar/entrar').then((m) => m.Entrar) },
  {
    path: '',
    canActivate: [conSesion],
    loadComponent: () => import('./shell/shell').then((m) => m.Shell),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'inicio' },
      { path: 'inicio', title: 'Inicio · MDA', loadComponent: () => import('./inicio/inicio').then((m) => m.Inicio) },
      {
        path: 'gastos', title: 'Cartera · MDA', loadComponent: () => import('./gastos/gastos').then((m) => m.Gastos),
        children: [
          { path: '', loadComponent: () => import('./gastos/resumen').then((m) => m.Resumen) },
          { path: 'movimientos', title: 'Movimientos · MDA', loadComponent: () => import('./gastos/movimientos').then((m) => m.Movimientos) },
          { path: 'importar', title: 'Importar extracto · MDA', loadComponent: () => import('./gastos/importar').then((m) => m.Importar) },
          { path: 'fijos', title: 'Fijos · MDA', loadComponent: () => import('./gastos/fijos').then((m) => m.Fijos) },
          { path: 'huchas', title: 'Huchas · MDA', loadComponent: () => import('./gastos/huchas').then((m) => m.Huchas) },
          { path: 'gasolina', title: 'Gasolina · MDA', loadComponent: () => import('./gastos/gasolina').then((m) => m.Gasolina) },
          { path: 'deudas', title: 'Coche y deudas · MDA', loadComponent: () => import('./gastos/deudas').then((m) => m.Deudas) },
          { path: 'estadisticas', title: 'Estadísticas · MDA', loadComponent: () => import('./gastos/estadisticas').then((m) => m.Estadisticas) },
        ],
      },
      { path: 'casa', title: 'Casa · MDA', loadComponent: () => import('./casa/casa').then((m) => m.Casa) },
      { path: 'pc', title: 'Mi PC · MDA', loadComponent: () => import('./pc/pc').then((m) => m.Pc) },
      { path: 'fotografia/previsiones', title: 'Previsiones · Fotografía · MDA', loadComponent: () => import('./fotografia/secciones').then((m) => m.Previsiones) },
      { path: 'fotografia/sitios', title: 'Mis sitios · Fotografía · MDA', loadComponent: () => import('./fotografia/secciones').then((m) => m.Sitios) },
      { path: 'fotografia/herramientas', title: 'Herramientas · Fotografía · MDA', loadComponent: () => import('./fotografia/secciones').then((m) => m.Herramientas) },
      { path: 'fotografia/ar', title: 'Sol y luna en AR · MDA', loadComponent: () => import('./fotografia/ar').then((m) => m.Ar) },
      { path: 'fotografia', title: 'Fotografía · MDA', loadComponent: () => import('./fotografia/fotografia').then((m) => m.Fotografia) },
      { path: 'fotos', title: 'Galería · MDA', loadComponent: () => import('./galeria/galeria').then((m) => m.Galeria) },
      { path: 'documentos', title: 'Documentos · MDA', loadComponent: () => import('./documentos/documentos').then((m) => m.Documentos) },
      { path: 'agenda', title: 'Agenda · MDA', loadComponent: () => import('./agenda/agenda').then((m) => m.Agenda) },
      { path: 'notas', title: 'Notas · MDA', loadComponent: () => import('./notas/notas').then((m) => m.Notas) },
      { path: 'musica', title: 'Música · MDA', loadComponent: () => import('./musica/musica').then((m) => m.Musica) },
      { path: 'musica/resumen', title: 'Tu música · MDA', loadComponent: () => import('./musica/resumen-musica').then((m) => m.ResumenMusica) },
      { path: 'perfil', title: 'Perfil · MDA', loadComponent: () => import('./perfil/perfil').then((m) => m.Perfil) },
      { path: 'cartera', redirectTo: 'gastos' },
      { path: 'seguridad', title: 'Seguridad · MDA', loadComponent: () => import('./seguridad/seguridad').then((m) => m.Seguridad) },
      { path: 'pronto/:id', title: 'Próximamente · MDA', loadComponent: () => import('./pronto/pronto').then((m) => m.Pronto) },
    ],
  },
  { path: 'panel', redirectTo: 'inicio' },
  { path: '**', redirectTo: 'inicio' },
];
