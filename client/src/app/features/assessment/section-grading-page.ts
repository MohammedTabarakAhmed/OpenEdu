import { Component, inject, input, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { PresentableError, toPresentableError } from '../../core/api/problem';
import { TranslatePipe } from '../../core/i18n/i18n.service';
import { BilingualPipe, ConfirmService, LocaleNumberPipe, SubmitError } from '../../shared/ui';
import { AssessmentApi } from './assessment.api';
import { GradebookResponse, GradebookRow } from './assessment.models';

/**
 * The section gradebook (15.3): record or amend each enrolment × component score (BR-05), then release the
 * section (BR-07) so learners see their results (BR-06). Scope (SEC-12) is resolved by the server.
 */
@Component({
  imports: [ReactiveFormsModule, RouterLink, RouterLinkActive, TranslatePipe, BilingualPipe, SubmitError, LocaleNumberPipe],
  templateUrl: './section-grading-page.html',
})
export class SectionGradingPage {
  readonly id = input.required<string>();

  private readonly api = inject(AssessmentApi);
  private readonly fb = inject(FormBuilder);
  private readonly confirm = inject(ConfirmService);

  protected readonly gradebook = signal<GradebookResponse | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadError = signal<PresentableError | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<PresentableError | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly editing = signal<string | null>(null);

  protected readonly scoreForm = this.fb.nonNullable.group({ score: [0, [Validators.required, Validators.min(0)]] });

  constructor() {
    queueMicrotask(() => this.load());
  }

  protected load(): void {
    this.loading.set(true);
    this.loadError.set(null);
    this.api.gradebook(this.id()).subscribe({
      next: (g) => {
        this.gradebook.set(g);
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        this.loadError.set(toPresentableError(failure));
        this.loading.set(false);
      },
    });
  }

  protected editKey(enrolmentId: string, componentId: string): string {
    return `${enrolmentId}:${componentId}`;
  }

  protected scoreFor(row: GradebookRow, componentId: string): number | null {
    return row.entries.find((e) => e.gradeComponentId === componentId)?.score ?? null;
  }

  protected openEdit(enrolmentId: string, componentId: string, currentScore: number | null): void {
    this.scoreForm.reset({ score: currentScore ?? 0 });
    this.error.set(null);
    this.editing.set(this.editKey(enrolmentId, componentId));
  }

  protected saveScore(enrolmentId: string, componentId: string): void {
    if (this.scoreForm.invalid || this.busy()) {
      this.scoreForm.markAllAsTouched();
      return;
    }
    const { score } = this.scoreForm.getRawValue();
    this.busy.set(true);
    this.error.set(null);
    this.api.recordGrade(this.id(), enrolmentId, componentId, score).subscribe({
      next: () => {
        this.busy.set(false);
        this.editing.set(null);
        this.notice.set('common.saved');
        this.load();
      },
      error: (failure: unknown) => {
        this.busy.set(false);
        this.error.set(toPresentableError(failure));
      },
    });
  }

  protected release(): void {
    if (this.busy() || !this.confirm.confirm('assessment.confirmRelease')) {
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.api.release(this.id()).subscribe({
      next: () => {
        this.busy.set(false);
        this.notice.set('common.saved');
        this.load();
      },
      error: (failure: unknown) => {
        this.busy.set(false);
        this.error.set(toPresentableError(failure));
      },
    });
  }
}
