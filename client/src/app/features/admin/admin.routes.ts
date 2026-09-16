import { Routes } from '@angular/router';
import { permissionGuard } from '../../core/auth/auth.guards';

/** Administrative feature area (17.1), lazily loaded; each screen is guarded by the permission its interface requires (17.3). */
export const adminRoutes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'sections' },
  {
    path: 'programmes',
    canActivate: [permissionGuard('sis.programme.read')],
    loadComponent: () => import('./programmes/programmes-page').then((m) => m.ProgrammesPage),
  },
  {
    path: 'courses',
    canActivate: [permissionGuard('sis.course.read')],
    loadComponent: () => import('./courses/courses-page').then((m) => m.CoursesPage),
  },
  {
    path: 'sections',
    canActivate: [permissionGuard('sis.section.read')],
    loadComponent: () => import('./sections/sections-page').then((m) => m.SectionsPage),
  },
  {
    path: 'sections/:id',
    canActivate: [permissionGuard('sis.section.read')],
    loadComponent: () => import('./sections/section-detail-page').then((m) => m.SectionDetailPage),
  },
  {
    path: 'learners',
    canActivate: [permissionGuard('sis.learner.read')],
    loadComponent: () => import('./learners/learners-page').then((m) => m.LearnersPage),
  },
  {
    path: 'learners/:id',
    canActivate: [permissionGuard('sis.learner.read')],
    loadComponent: () => import('./learners/learner-detail-page').then((m) => m.LearnerDetailPage),
  },
  {
    path: 'users',
    canActivate: [permissionGuard('identity.user.read')],
    loadComponent: () => import('./users/users-page').then((m) => m.UsersPage),
  },
];
