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
        path: 'gastos', title: 'Gastos · MDA', loadComponent: () => import('./gastos/gastos').then((m) => m.Gastos),
        children: [
          { path: '', loadComponent: () => import('./gastos/resumen').then((m) => m.Resumen) },
          { path: 'movimientos', loadComponent: () => import('./gastos/movimientos').then((m) => m.Movimientos) },
          { path: 'fijos', loadComponent: () => import('./gastos/fijos').then((m) => m.Fijos) },
          { path: 'gasolina', loadComponent: () => import('./gastos/gasolina').then((m) => m.Gasolina) },
          { path: 'deudas', loadComponent: () => import('./gastos/deudas').then((m) => m.Deudas) },
          { path: 'estadisticas', loadComponent: () => import('./gastos/estadisticas').then((m) => m.Estadisticas) },
        ],
      },
      { path: 'documentos', title: 'Documentos · MDA', loadComponent: () => import('./documentos/documentos').then((m) => m.Documentos) },
      { path: 'agenda', title: 'Agenda · MDA', loadComponent: () => import('./agenda/agenda').then((m) => m.Agenda) },
      { path: 'notas', title: 'Notas · MDA', loadComponent: () => import('./notas/notas').then((m) => m.Notas) },
      { path: 'musica', title: 'Música · MDA', loadComponent: () => import('./musica/musica').then((m) => m.Musica) },
      { path: 'seguridad', title: 'Seguridad · MDA', loadComponent: () => import('./seguridad/seguridad').then((m) => m.Seguridad) },
      { path: 'pronto/:id', title: 'Próximamente · MDA', loadComponent: () => import('./pronto/pronto').then((m) => m.Pronto) },
    ],
  },
  { path: 'panel', redirectTo: 'inicio' },
  { path: '**', redirectTo: 'inicio' },
];
