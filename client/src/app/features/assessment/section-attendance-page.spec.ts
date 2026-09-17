import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { SectionSummary } from '../content/content.models';
import { EnrolledLearner, SectionAttendanceResponse, SessionRegisterResponse, SessionSummary } from './assessment.models';
import { SectionAttendancePage } from './section-attendance-page';
import { expectAccessibleControls } from '../../shared/accessibility.spec-support';

const section: SectionSummary = {
  id: 's1', code: 'CS101-A', termName: '2026 Autumn', status: 'Open', courseId: 'c1',
  courseCode: 'CS101', courseNameEn: 'Programming', courseNameAr: 'برمجة', instructorUserId: 'ins',
};

const session: SessionSummary = { id: 'sess1', sectionId: 's1', scheduledStartUtc: '2026-09-15T09:00:00Z', scheduledEndUtc: '2026-09-15T10:00:00Z', location: 'Room 1' };
const learner: EnrolledLearner = { userId: 'learner01', learnerNumber: 'L001', fullNameEn: 'Learner One', fullNameAr: 'متعلم واحد' };

function response(canManage: boolean): SectionAttendanceResponse {
  return {
    section, canManage, sessions: [session],
    learners: [{ learnerUserId: 'learner01', sessionsHeld: 1, sessionsAttended: 1, attendancePercent: 100 }],
    records: canManage ? [] : [{ sessionId: 'sess1', learnerUserId: 'learner01', status: 'Present', recordedByUserId: 'ins', recordedAtUtc: '2026-09-15T10:00:00Z' }],
  };
}

function register(): SessionRegisterResponse {
  return { session, learners: [learner], records: [] };
}

describe('SectionAttendancePage (15.3 attendance, BR-13, SEC-12 scope from the server)', () => {
  let fixture: ComponentFixture<SectionAttendancePage>;
  let http: HttpTestingController;

  const element = (testId: string): HTMLElement | null => fixture.nativeElement.querySelector(`[data-testid="${testId}"]`);

  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [SectionAttendancePage],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(SectionAttendancePage);
    fixture.componentRef.setInput('id', 's1');
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  async function flushInitial(body: SectionAttendanceResponse): Promise<void> {
    await fixture.whenStable();
    http.expectOne('/api/v1/sections/s1/attendance').flush(body);
    fixture.detectChanges();
  }

  it('shows a learner their own recorded status and rate, without the manager summary table', async () => {
    await flushInitial(response(false));

    expect(element('learner-summary-table')).toBeNull();
    expect(element('my-attendance')?.textContent).toContain('100');
    expectAccessibleControls(fixture.nativeElement); // NFR-09
  });

  it('shows a manager the learner summary table and opens a session register to record status', async () => {
    await flushInitial(response(true));

    expect(element('learner-summary-table')).not.toBeNull();
    (element('session-sess1') as HTMLButtonElement).click();

    http.expectOne('/api/v1/sections/s1/sessions/sess1/attendance').flush(register());
    fixture.detectChanges();

    expect(element('register-panel')).not.toBeNull();
    expect(element('save-register')).not.toBeNull();
    expectAccessibleControls(fixture.nativeElement); // NFR-09: the per-learner status selects are named
  });

  it('records the register through the recording route (BR-13 decided by the aggregate)', async () => {
    await flushInitial(response(true));
    (element('session-sess1') as HTMLButtonElement).click();
    http.expectOne('/api/v1/sections/s1/sessions/sess1/attendance').flush(register());
    fixture.detectChanges();

    const select = element('status-learner01') as HTMLSelectElement;
    select.value = 'Present';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    (element('save-register') as HTMLButtonElement).click();

    const request = http.expectOne('/api/v1/sections/s1/sessions/sess1/attendance');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ entries: [{ learnerUserId: 'learner01', status: 'Present' }] });
    request.flush({ ...register(), records: [{ sessionId: 'sess1', learnerUserId: 'learner01', status: 'Present', recordedByUserId: 'ins', recordedAtUtc: '2026-09-15T10:00:00Z' }] });
    http.expectOne('/api/v1/sections/s1/attendance').flush(response(true));
    fixture.detectChanges();

    expect(element('notice')).not.toBeNull();
  });

  it('tells the caller when the section is not available to them (404, API-06)', async () => {
    await fixture.whenStable();
    http.expectOne('/api/v1/sections/s1/attendance').flush({ status: 404, title: 'sections.not_found' }, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(element('state-not-found')).not.toBeNull();
  });
});
