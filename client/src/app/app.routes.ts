import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: 'admin',
    loadComponent: () => import('./layouts/admin/admin-layout').then((m) => m.AdminLayout),
  },
  {
    path: 'instructor',
    loadComponent: () => import('./layouts/instructor/instructor-layout').then((m) => m.InstructorLayout),
  },
  {
    path: 'learner',
    loadComponent: () => import('./layouts/learner/learner-layout').then((m) => m.LearnerLayout),
  },
];
