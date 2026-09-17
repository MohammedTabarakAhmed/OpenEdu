import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { BlobSaver } from '../content/content.api';
import { Certificate } from './certificates.api';
import { MyCertificatesPage } from './my-certificates-page';
import { expectAccessibleControls } from '../../shared/accessibility.spec-support';

export function certificate(overrides: Partial<Certificate> = {}): Certificate {
  return {
    id: 'cert1', enrolmentId: 'e1', learnerId: 'l1', learnerNumber: 'L-0001', learnerFullNameEn: 'Amina Khalil', learnerFullNameAr: 'أمينة خليل',
    sectionId: 's1', sectionCode: 'CS101-A', term: '2026 Autumn', courseCode: 'CS101', courseNameEn: 'Programming', courseNameAr: 'برمجة',
    programmeCode: 'BSC-CS', programmeNameEn: 'Computer Science', programmeNameAr: 'علوم الحاسب',
    finalGrade: 88, completedAtUtc: '2026-09-16T00:00:00Z', verificationCode: 'ABCDE-FGHJK-MNPQR-STUVW', issuedAtUtc: '2026-09-17T00:00:00Z',
    ...overrides,
  };
}

describe('MyCertificatesPage (15.3 certificate listing/download)', () => {
  let fixture: ComponentFixture<MyCertificatesPage>;
  let http: HttpTestingController;
  let saver: { save: ReturnType<typeof vi.fn> };

  const element = (testId: string): HTMLElement | null => fixture.nativeElement.querySelector(`[data-testid="${testId}"]`);

  beforeEach(async () => {
    localStorage.clear();
    saver = { save: vi.fn() };
    await TestBed.configureTestingModule({
      imports: [MyCertificatesPage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), { provide: BlobSaver, useValue: saver }],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(MyCertificatesPage);
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  it('shows an explanatory empty state when nothing has been issued', () => {
    http.expectOne('/api/v1/me/certificates').flush([]);
    fixture.detectChanges();

    expect(element('state-empty')).not.toBeNull();
    expect(element('certificates-table')).toBeNull();
    expectAccessibleControls(fixture.nativeElement); // NFR-09
  });

  it('lists the certificates with their verification code and downloads the PDF under a code-derived name', () => {
    http.expectOne('/api/v1/me/certificates').flush([certificate()]);
    fixture.detectChanges();

    expect(element('certificate-cert1')?.textContent).toContain('ABCDE-FGHJK-MNPQR-STUVW');
    expect(element('certificate-cert1')?.textContent).toContain('CS101');

    (element('download-cert1') as HTMLButtonElement).click();
    const request = http.expectOne('/api/v1/certificates/cert1/file');
    expect(request.request.responseType).toBe('blob');
    request.flush(new Blob(['%PDF-'], { type: 'application/pdf' }));
    fixture.detectChanges();

    expect(saver.save).toHaveBeenCalledWith(expect.any(Blob), 'certificate-ABCDE-FGHJK-MNPQR-STUVW.pdf');
  });

  it('shows a load error when the listing fails', () => {
    http.expectOne('/api/v1/me/certificates').flush({ status: 500 }, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(element('state-error')).not.toBeNull();
  });
});
