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
      { path: 'pronto/:id', title: 'Próximamente · MDA', loadComponent: () => import('./pronto/pronto').then((m) => m.Pronto) },
    ],
  },
  { path: 'panel', redirectTo: 'inicio' },
  { path: '**', redirectTo: 'inicio' },
];
