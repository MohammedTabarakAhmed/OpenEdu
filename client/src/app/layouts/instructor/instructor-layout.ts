import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { SessionService } from '../../core/auth/session.service';
import { TranslatePipe } from '../../core/i18n/i18n.service';

/** The instructor shell (17.1): the sections assigned to the caller and their content. */
@Component({
  imports: [RouterOutlet, RouterLink, RouterLinkActive, TranslatePipe],
  template: `
    <div class="d-flex flex-wrap justify-content-between align-items-center gap-2 mb-3">
      <div><h1 class="h5 m-0">{{ 'shell.instructor' | t }}</h1><span class="oc-horizon mb-0" aria-hidden="true"></span></div>
      <ul class="nav nav-pills" [attr.aria-label]="'shell.instructor' | t">
        <li class="nav-item"><a class="nav-link" routerLink="sections" routerLinkActive="active" data-testid="nav-my-sections">{{ 'instructor.nav.sections' | t }}</a></li>
      </ul>
    </div>
    @if (session.principal(); as user) {
      <p class="text-secondary small" data-testid="shell-user">{{ 'shell.welcome' | t }} {{ user.userName }}</p>
    }
    <router-outlet />
  `,
})
export class InstructorLayout {
  protected readonly session = inject(SessionService);
}
