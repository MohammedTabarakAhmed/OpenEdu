import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { SessionService } from '../../core/auth/session.service';
import { TranslatePipe } from '../../core/i18n/i18n.service';

@Component({
  imports: [RouterOutlet, TranslatePipe],
  template: `
    <h1 class="h4">{{ 'shell.admin' | t }}</h1>
    @if (session.principal(); as user) {
      <p class="text-secondary" data-testid="shell-user">{{ 'shell.welcome' | t }} {{ user.userName }}</p>
    }
    <router-outlet />
  `,
})
export class AdminLayout {
  protected readonly session = inject(SessionService);
}
