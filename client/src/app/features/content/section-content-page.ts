import { Component, computed, inject, input, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { Observable } from 'rxjs';
import { PresentableError, errorsFor, toPresentableError } from '../../core/api/problem';
import { I18nService, TranslatePipe } from '../../core/i18n/i18n.service';
import { BilingualPipe, ConfirmService, FieldErrors, formatLocaleNumber, SubmitError } from '../../shared/ui';
import { BlobSaver, ContentApi } from './content.api';
import { CONTENT_ITEM_TYPES, ContentItem, CourseContent, SectionContent } from './content.models';

/**
 * A section's content hierarchy (15.3 "Content delivery") for both shells. The server decides the caller's
 * scope (SEC-12) and returns `canManage`; this page only hides controls the caller cannot use (17.3) and shows
 * refusals verbatim. Learners see published items only (BR-15) because the server never sends the others.
 */
@Component({
  imports: [ReactiveFormsModule, RouterLink, RouterLinkActive, TranslatePipe, BilingualPipe, FieldErrors, SubmitError],
  templateUrl: './section-content-page.html',
})
export class SectionContentPage {
  readonly id = input.required<string>();

  private readonly api = inject(ContentApi);
  private readonly fb = inject(FormBuilder);
  private readonly confirm = inject(ConfirmService);
  private readonly saver = inject(BlobSaver);
  private readonly i18n = inject(I18nService);

  protected readonly itemTypes = CONTENT_ITEM_TYPES;
  protected readonly content = signal<SectionContent | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadError = signal<PresentableError | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<PresentableError | null>(null);
  protected readonly notice = signal<string | null>(null);
  /** The unit whose "add item" form is open, if any. */
  protected readonly addingItemTo = signal<string | null>(null);
  protected readonly uploadingTo = signal<string | null>(null);

  protected readonly canManage = computed(() => this.content()?.canManage === true);
  protected readonly isEmpty = computed(() => (this.content()?.contents.length ?? 0) === 0);

  protected readonly unitForm = this.fb.nonNullable.group({
    titleEn: ['', [Validators.required, Validators.maxLength(200)]],
    titleAr: ['', [Validators.required, Validators.maxLength(200)]],
  });

  protected readonly itemForm = this.fb.nonNullable.group({
    titleEn: ['', [Validators.required, Validators.maxLength(200)]],
    titleAr: ['', [Validators.required, Validators.maxLength(200)]],
    itemType: ['Page' as (typeof CONTENT_ITEM_TYPES)[number], Validators.required],
    body: [''],
  });

  constructor() {
    // The route input is bound after construction; defer the first load by one tick.
    queueMicrotask(() => this.load());
  }

  protected load(): void {
    this.loading.set(true);
    this.loadError.set(null);
    this.api.sectionContent(this.id()).subscribe({
      next: (c) => {
        this.content.set(c);
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

  // ----- Units -----

  protected createUnit(): void {
    if (this.unitForm.invalid || this.busy()) {
      this.unitForm.markAllAsTouched();
      return;
    }
    const { titleEn, titleAr } = this.unitForm.getRawValue();
    this.run(this.api.createUnit(this.id(), { titleEn, titleAr, sortOrder: null }), () => this.unitForm.reset());
  }

  protected deleteUnit(unit: CourseContent): void {
    if (!this.confirm.confirm('content.confirmDeleteUnit')) {
      return;
    }
    this.run(this.api.deleteUnit(unit.id));
  }

  // ----- Items -----

  protected openAddItem(unit: CourseContent): void {
    this.itemForm.reset({ titleEn: '', titleAr: '', itemType: 'Page', body: '' });
    this.error.set(null);
    this.addingItemTo.set(unit.id);
  }

  protected addItem(unit: CourseContent): void {
    if (this.itemForm.invalid || this.busy()) {
      this.itemForm.markAllAsTouched();
      return;
    }
    const { titleEn, titleAr, itemType, body } = this.itemForm.getRawValue();
    this.run(
      this.api.addItem(unit.id, { titleEn, titleAr, itemType, body: body.trim() === '' ? null : body, sortOrder: null }),
      () => this.addingItemTo.set(null),
    );
  }

  protected removeItem(unit: CourseContent, item: ContentItem): void {
    if (!this.confirm.confirm('content.confirmDeleteItem')) {
      return;
    }
    this.run(this.api.removeItem(unit.id, item.id));
  }

  protected togglePublish(unit: CourseContent, item: ContentItem): void {
    this.run(item.isPublished ? this.api.unpublish(unit.id, item.id) : this.api.publish(unit.id, item.id));
  }

  // ----- Resources -----

  protected upload(unit: CourseContent, item: ContentItem, event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file || this.busy()) {
      return;
    }
    this.uploadingTo.set(item.id);
    this.run(this.api.upload(unit.id, item.id, file), () => {
      this.uploadingTo.set(null);
      input.value = '';
    }, () => {
      this.uploadingTo.set(null);
      input.value = '';
    });
  }

  protected removeResource(unit: CourseContent, item: ContentItem, resourceId: string): void {
    if (!this.confirm.confirm('content.confirmDeleteResource')) {
      return;
    }
    this.run(this.api.removeResource(unit.id, item.id, resourceId));
  }

  protected download(resourceId: string, fileName: string): void {
    this.error.set(null);
    this.api.download(resourceId).subscribe({
      next: (blob) => this.saver.save(blob, fileName),
      error: (failure: unknown) => this.error.set(toPresentableError(failure)),
    });
  }

  /** UI-09: the magnitude is formatted for the active locale and the unit comes from the resources (UI-06). */
  protected formatSize(bytes: number): string {
    if (bytes < 1024) {
      return `${formatLocaleNumber(this.i18n.language(), bytes)} ${this.i18n.t('common.unit.bytes')}`;
    }
    if (bytes < 1024 * 1024) {
      return `${formatLocaleNumber(this.i18n.language(), bytes / 1024, 'number', 1)} ${this.i18n.t('common.unit.kilobytes')}`;
    }
    return `${formatLocaleNumber(this.i18n.language(), bytes / (1024 * 1024), 'number', 1)} ${this.i18n.t('common.unit.megabytes')}`;
  }

  /** Runs a mutation with the in-flight guard (17.5), then reloads the hierarchy so the view reflects the server. */
  private run(request: Observable<unknown>, onSuccess?: () => void, onError?: () => void): void {
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
        onError?.();
        this.error.set(toPresentableError(failure));
      },
    });
  }
}
