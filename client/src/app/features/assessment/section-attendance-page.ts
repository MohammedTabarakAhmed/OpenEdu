import { Component, computed, inject, input, signal } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { PresentableError, toPresentableError } from '../../core/api/problem';
import { TranslatePipe } from '../../core/i18n/i18n.service';
import { BilingualPipe, LocaleDatePipe, LocaleNumberPipe, SubmitError } from '../../shared/ui';
import { AssessmentApi } from './assessment.api';
import { ATTENDANCE_STATUSES, AttendanceStatus, SectionAttendanceResponse, SessionRegisterResponse } from './assessment.models';

/**
 * Section attendance (15.3): the manager records each session's register (BR-13 decided by the aggregate); a
 * learner sees only their own records, scoped by the server (SEC-12).
 */
@Component({
  imports: [RouterLink, RouterLinkActive, TranslatePipe, BilingualPipe, LocaleDatePipe, SubmitError, LocaleNumberPipe],
  templateUrl: './section-attendance-page.html',
})
export class SectionAttendancePage {
  readonly id = input.required<string>();

  private readonly api = inject(AssessmentApi);

  protected readonly data = signal<SectionAttendanceResponse | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadError = signal<PresentableError | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<PresentableError | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly selectedSessionId = signal<string | null>(null);
  protected readonly register = signal<SessionRegisterResponse | null>(null);
  protected readonly draft = signal<Record<string, AttendanceStatus>>({});

  protected readonly statuses = ATTENDANCE_STATUSES;
  protected readonly canManage = computed(() => this.data()?.canManage === true);

  constructor() {
    queueMicrotask(() => this.load());
  }

  protected load(): void {
    this.loading.set(true);
    this.loadError.set(null);
    this.api.sectionAttendance(this.id()).subscribe({
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

  protected percentFor(learnerUserId: string): number | null {
    return this.data()?.learners.find((l) => l.learnerUserId === learnerUserId)?.attendancePercent ?? null;
  }

  protected statusFor(learnerUserId: string): AttendanceStatus | null {
    return this.data()?.records.find((r) => r.learnerUserId === learnerUserId)?.status ?? null;
  }

  protected openSession(sessionId: string): void {
    this.error.set(null);
    if (this.selectedSessionId() === sessionId) {
      this.selectedSessionId.set(null);
      this.register.set(null);
      return;
    }
    this.selectedSessionId.set(sessionId);
    this.api.sessionRegister(this.id(), sessionId).subscribe({
      next: (r) => {
        this.register.set(r);
        const initial: Record<string, AttendanceStatus> = {};
        for (const record of r.records) {
          initial[record.learnerUserId] = record.status;
        }
        this.draft.set(initial);
      },
      error: (failure: unknown) => this.error.set(toPresentableError(failure)),
    });
  }

  protected setStatus(learnerUserId: string, status: AttendanceStatus): void {
    this.draft.update((d) => ({ ...d, [learnerUserId]: status }));
  }

  protected save(): void {
    const sessionId = this.selectedSessionId();
    const reg = this.register();
    if (!sessionId || !reg || this.busy()) {
      return;
    }
    const entries = reg.learners
      .filter((l) => this.draft()[l.userId])
      .map((l) => ({ learnerUserId: l.userId, status: this.draft()[l.userId] }));
    if (entries.length === 0) {
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.api.recordAttendance(this.id(), sessionId, entries).subscribe({
      next: (r) => {
        this.busy.set(false);
        this.register.set(r);
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
