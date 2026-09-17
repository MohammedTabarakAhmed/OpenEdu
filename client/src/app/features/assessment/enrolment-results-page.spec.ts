import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { BlobSaver } from '../content/content.api';
import { certificate } from '../certificates/my-certificates-page.spec';
import { LearnerResultsResponse } from './assessment.models';
import { EnrolmentResultsPage } from './enrolment-results-page';

function results(overrides: Partial<LearnerResultsResponse> = {}): LearnerResultsResponse {
  return {
    enrolmentId: 'e1', sectionId: 's1', sectionCode: 'CS101-A', courseCode: 'CS101', courseNameEn: 'Programming', courseNameAr: 'برمجة',
    status: 'Active', finalGrade: null, isReleased: false,
    components: [{ id: 'c1', nameEn: 'Midterm', nameAr: 'نصفي', weightPercent: 50, maxScore: 100 }],
    entries: [],
    ...overrides,
  };
}

describe('EnrolmentResultsPage (15.3 released results, BR-06)', () => {
  let fixture: ComponentFixture<EnrolmentResultsPage>;
  let http: HttpTestingController;
  let saver: { save: ReturnType<typeof vi.fn> };

  const element = (testId: string): HTMLElement | null => fixture.nativeElement.querySelector(`[data-testid="${testId}"]`);

  beforeEach(async () => {
    localStorage.clear();
    saver = { save: vi.fn() };
    await TestBed.configureTestingModule({
      imports: [EnrolmentResultsPage],
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: BlobSaver, useValue: saver }],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(EnrolmentResultsPage);
    fixture.componentRef.setInput('id', 'e1');
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  it('withholds results until release (BR-06)', async () => {
    await fixture.whenStable();
    http.expectOne('/api/v1/me/enrolments/e1/results').flush(results());
    fixture.detectChanges();

    expect(element('not-released')).not.toBeNull();
    expect(element('results-table')).toBeNull();
  });

  it('shows the released entries and weighted final grade', async () => {
    await fixture.whenStable();
    http.expectOne('/api/v1/me/enrolments/e1/results').flush(results({
      isReleased: true, finalGrade: 87.5, status: 'Completed',
      entries: [{ id: 'g1', enrolmentId: 'e1', gradeComponentId: 'c1', score: 90, isReleased: true, gradedByUserId: 'ins', gradedAtUtc: '2026-09-17T00:00:00Z' }],
    }));
    fixture.detectChanges();

    expect(element('results-table')).not.toBeNull();
    expect(element('final-grade')?.textContent).toContain('87.5');
    expect(fixture.nativeElement.textContent).toContain('90');

    // A completed enrolment looks for its certificate among the learner's own; none yet.
    http.expectOne('/api/v1/me/certificates').flush([]);
    fixture.detectChanges();
    expect(element('download-certificate')).toBeNull();
  });

  it('offers the certificate download once one has been issued for the completed enrolment', async () => {
    await fixture.whenStable();
    http.expectOne('/api/v1/me/enrolments/e1/results').flush(results({ isReleased: true, finalGrade: 88, status: 'Completed', entries: [] }));
    http.expectOne('/api/v1/me/certificates').flush([certificate({ id: 'cert1', enrolmentId: 'e1' }), certificate({ id: 'other', enrolmentId: 'e9' })]);
    fixture.detectChanges();

    (element('download-certificate') as HTMLButtonElement).click();
    http.expectOne('/api/v1/certificates/cert1/file').flush(new Blob(['%PDF-'], { type: 'application/pdf' }));
    expect(saver.save).toHaveBeenCalledWith(expect.any(Blob), 'certificate-ABCDE-FGHJK-MNPQR-STUVW.pdf');
  });

  it('does not look for a certificate while the enrolment is not completed', async () => {
    await fixture.whenStable();
    http.expectOne('/api/v1/me/enrolments/e1/results').flush(results({ isReleased: true, finalGrade: null, status: 'Active' }));
    fixture.detectChanges();

    http.expectNone('/api/v1/me/certificates');
    expect(element('download-certificate')).toBeNull();
  });

  it('shows a load error when the results request fails', async () => {
    await fixture.whenStable();
    http.expectOne('/api/v1/me/enrolments/e1/results').flush({ status: 404 }, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(element('state-error')).not.toBeNull();
  });
});
