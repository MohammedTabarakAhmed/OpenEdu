import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { SectionSummary } from '../content/content.models';
import { ConfirmService } from '../../shared/ui';
import { BlobSaver } from '../content/content.api';
import { AssignmentItem, SectionAssignmentsResponse, SubmissionItem } from './assessment.models';
import { SectionAssignmentsPage } from './section-assignments-page';
import { expectAccessibleControls } from '../../shared/accessibility.spec-support';

const section: SectionSummary = {
  id: 's1', code: 'CS101-A', termName: '2026 Autumn', status: 'Open', courseId: 'c1',
  courseCode: 'CS101', courseNameEn: 'Programming', courseNameAr: 'برمجة', instructorUserId: 'ins',
};

function assignment(overrides: Partial<AssignmentItem> = {}): AssignmentItem {
  return {
    id: 'a1', sectionId: 's1', titleEn: 'Homework 1', titleAr: 'الواجب 1', instructions: 'Solve it.',
    maxScore: 100, dueAtUtc: '2026-09-20T00:00:00Z', allowLate: false, latePenaltyPercent: 0,
    isPublished: true, submissionCount: 0, markedCount: 0, mySubmission: null,
    ...overrides,
  };
}

function submission(overrides: Partial<SubmissionItem> = {}): SubmissionItem {
  return {
    id: 'sub1', assignmentId: 'a1', learnerUserId: 'learner01', learnerNumber: 'L001', learnerNameEn: 'Learner One',
    learnerNameAr: 'متعلم واحد', submittedAtUtc: '2026-09-18T00:00:00Z', isLate: false, textBody: 'Answer', hasFile: false,
    score: null, feedback: null, gradedAtUtc: null, originalityScore: null,
    ...overrides,
  };
}

function response(canManage: boolean, assignments: AssignmentItem[]): SectionAssignmentsResponse {
  return { section, canManage, assignments };
}

describe('SectionAssignmentsPage (15.3 assessment, SEC-12 scope from the server)', () => {
  let fixture: ComponentFixture<SectionAssignmentsPage>;
  let http: HttpTestingController;

  const element = (testId: string): HTMLElement | null => fixture.nativeElement.querySelector(`[data-testid="${testId}"]`);

  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [SectionAssignmentsPage],
      providers: [
        provideRouter([]), provideHttpClient(), provideHttpClientTesting(),
        { provide: ConfirmService, useValue: { confirm: () => true } },
        { provide: BlobSaver, useValue: { save: () => {} } },
      ],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(SectionAssignmentsPage);
    fixture.componentRef.setInput('id', 's1');
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  async function flushInitial(body: SectionAssignmentsResponse): Promise<void> {
    await fixture.whenStable();
    http.expectOne('/api/v1/sections/s1/assignments').flush(body);
    fixture.detectChanges();
  }

  it('shows a learner only published assignments with a submission form and no management controls (BR-15-style)', async () => {
    await flushInitial(response(false, [assignment()]));

    expect(fixture.nativeElement.textContent).toContain('Homework 1');
    expect(element('new-assignment')).toBeNull();
    expect(element('publish-a1')).toBeNull();
    expect(element('submit-form-a1')).not.toBeNull();
    expectAccessibleControls(fixture.nativeElement); // NFR-09
  });

  it('shows a manager the publication controls, submission counts and submissions table', async () => {
    await flushInitial(response(true, [assignment({ submissionCount: 1, markedCount: 0 })]));

    expect(element('new-assignment')).not.toBeNull();
    expect(element('publish-a1')?.textContent).toContain('Unpublish');
    (element('view-submissions-a1') as HTMLButtonElement).click();

    http.expectOne('/api/v1/assignments/a1/submissions').flush({
      assignment: assignment(), enrolledLearners: [], submissions: [submission()],
    });
    fixture.detectChanges();

    expect(element('submissions-table')).not.toBeNull();
    expect(fixture.nativeElement.textContent).toContain('L001');
  });

  it('marks a submission and reloads it', async () => {
    await flushInitial(response(true, [assignment()]));
    (element('view-submissions-a1') as HTMLButtonElement).click();
    http.expectOne('/api/v1/assignments/a1/submissions').flush({
      assignment: assignment(), enrolledLearners: [], submissions: [submission()],
    });
    fixture.detectChanges();

    (element('mark-sub1') as HTMLButtonElement).click();
    fixture.detectChanges();
    expectAccessibleControls(fixture.nativeElement); // NFR-09: score and feedback inputs are named
    fixture.detectChanges();
    (element('mark-submit') as HTMLButtonElement).click();

    const request = http.expectOne('/api/v1/submissions/sub1/mark');
    expect(request.request.method).toBe('POST');
    request.flush(submission({ score: 90 }));
    http.expectOne('/api/v1/assignments/a1/submissions').flush({
      assignment: assignment(), enrolledLearners: [], submissions: [submission({ score: 90 })],
    });
    fixture.detectChanges();

    expect(element('notice')).not.toBeNull();
  });

  it('submits a learner text answer as multipart form data (BR-08/09 decided by the server)', async () => {
    await flushInitial(response(false, [assignment()]));

    (element('submit-a1') as HTMLButtonElement).click();

    const request = http.expectOne('/api/v1/assignments/a1/submit');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toBeInstanceOf(FormData);
    request.flush(submission());
    http.expectOne('/api/v1/sections/s1/assignments').flush(response(false, [assignment({ mySubmission: submission() })]));
    fixture.detectChanges();

    expect(element('notice')).not.toBeNull();
  });

  it('tells the caller when the section is not available to them (404, API-06)', async () => {
    await fixture.whenStable();
    http.expectOne('/api/v1/sections/s1/assignments').flush({ status: 404, title: 'sections.not_found' }, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(element('state-not-found')).not.toBeNull();
  });
});
