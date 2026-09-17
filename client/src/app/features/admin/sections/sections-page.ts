import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { PresentableError, errorsFor, toPresentableError } from '../../../core/api/problem';
import { SessionService } from '../../../core/auth/session.service';
import { TranslatePipe } from '../../../core/i18n/i18n.service';
import { PageState } from '../../../shared/page-state';
import { BilingualPipe, FieldErrors, LocaleDatePipe, LocaleNumberPipe, PageControls, SearchBox, SubmitError } from '../../../shared/ui';
import { CoursesApi, SectionsApi } from '../admin.api';
import { Course, DELIVERY_MODES, InstructorSummary, SECTION_STATUSES, Section } from '../admin.models';

/** Section listing with status filter and creation (15.3); management continues on the detail page. */
@Component({
  imports: [ReactiveFormsModule, RouterLink, TranslatePipe, BilingualPipe, LocaleDatePipe, PageControls, FieldErrors, SubmitError, SearchBox, LocaleNumberPipe],
  templateUrl: './sections-page.html',
})
export class SectionsPage {
  private readonly api = inject(SectionsApi);
  private readonly coursesApi = inject(CoursesApi);
  private readonly fb = inject(FormBuilder);
  private readonly router = inject(Router);
  protected readonly session = inject(SessionService);

  protected readonly statuses = SECTION_STATUSES;
  protected readonly deliveryModes = DELIVERY_MODES;

  protected search = '';
  protected status = '';
  protected readonly courses = signal<Course[]>([]);
  protected readonly instructors = signal<InstructorSummary[]>([]);
  protected readonly state = new PageState<Section>((page, pageSize) =>
    this.api.list({ page, pageSize, search: this.search, status: this.status, sort: 'code' }),
  );

  protected readonly showForm = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<PresentableError | null>(null);

  protected readonly form = this.fb.nonNullable.group({
    courseId: ['', Validators.required],
    code: ['', [Validators.required, Validators.maxLength(30), Validators.pattern(/^[A-Za-z0-9-]+$/)]],
    termName: ['', [Validators.required, Validators.maxLength(50)]],
    startDate: ['', Validators.required],
    endDate: ['', Validators.required],
    capacity: [30, [Validators.required, Validators.min(1), Validators.max(1000)]],
    instructorUserId: ['', Validators.required],
    deliveryMode: ['InPerson', Validators.required],
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
    this.form.reset({ courseId: '', code: '', termName: '', startDate: '', endDate: '', capacity: 30, instructorUserId: '', deliveryMode: 'InPerson' });
    if (this.courses().length === 0) {
      this.coursesApi.list({ page: 1, pageSize: 100, sort: 'code' }).subscribe((c) => this.courses.set(c.items));
      this.api.instructors().subscribe((i) => this.instructors.set(i));
    }
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

    this.busy.set(true);
    this.error.set(null);
    this.api.create(this.form.getRawValue()).subscribe({
      next: (created) => {
        this.busy.set(false);
        void this.router.navigate(['/admin/sections', created.section.id]);
      },
      error: (failure: unknown) => {
        this.busy.set(false);
        this.error.set(toPresentableError(failure));
      },
    });
  }
}
