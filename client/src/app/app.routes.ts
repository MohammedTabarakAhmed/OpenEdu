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
    // 15.4: certificate verification is anonymous — no guard; a signed-in user may use it too.
    path: 'verify',
    loadComponent: () => import('./features/certificates/verify-certificate-page').then((m) => m.VerifyCertificatePage),
  },
  {
    path: 'verify/:code',
    loadComponent: () => import('./features/certificates/verify-certificate-page').then((m) => m.VerifyCertificatePage),
  },
  {
    // Privacy notice: anonymous, readable before signing in (optional scope, added after the mandatory increments).
    path: 'privacy',
    loadComponent: () => import('./features/legal/privacy-notice-page').then((m) => m.PrivacyNoticePage),
  },
  {
    path: 'terms',
    loadComponent: () => import('./features/legal/terms-of-use-page').then((m) => m.TermsOfUsePage),
  },
  {
    path: 'admin',
    canActivate: [roleGuard(Roles.Administrator, Roles.Registrar)],
    loadComponent: () => import('./layouts/admin/admin-layout').then((m) => m.AdminLayout),
    loadChildren: () => import('./features/admin/admin.routes').then((m) => m.adminRoutes),
  },
  {
    path: 'instructor',
    canActivate: [roleGuard(Roles.Instructor)],
    loadComponent: () => import('./layouts/instructor/instructor-layout').then((m) => m.InstructorLayout),
    loadChildren: () => import('./features/instructor/instructor.routes').then((m) => m.instructorRoutes),
  },
  {
    path: 'learner',
    canActivate: [roleGuard(Roles.Learner)],
    loadComponent: () => import('./layouts/learner/learner-layout').then((m) => m.LearnerLayout),
    loadChildren: () => import('./features/learner/learner.routes').then((m) => m.learnerRoutes),
  },
  { path: '**', redirectTo: '' },
];
