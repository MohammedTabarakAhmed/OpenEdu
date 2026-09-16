import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { PresentableError, errorsFor, toPresentableError } from '../../../core/api/problem';
import { SessionService } from '../../../core/auth/session.service';
import { TranslatePipe } from '../../../core/i18n/i18n.service';
import { PageState } from '../../../shared/page-state';
import { BilingualPipe, ConfirmService, FieldErrors, PageControls, SearchBox, SubmitError } from '../../../shared/ui';
import { CoursesApi, ProgrammesApi } from '../admin.api';
import { Course, Programme } from '../admin.models';

/** Course listing (filterable by programme), creation, amendment and deletion (15.3). */
@Component({
  imports: [ReactiveFormsModule, TranslatePipe, BilingualPipe, PageControls, FieldErrors, SubmitError, SearchBox],
  templateUrl: './courses-page.html',
})
export class CoursesPage {
  private readonly api = inject(CoursesApi);
  private readonly programmesApi = inject(ProgrammesApi);
  private readonly fb = inject(FormBuilder);
  private readonly confirm = inject(ConfirmService);
  protected readonly session = inject(SessionService);

  protected search = '';
  protected programmeId = '';
  protected readonly programmes = signal<Programme[]>([]);
  protected readonly state = new PageState<Course>((page, pageSize) =>
    this.api.list({ page, pageSize, search: this.search, programmeId: this.programmeId, sort: 'code' }),
  );

  protected readonly editing = signal<Course | null>(null);
  protected readonly showForm = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<PresentableError | null>(null);

  protected readonly form = this.fb.nonNullable.group({
    programmeId: ['', Validators.required],
    code: ['', [Validators.required, Validators.maxLength(20), Validators.pattern(/^[A-Za-z0-9-]+$/)]],
    nameEn: ['', [Validators.required, Validators.maxLength(200)]],
    nameAr: ['', [Validators.required, Validators.maxLength(200)]],
    descriptionEn: [''],
    descriptionAr: [''],
    credits: [3, [Validators.required, Validators.min(0), Validators.max(60)]],
  });

  constructor() {
    // The programme filter and selector are bounded reference lists (NFR-03: one bounded page).
    this.programmesApi.list({ page: 1, pageSize: 100, sort: 'code' }).subscribe((p) => this.programmes.set(p.items));
    this.state.load();
  }

  protected errors(control: string): string[] {
    return errorsFor(this.error(), control);
  }

  protected onSearch(term: string): void {
    this.search = term;
    this.state.load(1);
  }

  protected onProgrammeFilter(value: string): void {
    this.programmeId = value;
    this.state.load(1);
  }

  protected startCreate(): void {
    this.editing.set(null);
    this.error.set(null);
    this.form.reset({ programmeId: this.programmeId, code: '', nameEn: '', nameAr: '', descriptionEn: '', descriptionAr: '', credits: 3 });
    this.form.controls.code.enable();
    this.form.controls.programmeId.enable();
    this.showForm.set(true);
  }

  protected startEdit(course: Course): void {
    this.editing.set(course);
    this.error.set(null);
    this.form.reset({
      programmeId: course.programmeId, code: course.code, nameEn: course.nameEn, nameAr: course.nameAr,
      descriptionEn: course.descriptionEn ?? '', descriptionAr: course.descriptionAr ?? '', credits: course.credits,
    });
    this.form.controls.code.disable();
    this.form.controls.programmeId.disable();
    this.showForm.set(true);
  }

  protected cancel(): void {
    this.showForm.set(false);
    this.editing.set(null);
  }

  protected submit(): void {
    if (this.busy() || this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const v = this.form.getRawValue();
    const body = { nameEn: v.nameEn, nameAr: v.nameAr, descriptionEn: v.descriptionEn || null, descriptionAr: v.descriptionAr || null, credits: v.credits };
    const editing = this.editing();
    const request = editing ? this.api.update(editing.id, body) : this.api.create({ ...body, programmeId: v.programmeId, code: v.code });

    this.busy.set(true);
    this.error.set(null);
    request.subscribe({
      next: () => {
        this.busy.set(false);
        this.showForm.set(false);
        this.state.reload();
      },
      error: (failure: unknown) => {
        this.busy.set(false);
        this.error.set(toPresentableError(failure));
      },
    });
  }

  protected remove(course: Course): void {
    if (!this.confirm.confirm('courses.confirmDelete')) {
      return;
    }

    this.api.delete(course.id).subscribe({
      next: () => this.state.reloadAfterRemoval(),
      error: (failure: unknown) => this.error.set(toPresentableError(failure)),
    });
  }
}
