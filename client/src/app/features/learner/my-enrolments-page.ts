import { Component, inject, signal } from '@angular/core';
import { PresentableError, toPresentableError } from '../../core/api/problem';
import { TranslatePipe } from '../../core/i18n/i18n.service';
import { PageState } from '../../shared/page-state';
import { BilingualPipe, ConfirmService, LocaleDatePipe, PageControls, SubmitError } from '../../shared/ui';
import { CatalogueApi } from '../admin/admin.api';
import { Enrolment, Transcript } from '../admin/admin.models';

/** Enrolled course listing and withdrawal for the calling learner (15.3), plus the learner's own transcript summary. */
@Component({
  imports: [TranslatePipe, BilingualPipe, LocaleDatePipe, PageControls, SubmitError],
  template: `
    <div class="d-flex flex-wrap justify-content-between align-items-center gap-2 mb-3">
      <h2 class="h5 m-0">{{ 'myEnrolments.title' | t }}</h2>
      <div class="form-check form-switch">
        <input class="form-check-input" type="checkbox" id="active-only" [checked]="activeOnly()" (change)="toggleActiveOnly($any($event.target).checked)" />
        <label class="form-check-label" for="active-only">{{ 'myEnrolments.activeOnly' | t }}</label>
      </div>
    </div>

    @if (transcript(); as t) {
      <p class="small text-secondary" data-testid="transcript-summary">
        {{ 'learners.number' | t }}: {{ t.learnerNumber }} · {{ 'transcript.completed' | t }}: {{ t.completedCount }} · {{ 'transcript.credits' | t }}: {{ t.creditsEarned }}
      </p>
    }

    <app-submit-error [error]="error()" />
    @if (state.error()?.status === 404) {
      <div class="alert alert-info" role="status" data-testid="no-learner-record">{{ 'myEnrolments.noRecord' | t }}</div>
    } @else {
      <app-page-controls [state]="state" />
    }

    @if (state.items().length > 0) {
      <div class="table-responsive">
        <table class="table table-sm align-middle" data-testid="my-enrolment-table">
          <thead>
            <tr>
              <th scope="col">{{ 'courses.title' | t }}</th>
              <th scope="col">{{ 'sections.term' | t }}</th>
              <th scope="col">{{ 'sections.instructor' | t }}</th>
              <th scope="col">{{ 'enrolments.enrolledAt' | t }}</th>
              <th scope="col">{{ 'common.status' | t }}</th>
              <th scope="col"></th>
            </tr>
          </thead>
          <tbody>
            @for (e of state.items(); track e.id) {
              <tr>
                <td><code>{{ e.section.courseCode }}</code> {{ e.section | bilingual: 'courseName' }}</td>
                <td>{{ e.section.termName }}</td>
                <td>{{ (e.section | bilingual: 'instructorName') || '—' }}</td>
                <td>{{ e.enrolledAtUtc | localeDate: 'datetime' }}</td>
                <td><span class="badge" [class]="'badge text-bg-' + (e.status === 'Active' ? 'success' : e.status === 'Withdrawn' ? 'secondary' : 'warning')">{{ 'enrolments.status.' + e.status | t }}</span></td>
                <td class="text-end">
                  @if (e.status === 'Active' || e.status === 'AtRisk') {
                    <button class="btn btn-link btn-sm text-danger" type="button" (click)="withdraw(e)" [disabled]="busy()">{{ 'enrolments.withdraw' | t }}</button>
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    }
  `,
})
export class MyEnrolmentsPage {
  private readonly api = inject(CatalogueApi);
  private readonly confirm = inject(ConfirmService);

  protected readonly activeOnly = signal(true);
  protected readonly transcript = signal<Transcript | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<PresentableError | null>(null);
  protected readonly state = new PageState<Enrolment>((page, pageSize) => this.api.myEnrolments(page, pageSize, this.activeOnly()));

  constructor() {
    this.state.load();
    this.api.myTranscript().subscribe({ next: (t) => this.transcript.set(t), error: () => this.transcript.set(null) });
  }

  protected toggleActiveOnly(checked: boolean): void {
    this.activeOnly.set(checked);
    this.state.load(1);
  }

  protected withdraw(enrolment: Enrolment): void {
    if (this.busy() || !this.confirm.confirm('enrolments.confirmWithdraw')) {
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.api.withdraw(enrolment.id).subscribe({
      next: () => {
        this.busy.set(false);
        this.state.reload();
      },
      error: (failure: unknown) => {
        this.busy.set(false);
        this.error.set(toPresentableError(failure));
      },
    });
  }
}
