import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { SessionService } from '../../../core/auth/session.service';
import { ConfirmService } from '../../../shared/ui';
import { certificate } from '../../certificates/my-certificates-page.spec';
import { BlobSaver } from '../../content/content.api';
import { Enrolment, Learner } from '../admin.models';
import { LearnerDetailPage } from './learner-detail-page';
import { expectAccessibleControls } from '../../../shared/accessibility.spec-support';

function learner(): Learner {
  return {
    id: 'l1', user: { userId: 'u1', userName: 'amina', email: 'amina@example.test', fullNameEn: 'Amina Khalil', fullNameAr: 'أمينة خليل', isActive: true },
    learnerNumber: 'L-0001', nationalId: null, dateOfBirth: null, gender: 'Female', phone: null, status: 'Active', createdAtUtc: '2026-09-01T00:00:00Z', modifiedAtUtc: null,
  };
}

function enrolment(id: string, status: Enrolment['status'], finalGrade: number | null): Enrolment {
  return {
    id,
    learner: { learnerId: 'l1', learnerNumber: 'L-0001', userId: 'u1', userName: 'amina', fullNameEn: 'Amina Khalil', fullNameAr: 'أمينة خليل' },
    section: {
      sectionId: 's1', sectionCode: 'CS101-A', termName: '2026 Autumn', startDate: '2026-09-01', endDate: '2026-12-15',
      courseId: 'c1', courseCode: 'CS101', courseNameEn: 'Programming', courseNameAr: 'برمجة', credits: 3, instructorNameEn: null, instructorNameAr: null,
    },
    enrolledAtUtc: '2026-09-01T00:00:00Z', status, finalGrade, completedAtUtc: status === 'Completed' ? '2026-09-16T00:00:00Z' : null,
  };
}

describe('LearnerDetailPage certificate actions (Increment 6)', () => {
  let fixture: ComponentFixture<LearnerDetailPage>;
  let http: HttpTestingController;
  let saver: { save: ReturnType<typeof vi.fn> };
  let permissions: string[];

  const element = (testId: string): HTMLElement | null => fixture.nativeElement.querySelector(`[data-testid="${testId}"]`);

  async function create(granted: string[]): Promise<void> {
    permissions = granted;
    saver = { save: vi.fn() };
    await TestBed.configureTestingModule({
      imports: [LearnerDetailPage],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        { provide: BlobSaver, useValue: saver },
        { provide: ConfirmService, useValue: { confirm: () => true } },
        { provide: SessionService, useValue: { hasPermission: (code: string) => permissions.includes(code), principal: () => null } },
      ],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(LearnerDetailPage);
    fixture.componentRef.setInput('id', 'l1');
    fixture.detectChanges();
    await fixture.whenStable();

    http.expectOne('/api/v1/learners/l1').flush(learner());
    http.expectOne((r) => r.url === '/api/v1/learners/l1/enrolments').flush({
      items: [enrolment('e-active', 'Active', null), enrolment('e-done', 'Completed', 88), enrolment('e-issued', 'Completed', 91)],
      page: 1, pageSize: 20, totalCount: 3,
    });
    http.expectOne('/api/v1/learners/l1/certificates').flush([certificate({ id: 'cert-issued', enrolmentId: 'e-issued' })]);
    fixture.detectChanges();
  }

  beforeEach(() => localStorage.clear());
  afterEach(() => http.verify());

  it('offers issuance only for completed enrolments without a certificate, and download where one exists', async () => {
    await create(['sis.learner.read', 'sis.certificate.issue']);

    expect(element('issue-certificate-e-active')).toBeNull();
    expect(element('issue-certificate-e-done')).not.toBeNull();
    expect(element('issue-certificate-e-issued')).toBeNull();
    expect(element('download-certificate-e-issued')).not.toBeNull();
    expectAccessibleControls(fixture.nativeElement); // NFR-09
  });

  it('hides issuance from a caller without sis.certificate.issue but still allows download', async () => {
    await create(['sis.learner.read']);

    expect(element('issue-certificate-e-done')).toBeNull();
    expect(element('download-certificate-e-issued')).not.toBeNull();
  });

  it('issues a certificate and replaces the action with download', async () => {
    await create(['sis.learner.read', 'sis.certificate.issue']);

    (element('issue-certificate-e-done') as HTMLButtonElement).click();
    const request = http.expectOne('/api/v1/enrolments/e-done/certificate');
    expect(request.request.method).toBe('POST');
    request.flush(certificate({ id: 'cert-new', enrolmentId: 'e-done' }), { status: 201, statusText: 'Created' });
    fixture.detectChanges();

    expect(element('issue-certificate-e-done')).toBeNull();
    expect(element('download-certificate-e-done')).not.toBeNull();
  });

  it('shows a BR-10 refusal with its rule reference', async () => {
    await create(['sis.learner.read', 'sis.certificate.issue']);

    (element('issue-certificate-e-done') as HTMLButtonElement).click();
    http.expectOne('/api/v1/enrolments/e-done/certificate').flush(
      { type: 'urn:opencampus:rule:BR-10', title: 'BR-10', status: 422, detail: 'A certificate can only be issued for a completed enrolment with a final grade at or above the pass threshold of 50 percent.' },
      { status: 422, statusText: 'Unprocessable Entity' },
    );
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('BR-10');
    expect(element('issue-certificate-e-done')).not.toBeNull();
  });

  it('downloads an issued certificate under a code-derived file name', async () => {
    await create(['sis.learner.read']);

    (element('download-certificate-e-issued') as HTMLButtonElement).click();
    http.expectOne('/api/v1/certificates/cert-issued/file').flush(new Blob(['%PDF-'], { type: 'application/pdf' }));
    fixture.detectChanges();

    expect(saver.save).toHaveBeenCalledWith(expect.any(Blob), 'certificate-ABCDE-FGHJK-MNPQR-STUVW.pdf');
  });
});
