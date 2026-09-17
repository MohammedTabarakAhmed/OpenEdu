import { Routes } from '@angular/router';
import { permissionGuard } from '../../core/auth/auth.guards';

/** Learner self-service feature area (15.3), lazily loaded. */
export const learnerRoutes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'catalogue' },
  { path: 'catalogue', loadComponent: () => import('./catalogue-page').then((m) => m.CataloguePage) },
  { path: 'enrolments', loadComponent: () => import('./my-enrolments-page').then((m) => m.MyEnrolmentsPage) },
  {
    path: 'courses',
    canActivate: [permissionGuard('lms.content.read')],
    loadComponent: () => import('../content/my-sections-page').then((m) => m.MySectionsPage),
    data: { mode: 'enrolled' },
  },
  {
    path: 'courses/:id/content',
    canActivate: [permissionGuard('lms.content.read')],
    loadComponent: () => import('../content/section-content-page').then((m) => m.SectionContentPage),
  },
];
