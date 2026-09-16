import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { SessionService } from '../../core/auth/session.service';
import { TranslatePipe } from '../../core/i18n/i18n.service';

/** The learner shell (17.1): catalogue and the learner's own enrolments. */
@Component({
  imports: [RouterOutlet, RouterLink, RouterLinkActive, TranslatePipe],
  template: `
    <div class="d-flex flex-wrap justify-content-between align-items-center gap-2 mb-3">
      <h1 class="h5 m-0">{{ 'shell.learner' | t }}</h1>
      <ul class="nav nav-pills" [attr.aria-label]="'shell.learner' | t">
        <li class="nav-item"><a class="nav-link" routerLink="catalogue" routerLinkActive="active" data-testid="nav-catalogue">{{ 'learner.nav.catalogue' | t }}</a></li>
        <li class="nav-item"><a class="nav-link" routerLink="enrolments" routerLinkActive="active" data-testid="nav-my-enrolments">{{ 'learner.nav.enrolments' | t }}</a></li>
      </ul>
    </div>
    @if (session.principal(); as user) {
      <p class="text-secondary small" data-testid="shell-user">{{ 'shell.welcome' | t }} {{ user.userName }}</p>
    }
    <router-outlet />
  `,
})
export class LearnerLayout {
  protected readonly session = inject(SessionService);
}
