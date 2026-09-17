import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '../../core/i18n/i18n.service';
import { Mark } from '../../shared/brand';
import { LocaleDatePipe } from '../../shared/ui';
import { OperatorDetailsBlock } from './operator';

/**
 * Terms of use — optional scope, companion to the privacy notice. States, for a system that has no purchases, no
 * self-registration, no marketing and no reviews, exactly that: no fees or refunds arise here, no promotional mail is
 * sent without separate consent and an opt-out, no testimonials or unsupported claims appear, no dark patterns.
 * Anonymous, bilingual, with the operator's details and governing law drawn from `OPERATOR`.
 */
@Component({
  imports: [RouterLink, TranslatePipe, LocaleDatePipe, Mark, OperatorDetailsBlock],
  template: `
    <div class="oc-legal">
      <article class="card">
        <div class="card-body">
          <header class="oc-legal-masthead">
            <p class="oc-brand mb-2"><app-mark [size]="28" /> {{ 'app.title' | t }}</p>
            <h1 class="h3 mb-0" data-testid="terms-title">{{ 'terms.title' | t }}</h1>
            <span class="oc-horizon" aria-hidden="true"></span>
            <p class="text-secondary small mb-3">{{ 'privacy.updated' | t }}: {{ revised | localeDate: 'date' }}</p>
            <p class="oc-legal-lead">{{ 'terms.intro' | t }}</p>
          </header>

          @for (section of sections; track section) {
            <h2 class="h6">{{ 'terms.' + section + '.title' | t }}</h2>
            <p [attr.data-testid]="'terms-' + section">{{ 'terms.' + section + '.body' | t }}</p>
          }

          <app-operator-details />

          <p class="text-center small mt-5 mb-0"><a routerLink="/privacy">{{ 'privacy.title' | t }}</a> &middot; <a routerLink="/verify">{{ 'verify.title' | t }}</a></p>
        </div>
      </article>
    </div>
  `,
})
export class TermsOfUsePage {
  protected readonly revised = '2026-09-17';
  protected readonly sections = ['accounts', 'use', 'content', 'fees', 'comms', 'claims', 'fairness', 'availability', 'law', 'changes'];
}
