import { Component, computed, inject, input, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import { PresentableError, errorsFor, toPresentableError } from '../../../core/api/problem';
import { SessionService } from '../../../core/auth/session.service';
import { TranslatePipe } from '../../../core/i18n/i18n.service';
import { PageState } from '../../../shared/page-state';
import { BilingualPipe, ConfirmService, FieldErrors, LocaleDatePipe, PageControls, SubmitError } from '../../../shared/ui';
import { EnrolmentsApi, LearnersApi, SectionsApi } from '../admin.api';
import { DELIVERY_MODES, Enrolment, InstructorSummary, Learner, SectionDetail } from '../admin.models';

type Tab = 'details' | 'sessions' | 'scheme' | 'enrolments';

/**
 * One section: amendment, state transition (Draft → Open → Closed / Cancelled), scheduled sessions,
 * grade scheme and enrolments (15.3). Rule refusals from the server (BR-01/02/03/14/16) are shown verbatim.
 */
@Component({
  imports: [ReactiveFormsModule, RouterLink, TranslatePipe, BilingualPipe, LocaleDatePipe, PageControls, FieldErrors, SubmitError],
  templateUrl: './section-detail-page.html',
})
export class SectionDetailPage {
  readonly id = input.required<string>();

  private readonly api = inject(SectionsApi);
  private readonly learnersApi = inject(LearnersApi);
  private readonly enrolmentsApi = inject(EnrolmentsApi);
  private readonly fb = inject(FormBuilder);
  private readonly router = inject(Router);
  private readonly confirm = inject(ConfirmService);
  protected readonly session = inject(SessionService);

  protected readonly deliveryModes = DELIVERY_MODES;
  protected readonly tab = signal<Tab>('details');
  protected readonly detail = signal<SectionDetail | null>(null);
  protected readonly loadError = signal<PresentableError | null>(null);
  protected readonly instructors = signal<InstructorSummary[]>([]);
  protected readonly busy = signal(false);
  protected readonly error = signal<PresentableError | null>(null);

  protected readonly canWrite = computed(() => this.session.hasPermission('sis.section.write'));
  protected readonly canTransition = computed(() => this.session.hasPermission('sis.section.open'));
  protected readonly canEnrol = computed(() => this.session.hasPermission('sis.enrolment.write'));
  protected readonly isAmendable = computed(() => {
    const status = this.detail()?.section.status;
    return status === 'Draft' || status === 'Open';
  });

  protected readonly form = this.fb.nonNullable.group({
    termName: ['', [Validators.required, Validators.maxLength(50)]],
    startDate: ['', Validators.required],
    endDate: ['', Validators.required],
    capacity: [1, [Validators.required, Validators.min(1), Validators.max(1000)]],
    instructorUserId: ['', Validators.required],
    deliveryMode: ['InPerson', Validators.required],
  });

  protected readonly sessionForm = this.fb.nonNullable.group({
    date: ['', Validators.required],
    startTime: ['09:00', Validators.required],
    endTime: ['11:00', Validators.required],
    location: [''],
  });

  protected readonly componentForm = this.fb.nonNullable.group({
    nameEn: ['', [Validators.required, Validators.maxLength(200)]],
    nameAr: ['', [Validators.required, Validators.maxLength(200)]],
    weightPercent: [0, [Validators.required, Validators.min(0.01), Validators.max(100)]],
    maxScore: [100, [Validators.required, Validators.min(0.01)]],
  });

  protected readonly enrolments = new PageState<Enrolment>((page, pageSize) => this.api.enrolments(this.id(), page, pageSize));
  protected learnerSearch = '';
  protected readonly learnerMatches = signal<Learner[]>([]);
  protected selectedLearnerId = '';

  constructor() {
    queueMicrotask(() => this.load());
  }

  protected errors(control: string): string[] {
    return errorsFor(this.error(), control);
  }

  protected select(tab: Tab): void {
    this.tab.set(tab);
    this.error.set(null);
    if (tab === 'enrolments' && this.enrolments.totalPages() === 0 && !this.enrolments.loading()) {
      this.enrolments.load(1);
    }
  }

  private load(): void {
    this.api.get(this.id()).subscribe({
      next: (detail) => this.apply(detail),
      error: (failure: unknown) => this.loadError.set(toPresentableError(failure)),
    });
    if (this.session.hasPermission('sis.section.write')) {
      this.api.instructors().subscribe((i) => this.instructors.set(i));
    }
  }

  private apply(detail: SectionDetail): void {
    this.detail.set(detail);
    const s = detail.section;
    this.form.reset({
      termName: s.termName, startDate: s.startDate, endDate: s.endDate, capacity: s.capacity,
      instructorUserId: s.instructor?.userId ?? '', deliveryMode: s.deliveryMode,
    });
  }

  /** 17.5: one in-flight submission at a time; failures are presented, never swallowed. */
  private run<T>(request: Observable<T>, onSuccess: (value: T) => void): void {
    if (this.busy()) {
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    request.subscribe({
      next: (value) => {
        this.busy.set(false);
        onSuccess(value);
      },
      error: (failure: unknown) => {
        this.busy.set(false);
        this.error.set(toPresentableError(failure));
      },
    });
  }

  // ----- Details -----

  protected save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.run(this.api.update(this.id(), this.form.getRawValue()), (detail) => this.apply(detail));
  }

  protected transition(action: 'open' | 'close' | 'cancel'): void {
    if (action !== 'open' && !this.confirm.confirm(action === 'close' ? 'sections.confirmClose' : 'sections.confirmCancel')) {
      return;
    }
    this.run(this.api.transition(this.id(), action), (detail) => this.apply(detail));
  }

  protected remove(): void {
    if (!this.confirm.confirm('sections.confirmDelete')) {
      return;
    }
    this.run(this.api.delete(this.id()), () => void this.router.navigate(['/admin/sections']));
  }

  // ----- Sessions -----

  protected addSession(): void {
    if (this.sessionForm.invalid) {
      this.sessionForm.markAllAsTouched();
      return;
    }
    const v = this.sessionForm.getRawValue();
    // The form captures local wall-clock time; the interface carries UTC (DC-04).
    const body = {
      scheduledStartUtc: new Date(`${v.date}T${v.startTime}`).toISOString(),
      scheduledEndUtc: new Date(`${v.date}T${v.endTime}`).toISOString(),
      location: v.location || null,
    };
    this.run(this.api.addSession(this.id(), body), () => {
      this.sessionForm.reset({ date: '', startTime: '09:00', endTime: '11:00', location: '' });
      this.load();
    });
  }

  protected removeSession(sessionId: string): void {
    if (!this.confirm.confirm('sections.confirmRemoveSession')) {
      return;
    }
    this.run(this.api.removeSession(this.id(), sessionId), () => this.load());
  }

  // ----- Grade scheme -----

  protected addComponent(): void {
    if (this.componentForm.invalid) {
      this.componentForm.markAllAsTouched();
      return;
    }
    this.run(this.api.addGradeComponent(this.id(), this.componentForm.getRawValue()), () => {
      this.componentForm.reset({ nameEn: '', nameAr: '', weightPercent: 0, maxScore: 100 });
      this.load();
    });
  }

  protected removeComponent(componentId: string): void {
    if (!this.confirm.confirm('sections.confirmRemoveComponent')) {
      return;
    }
    this.run(this.api.removeGradeComponent(this.id(), componentId), () => this.load());
  }

  // ----- Enrolments -----

  protected searchLearners(term: string): void {
    this.learnerSearch = term;
    if (!term.trim()) {
      this.learnerMatches.set([]);
      return;
    }
    this.learnersApi.list({ page: 1, pageSize: 10, search: term, status: 'Active' }).subscribe((page) => this.learnerMatches.set(page.items));
  }

  protected enrol(): void {
    if (!this.selectedLearnerId) {
      return;
    }
    this.run(this.enrolmentsApi.create({ learnerId: this.selectedLearnerId, sectionId: this.id() }), () => {
      this.selectedLearnerId = '';
      this.learnerMatches.set([]);
      this.enrolments.load(1);
      this.load();
    });
  }

  protected withdraw(enrolment: Enrolment): void {
    if (!this.confirm.confirm('enrolments.confirmWithdraw')) {
      return;
    }
    this.run(this.enrolmentsApi.withdraw(enrolment.id), () => {
      this.enrolments.reload();
      this.load();
    });
  }
}
