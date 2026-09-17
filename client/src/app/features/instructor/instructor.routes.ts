import { Routes } from '@angular/router';
import { permissionGuard } from '../../core/auth/auth.guards';

/** Instructor feature area (17.1), lazily loaded: assigned sections and their content (15.3 "Content delivery"). */
export const instructorRoutes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'sections' },
  {
    path: 'sections',
    canActivate: [permissionGuard('lms.content.write')],
    loadComponent: () => import('../content/my-sections-page').then((m) => m.MySectionsPage),
    data: { mode: 'teaching' },
  },
  {
    path: 'sections/:id/content',
    canActivate: [permissionGuard('lms.content.write')],
    loadComponent: () => import('../content/section-content-page').then((m) => m.SectionContentPage),
  },
  {
    path: 'sections/:id/assignments',
    canActivate: [permissionGuard('lms.assignment.write')],
    loadComponent: () => import('../assessment/section-assignments-page').then((m) => m.SectionAssignmentsPage),
  },
  {
    path: 'sections/:id/attendance',
    canActivate: [permissionGuard('lms.attendance.write')],
    loadComponent: () => import('../assessment/section-attendance-page').then((m) => m.SectionAttendancePage),
  },
  {
    path: 'sections/:id/grades',
    canActivate: [permissionGuard('sis.grade.write')],
    loadComponent: () => import('../assessment/section-grading-page').then((m) => m.SectionGradingPage),
  },
];
