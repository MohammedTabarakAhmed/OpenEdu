import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { PresentableError, errorsFor, toPresentableError } from '../../../core/api/problem';
import { SessionService } from '../../../core/auth/session.service';
import { TranslatePipe } from '../../../core/i18n/i18n.service';
import { PageState } from '../../../shared/page-state';
import { ConfirmService, FieldErrors, PageControls, SearchBox, SubmitError } from '../../../shared/ui';
import { ProgrammesApi } from '../admin.api';
import { Programme } from '../admin.models';

/** Programme listing, creation, amendment and deletion (15.3), paged server-side (17.5). */
@Component({
  imports: [ReactiveFormsModule, TranslatePipe, PageControls, FieldErrors, SubmitError, SearchBox],
  templateUrl: './programmes-page.html',
})
export class ProgrammesPage {
  private readonly api = inject(ProgrammesApi);
  private readonly fb = inject(FormBuilder);
  private readonly confirm = inject(ConfirmService);
  protected readonly session = inject(SessionService);

  protected search = '';
  protected readonly state = new PageState<Programme>((page, pageSize) =>
    this.api.list({ page, pageSize, search: this.search, sort: 'code' }),
  );

  protected readonly editing = signal<Programme | null>(null);
  protected readonly showForm = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<PresentableError | null>(null);

  protected readonly form = this.fb.nonNullable.group({
    code: ['', [Validators.required, Validators.maxLength(20), Validators.pattern(/^[A-Za-z0-9-]+$/)]],
    nameEn: ['', [Validators.required, Validators.maxLength(200)]],
    nameAr: ['', [Validators.required, Validators.maxLength(200)]],
    durationMonths: [12, [Validators.required, Validators.min(1), Validators.max(120)]],
    isActive: [true],
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

  protected startCreate(): void {
    this.editing.set(null);
    this.error.set(null);
    this.form.reset({ code: '', nameEn: '', nameAr: '', durationMonths: 12, isActive: true });
    this.form.controls.code.enable();
    this.showForm.set(true);
  }

  protected startEdit(programme: Programme): void {
    this.editing.set(programme);
    this.error.set(null);
    this.form.reset({ code: programme.code, nameEn: programme.nameEn, nameAr: programme.nameAr, durationMonths: programme.durationMonths, isActive: programme.isActive });
    this.form.controls.code.disable();
    this.showForm.set(true);
  }

  protected cancel(): void {
    this.showForm.set(false);
    this.editing.set(null);
  }

  /** 17.5: submission is disabled while in flight. */
  protected submit(): void {
    if (this.busy() || this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();
    const editing = this.editing();
    const request = editing
      ? this.api.update(editing.id, { nameEn: value.nameEn, nameAr: value.nameAr, durationMonths: value.durationMonths, isActive: value.isActive })
      : this.api.create({ code: value.code, nameEn: value.nameEn, nameAr: value.nameAr, durationMonths: value.durationMonths });

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

  protected remove(programme: Programme): void {
    if (!this.confirm.confirm('programmes.confirmDelete')) {
      return;
    }

    this.api.delete(programme.id).subscribe({
      next: () => this.state.reloadAfterRemoval(),
      error: (failure: unknown) => this.error.set(toPresentableError(failure)),
    });
  }
}
