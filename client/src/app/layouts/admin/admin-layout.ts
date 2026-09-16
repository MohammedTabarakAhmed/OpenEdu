import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { SessionService } from '../../core/auth/session.service';
import { TranslatePipe } from '../../core/i18n/i18n.service';
import { ResourceKey } from '../../core/i18n/resources';

interface AdminLink {
  path: string;
  label: ResourceKey;
  permission: string;
  testId: string;
}

/** The administrative shell (17.1): navigation shows only functions the user holds a permission for (17.3). */
@Component({
  imports: [RouterOutlet, RouterLink, RouterLinkActive, TranslatePipe],
  template: `
    <div class="row g-4">
      <nav class="col-12 col-md-3 col-lg-2" [attr.aria-label]="'shell.admin' | t">
        <h1 class="h5">{{ 'shell.admin' | t }}</h1>
        <ul class="nav nav-pills flex-column">
          @for (link of links; track link.path) {
            @if (session.hasPermission(link.permission)) {
              <li class="nav-item">
                <a class="nav-link" [routerLink]="link.path" routerLinkActive="active" [attr.data-testid]="link.testId">{{ link.label | t }}</a>
              </li>
            }
          }
        </ul>
      </nav>
      <div class="col-12 col-md-9 col-lg-10">
        <router-outlet />
      </div>
    </div>
  `,
})
export class AdminLayout {
  protected readonly session = inject(SessionService);

  protected readonly links: AdminLink[] = [
    { path: 'programmes', label: 'admin.nav.programmes', permission: 'sis.programme.read', testId: 'nav-programmes' },
    { path: 'courses', label: 'admin.nav.courses', permission: 'sis.course.read', testId: 'nav-courses' },
    { path: 'sections', label: 'admin.nav.sections', permission: 'sis.section.read', testId: 'nav-sections' },
    { path: 'learners', label: 'admin.nav.learners', permission: 'sis.learner.read', testId: 'nav-learners' },
    { path: 'users', label: 'admin.nav.users', permission: 'identity.user.read', testId: 'nav-users' },
  ];
}
