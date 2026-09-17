import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '../../core/i18n/i18n.service';
import { Mark } from '../../shared/brand';
import { LocaleDatePipe } from '../../shared/ui';

/**
 * Privacy notice — optional scope added after the mandatory increments (SDD section 3 permits optional scope only
 * then). A page of text, no behaviour: it states what the system as built holds, why, who can see it, how it is
 * protected (section 16), how long it is kept (DC-03: academic records are retained, accounts are deactivated, never
 * physically deleted) and where to direct requests. Anonymous like `/verify`, so it can be read before signing in.
 * The date is the notice's own revision date, updated whenever the text changes.
 */
@Component({
  imports: [RouterLink, TranslatePipe, LocaleDatePipe, Mark],
  template: `
    <div class="row justify-content-center">
      <div class="col-12 col-lg-8">
        <article class="card">
          <div class="card-body p-4">
            <p class="oc-brand mb-1"><app-mark [size]="28" /> {{ 'app.title' | t }}</p>
            <h1 class="h4 mb-0" data-testid="privacy-title">{{ 'privacy.title' | t }}</h1>
            <span class="oc-horizon" aria-hidden="true"></span>
            <p class="text-secondary small mb-1">{{ 'privacy.updated' | t }}: {{ revised | localeDate: 'date' }}</p>
            <p>{{ 'privacy.intro' | t }}</p>

            <h2 class="h6 mt-4">{{ 'privacy.data.title' | t }}</h2>
            <ul>
              <li>{{ 'privacy.data.account' | t }}</li>
              <li>{{ 'privacy.data.learner' | t }}</li>
              <li>{{ 'privacy.data.academic' | t }}</li>
              <li>{{ 'privacy.data.technical' | t }}</li>
            </ul>

            <h2 class="h6 mt-4">{{ 'privacy.purpose.title' | t }}</h2>
            <p>{{ 'privacy.purpose.body' | t }}</p>

            <h2 class="h6 mt-4">{{ 'privacy.access.title' | t }}</h2>
            <p>{{ 'privacy.access.body' | t }}</p>

            <h2 class="h6 mt-4">{{ 'privacy.protection.title' | t }}</h2>
            <ul>
              <li>{{ 'privacy.protection.transport' | t }}</li>
              <li>{{ 'privacy.protection.credentials' | t }}</li>
              <li>{{ 'privacy.protection.files' | t }}</li>
              <li>{{ 'privacy.protection.audit' | t }}</li>
            </ul>

            <h2 class="h6 mt-4">{{ 'privacy.cookies.title' | t }}</h2>
            <p data-testid="privacy-cookies">{{ 'privacy.cookies.body' | t }}</p>

            <h2 class="h6 mt-4">{{ 'privacy.retention.title' | t }}</h2>
            <p data-testid="privacy-retention">{{ 'privacy.retention.body' | t }}</p>

            <h2 class="h6 mt-4">{{ 'privacy.notices.title' | t }}</h2>
            <p>{{ 'privacy.notices.body' | t }}</p>

            <h2 class="h6 mt-4">{{ 'privacy.contact.title' | t }}</h2>
            <p class="mb-0">{{ 'privacy.contact.body' | t }}</p>

            <p class="mt-4 mb-0"><a routerLink="/verify">{{ 'verify.title' | t }}</a></p>
          </div>
        </article>
      </div>
    </div>
  `,
})
export class PrivacyNoticePage {
  /** Revision date of the notice text (not the build date), so readers can tell whether they have seen this version. */
  protected readonly revised = '2026-09-17';
}
