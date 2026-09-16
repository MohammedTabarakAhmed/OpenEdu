import { Component, inject, input, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { PresentableError, errorsFor, toPresentableError } from '../../../core/api/problem';
import { SessionService } from '../../../core/auth/session.service';
import { TranslatePipe } from '../../../core/i18n/i18n.service';
import { PageState } from '../../../shared/page-state';
import { BilingualPipe, ConfirmService, FieldErrors, LocaleDatePipe, PageControls, SubmitError } from '../../../shared/ui';
import { EnrolmentsApi, LearnersApi } from '../admin.api';
import { Enrolment, GENDERS, LEARNER_STATUSES, Learner, Transcript } from '../admin.models';

/** One learner: record amendment, enrolments by learner, and the transcript (15.3). */
@Component({
  imports: [ReactiveFormsModule, RouterLink, TranslatePipe, BilingualPipe, LocaleDatePipe, PageControls, FieldErrors, SubmitError],
  templateUrl: './learner-detail-page.html',
})
export class LearnerDetailPage {
  readonly id = input.required<string>();

  private readonly api = inject(LearnersApi);
  private readonly enrolmentsApi = inject(EnrolmentsApi);
  private readonly fb = inject(FormBuilder);
  private readonly confirm = inject(ConfirmService);
  protected readonly session = inject(SessionService);

  protected readonly statuses = LEARNER_STATUSES;
  protected readonly genders = GENDERS;
  protected readonly learner = signal<Learner | null>(null);
  protected readonly transcript = signal<Transcript | null>(null);
  protected readonly loadError = signal<PresentableError | null>(null);
  protected readonly busy = signal(false);
  protected readonly saved = signal(false);
  protected readonly error = signal<PresentableError | null>(null);

  protected readonly form = this.fb.nonNullable.group({
    nationalId: [''],
    dateOfBirth: [''],
    gender: ['Unspecified', Validators.required],
    phone: [''],
    status: ['Active', Validators.required],
  });

  protected readonly enrolments = new PageState<Enrolment>((page, pageSize) => this.api.enrolments(this.id(), page, pageSize));

  constructor() {
    queueMicrotask(() => this.load());
  }

  protected errors(control: string): string[] {
    return errorsFor(this.error(), control);
  }

  private load(): void {
    this.api.get(this.id()).subscribe({
      next: (learner) => {
        this.learner.set(learner);
        this.form.reset({
          nationalId: learner.nationalId ?? '', dateOfBirth: learner.dateOfBirth ?? '', gender: learner.gender,
          phone: learner.phone ?? '', status: learner.status,
        });
      },
      error: (failure: unknown) => this.loadError.set(toPresentableError(failure)),
    });
    this.enrolments.load(1);
    if (this.session.hasPermission('sis.report.read')) {
      this.api.transcript(this.id()).subscribe({ next: (t) => this.transcript.set(t), error: () => this.transcript.set(null) });
    }
  }

  protected save(): void {
    if (this.busy() || this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const v = this.form.getRawValue();
    this.busy.set(true);
    this.error.set(null);
    this.saved.set(false);
    this.api.update(this.id(), { nationalId: v.nationalId || null, dateOfBirth: v.dateOfBirth || null, gender: v.gender, phone: v.phone || null, status: v.status }).subscribe({
      next: (learner) => {
        this.busy.set(false);
        this.saved.set(true);
        this.learner.set(learner);
      },
      error: (failure: unknown) => {
        this.busy.set(false);
        this.error.set(toPresentableError(failure));
      },
    });
  }

  protected withdraw(enrolment: Enrolment): void {
    if (!this.confirm.confirm('enrolments.confirmWithdraw')) {
      return;
    }
    this.enrolmentsApi.withdraw(enrolment.id).subscribe({
      next: () => this.load(),
      error: (failure: unknown) => this.error.set(toPresentableError(failure)),
    });
  }
}
