import { Routes } from '@angular/router';
import { anonymousOnlyGuard, landingRedirectGuard, roleGuard } from './core/auth/auth.guards';
import { Roles } from './core/auth/auth.models';

/** Feature areas are lazily loaded (SDD 17.1); route protection reflects roles (17.3). */
export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    canActivate: [landingRedirectGuard],
    children: [],
  },
  {
    path: 'login',
    canActivate: [anonymousOnlyGuard],
    loadComponent: () => import('./features/auth/login').then((m) => m.Login),
  },
  {
    path: 'admin',
    canActivate: [roleGuard(Roles.Administrator, Roles.Registrar)],
    loadComponent: () => import('./layouts/admin/admin-layout').then((m) => m.AdminLayout),
  },
  {
    path: 'instructor',
    canActivate: [roleGuard(Roles.Instructor)],
    loadComponent: () => import('./layouts/instructor/instructor-layout').then((m) => m.InstructorLayout),
  },
  {
    path: 'learner',
    canActivate: [roleGuard(Roles.Learner)],
    loadComponent: () => import('./layouts/learner/learner-layout').then((m) => m.LearnerLayout),
  },
  { path: '**', redirectTo: '' },
];
