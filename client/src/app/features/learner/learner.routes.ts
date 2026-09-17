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
  {
    path: 'courses/:id/assignments',
    canActivate: [permissionGuard('lms.assignment.read')],
    loadComponent: () => import('../assessment/section-assignments-page').then((m) => m.SectionAssignmentsPage),
  },
  {
    path: 'courses/:id/attendance',
    canActivate: [permissionGuard('lms.attendance.read')],
    loadComponent: () => import('../assessment/section-attendance-page').then((m) => m.SectionAttendancePage),
  },
  {
    path: 'certificates',
    canActivate: [permissionGuard('sis.enrolment.read')],
    loadComponent: () => import('../certificates/my-certificates-page').then((m) => m.MyCertificatesPage),
  },
  {
    path: 'enrolments/:id/results',
    canActivate: [permissionGuard('sis.grade.read')],
    loadComponent: () => import('../assessment/enrolment-results-page').then((m) => m.EnrolmentResultsPage),
  },
];
