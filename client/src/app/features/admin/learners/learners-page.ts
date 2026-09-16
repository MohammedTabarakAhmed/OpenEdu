import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { PresentableError, errorsFor, toPresentableError } from '../../../core/api/problem';
import { SessionService } from '../../../core/auth/session.service';
import { TranslatePipe } from '../../../core/i18n/i18n.service';
import { PageState } from '../../../shared/page-state';
import { BilingualPipe, FieldErrors, PageControls, SearchBox, SubmitError } from '../../../shared/ui';
import { LearnersApi } from '../admin.api';
import { GENDERS, LEARNER_STATUSES, Learner, LearnerUserSummary } from '../admin.models';

/** Learner listing (search by number or name, status filter) and creation for an existing Learner-role account (15.3). */
@Component({
  imports: [ReactiveFormsModule, RouterLink, TranslatePipe, BilingualPipe, PageControls, FieldErrors, SubmitError, SearchBox],
  templateUrl: './learners-page.html',
})
export class LearnersPage {
  private readonly api = inject(LearnersApi);
  private readonly fb = inject(FormBuilder);
  private readonly router = inject(Router);
  protected readonly session = inject(SessionService);

  protected readonly statuses = LEARNER_STATUSES;
  protected readonly genders = GENDERS;

  protected search = '';
  protected status = '';
  protected readonly state = new PageState<Learner>((page, pageSize) =>
    this.api.list({ page, pageSize, search: this.search, status: this.status, sort: 'learnerNumber' }),
  );

  protected readonly candidates = signal<LearnerUserSummary[]>([]);
  protected readonly showForm = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<PresentableError | null>(null);

  protected readonly form = this.fb.nonNullable.group({
    userId: ['', Validators.required],
    learnerNumber: ['', [Validators.required, Validators.maxLength(20), Validators.pattern(/^[A-Za-z0-9-]+$/)]],
    nationalId: [''],
    dateOfBirth: [''],
    gender: ['Unspecified', Validators.required],
    phone: [''],
  });

  constructor() {
    this.state.load();
  }

  protected errors(control: string): string[] {
    return errorsFor(this.error(), control);
  }

  protected onSearch(term: string): void {
    this.search = term;
    this.state.load(1);
  }

  protected onStatusFilter(value: string): void {
    this.status = value;
    this.state.load(1);
  }

  protected startCreate(): void {
    this.error.set(null);
    this.form.reset({ userId: '', learnerNumber: '', nationalId: '', dateOfBirth: '', gender: 'Unspecified', phone: '' });
    this.api.unlinkedUsers().subscribe((users) => this.candidates.set(users));
    this.showForm.set(true);
  }

  protected cancel(): void {
    this.showForm.set(false);
  }

  protected submit(): void {
    if (this.busy() || this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const v = this.form.getRawValue();
    this.busy.set(true);
    this.error.set(null);
    this.api
      .create({ userId: v.userId, learnerNumber: v.learnerNumber, nationalId: v.nationalId || null, dateOfBirth: v.dateOfBirth || null, gender: v.gender, phone: v.phone || null })
      .subscribe({
        next: (learner) => {
          this.busy.set(false);
          void this.router.navigate(['/admin/learners', learner.id]);
        },
        error: (failure: unknown) => {
          this.busy.set(false);
          this.error.set(toPresentableError(failure));
        },
      });
  }
}
