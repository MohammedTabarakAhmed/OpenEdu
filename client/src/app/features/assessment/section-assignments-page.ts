import { Component, computed, inject, input, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { Observable } from 'rxjs';
import { PresentableError, errorsFor, toPresentableError } from '../../core/api/problem';
import { TranslatePipe } from '../../core/i18n/i18n.service';
import { BilingualPipe, ConfirmService, FieldErrors, LocaleDatePipe, LocaleNumberPipe, SubmitError } from '../../shared/ui';
import { BlobSaver } from '../content/content.api';
import { AssessmentApi } from './assessment.api';
import { AssignmentItem, AssignmentSubmissionsResponse, SectionAssignmentsResponse } from './assessment.models';

/**
 * A section's assignments (15.3 "Assessment and grading"): publication and management for the section's
 * manager, and submission for its learners (BR-08, BR-09). The server decides scope (SEC-12) via `canManage`
 * and, for a learner, returns only published assignments together with `mySubmission`.
 */
@Component({
  imports: [ReactiveFormsModule, RouterLink, RouterLinkActive, TranslatePipe, BilingualPipe, LocaleDatePipe, FieldErrors, SubmitError, LocaleNumberPipe],
  templateUrl: './section-assignments-page.html',
})
export class SectionAssignmentsPage {
  readonly id = input.required<string>();

  private readonly api = inject(AssessmentApi);
  private readonly fb = inject(FormBuilder);
  private readonly confirm = inject(ConfirmService);
  private readonly saver = inject(BlobSaver);

  protected readonly data = signal<SectionAssignmentsResponse | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadError = signal<PresentableError | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<PresentableError | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly creating = signal(false);
  protected readonly expanded = signal<string | null>(null);
  protected readonly submissions = signal<AssignmentSubmissionsResponse | null>(null);
  protected readonly markingId = signal<string | null>(null);

  protected readonly canManage = computed(() => this.data()?.canManage === true);

  protected readonly createForm = this.fb.nonNullable.group({
    titleEn: ['', [Validators.required, Validators.maxLength(200)]],
    titleAr: ['', [Validators.required, Validators.maxLength(200)]],
    instructions: [''],
    maxScore: [100, [Validators.required, Validators.min(0.01)]],
    dueAtUtc: ['', Validators.required],
    allowLate: [false],
    latePenaltyPercent: [0, [Validators.required, Validators.min(0), Validators.max(100)]],
  });

  protected readonly submitForm = this.fb.nonNullable.group({ textBody: [''] });
  protected readonly markForm = this.fb.nonNullable.group({
    score: [0, [Validators.required, Validators.min(0)]],
    feedback: [''],
  });

  private pendingFile: File | null = null;

  constructor() {
    queueMicrotask(() => this.load());
  }

  protected load(): void {
    this.loading.set(true);
    this.loadError.set(null);
    this.api.listAssignments(this.id()).subscribe({
      next: (d) => {
        this.data.set(d);
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        this.loadError.set(toPresentableError(failure));
        this.loading.set(false);
      },
    });
  }

  protected fieldErrors(control: string): string[] {
    return errorsFor(this.error(), control);
  }

  // ----- Manager: create, publish, delete -----

  protected createAssignment(): void {
    if (this.createForm.invalid || this.busy()) {
      this.createForm.markAllAsTouched();
      return;
    }
    const v = this.createForm.getRawValue();
    this.run(
      this.api.createAssignment(this.id(), {
        titleEn: v.titleEn,
        titleAr: v.titleAr,
        instructions: v.instructions.trim() === '' ? null : v.instructions,
        maxScore: v.maxScore,
        dueAtUtc: new Date(v.dueAtUtc).toISOString(),
        allowLate: v.allowLate,
        latePenaltyPercent: v.latePenaltyPercent,
      }),
      () => {
        this.createForm.reset({ titleEn: '', titleAr: '', instructions: '', maxScore: 100, dueAtUtc: '', allowLate: false, latePenaltyPercent: 0 });
        this.creating.set(false);
      },
    );
  }

  protected togglePublish(a: AssignmentItem): void {
    this.run(a.isPublished ? this.api.unpublishAssignment(a.id) : this.api.publishAssignment(a.id));
  }

  protected deleteAssignment(a: AssignmentItem): void {
    if (!this.confirm.confirm('assessment.confirmDeleteAssignment')) {
      return;
    }
    this.run(this.api.deleteAssignment(a.id));
  }

  // ----- Manager: submissions and marking -----

  protected openSubmissions(a: AssignmentItem): void {
    this.error.set(null);
    if (this.expanded() === a.id) {
      this.expanded.set(null);
      this.submissions.set(null);
      return;
    }
    this.expanded.set(a.id);
    this.api.listSubmissions(a.id).subscribe({
      next: (s) => this.submissions.set(s),
      error: (failure: unknown) => this.error.set(toPresentableError(failure)),
    });
  }

  protected openMark(submissionId: string, score: number | null, feedback: string | null): void {
    this.markForm.reset({ score: score ?? 0, feedback: feedback ?? '' });
    this.error.set(null);
    this.markingId.set(submissionId);
  }

  protected mark(assignment: AssignmentItem): void {
    const submissionId = this.markingId();
    if (!submissionId || this.markForm.invalid || this.busy()) {
      this.markForm.markAllAsTouched();
      return;
    }
    const { score, feedback } = this.markForm.getRawValue();
    this.busy.set(true);
    this.error.set(null);
    this.api.mark(submissionId, score, feedback.trim() === '' ? null : feedback).subscribe({
      next: () => {
        this.busy.set(false);
        this.markingId.set(null);
        this.notice.set('common.saved');
        this.api.listSubmissions(assignment.id).subscribe({ next: (s) => this.submissions.set(s) });
      },
      error: (failure: unknown) => {
        this.busy.set(false);
        this.error.set(toPresentableError(failure));
      },
    });
  }

  // ----- Learner: submission -----

  protected pickFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.pendingFile = input.files?.[0] ?? null;
  }

  protected submit(a: AssignmentItem): void {
    if (this.busy()) {
      return;
    }
    const { textBody } = this.submitForm.getRawValue();
    this.run(this.api.submit(a.id, textBody.trim() === '' ? null : textBody, this.pendingFile), () => {
      this.submitForm.reset({ textBody: '' });
      this.pendingFile = null;
    });
  }

  protected downloadFile(submissionId: string, fileName: string): void {
    this.error.set(null);
    this.api.downloadSubmissionFile(submissionId).subscribe({
      next: (blob) => this.saver.save(blob, fileName || 'submission'),
      error: (failure: unknown) => this.error.set(toPresentableError(failure)),
    });
  }

  private run(request: Observable<unknown>, onSuccess?: () => void): void {
    this.busy.set(true);
    this.error.set(null);
    this.notice.set(null);
    request.subscribe({
      next: () => {
        this.busy.set(false);
        onSuccess?.();
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
