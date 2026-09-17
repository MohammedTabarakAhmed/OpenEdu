import { Component, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { PresentableError, toPresentableError } from '../../core/api/problem';
import { TranslatePipe } from '../../core/i18n/i18n.service';
import { BilingualPipe } from '../../shared/ui';
import { ContentApi } from './content.api';
import { SectionSummary } from './content.models';

/**
 * The caller's own sections: assigned as instructor (`mode` = teaching) or enrolled as learner (`mode` = enrolled).
 * The server scopes the list (SEC-12); each row links to that section's content.
 */
@Component({
  imports: [RouterLink, TranslatePipe, BilingualPipe],
  template: `
    <h2 class="h5 mb-3">{{ (mode() === 'teaching' ? 'content.mySections' : 'content.myCourses') | t }}</h2>

    @if (loading()) {
      <p class="text-secondary" role="status" data-testid="state-loading">{{ 'common.loading' | t }}</p>
    } @else if (error()) {
      <div class="alert alert-danger" role="alert" data-testid="state-error">{{ 'common.loadFailed' | t }}</div>
    } @else if (sections().length === 0) {
      <p class="text-secondary" data-testid="state-empty">{{ (mode() === 'teaching' ? 'content.noSections' : 'content.noCourses') | t }}</p>
    } @else {
      <div class="list-group" data-testid="section-list">
        @for (s of sections(); track s.id) {
          <a class="list-group-item list-group-item-action d-flex flex-wrap justify-content-between align-items-center gap-2" [routerLink]="[s.id, 'content']" [attr.data-testid]="'section-' + s.code">
            <span><code>{{ s.courseCode }}</code> {{ s | bilingual: 'courseName' }}</span>
            <span class="small text-secondary">{{ s.code }} · {{ s.termName }} · {{ 'sections.status.' + s.status | t }}</span>
          </a>
        }
      </div>
    }
  `,
})
export class MySectionsPage {
  readonly mode = input.required<'teaching' | 'enrolled'>();

  private readonly api = inject(ContentApi);
  protected readonly sections = signal<SectionSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<PresentableError | null>(null);

  constructor() {
    queueMicrotask(() => {
      const source = this.mode() === 'teaching' ? this.api.teaching() : this.api.enrolled();
      source.subscribe({
        next: (list) => {
          this.sections.set(list);
          this.loading.set(false);
        },
        error: (failure: unknown) => {
          this.error.set(toPresentableError(failure));
          this.loading.set(false);
        },
      });
    });
  }
}
