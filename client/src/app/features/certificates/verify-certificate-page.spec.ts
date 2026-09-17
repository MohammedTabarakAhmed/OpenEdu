import { registerLocaleData } from '@angular/common';
import { provideHttpClient } from '@angular/common/http';
import localeAr from '@angular/common/locales/ar';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { I18nService } from '../../core/i18n/i18n.service';
import { CertificateVerification } from './certificates.api';
import { VerifyCertificatePage } from './verify-certificate-page';
import { expectAccessibleControls } from '../../shared/accessibility.spec-support';

function verification(): CertificateVerification {
  return {
    verificationCode: 'ABCDE-FGHJK-MNPQR-STUVW', issuedAtUtc: '2026-09-17T00:00:00Z',
    learnerFullNameEn: 'Amina Khalil', learnerFullNameAr: 'أمينة خليل',
    courseCode: 'CS101', courseNameEn: 'Programming', courseNameAr: 'برمجة',
    programmeNameEn: 'Computer Science', programmeNameAr: 'علوم الحاسب', term: '2026 Autumn', completedAtUtc: '2026-09-16T00:00:00Z',
  };
}

describe('VerifyCertificatePage (15.4 anonymous verification)', () => {
  let fixture: ComponentFixture<VerifyCertificatePage>;
  let http: HttpTestingController;

  const element = (testId: string): HTMLElement | null => fixture.nativeElement.querySelector(`[data-testid="${testId}"]`);

  async function create(code?: string): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [VerifyCertificatePage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([{ path: 'verify/:code', component: VerifyCertificatePage }])],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(VerifyCertificatePage);
    if (code !== undefined) {
      fixture.componentRef.setInput('code', code);
    }
    fixture.detectChanges();
    await fixture.whenStable();
  }

  // UI-09: the page formats dates by locale; the Arabic locale data is registered by app.config in the real app.
  beforeAll(() => registerLocaleData(localeAr));
  beforeEach(() => localStorage.clear());
  afterEach(() => http.verify());

  it('renders the form and issues no request until a code is entered', async () => {
    await create();

    expect(element('verify-form')).not.toBeNull();
    http.expectNone((r) => r.url.startsWith('/api/v1/certificates/verify/'));
    expectAccessibleControls(fixture.nativeElement); // NFR-09
  });

  it('looks up a code taken from the route and shows what it certifies, without any identifiers', async () => {
    await create('abcde-fghjk-mnpqr-stuvw');

    http.expectOne('/api/v1/certificates/verify/abcde-fghjk-mnpqr-stuvw').flush(verification());
    fixture.detectChanges();

    expect(element('verify-valid')).not.toBeNull();
    expect(element('verify-learner')?.textContent).toContain('Amina Khalil');
    expect(element('verify-result')?.textContent).toContain('CS101');
    expect(element('verify-result')?.textContent).toContain('ABCDE-FGHJK-MNPQR-STUVW');
  });

  it('shows the Arabic name when the language is Arabic', async () => {
    await create('ABCDE-FGHJK-MNPQR-STUVW');
    TestBed.inject(I18nService).use('ar');

    http.expectOne('/api/v1/certificates/verify/ABCDE-FGHJK-MNPQR-STUVW').flush(verification());
    fixture.detectChanges();

    expect(element('verify-learner')?.textContent).toContain('أمينة خليل');
  });

  it('submits a typed code, moves it into the address and reports an unknown code distinctly from a failure', async () => {
    await create();
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    const input = element('verify-code') as HTMLInputElement;
    input.value = '  zzzzz-zzzzz-zzzzz-zzzzz ';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    (element('verify-form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    fixture.detectChanges();

    expect(navigate).toHaveBeenCalledWith(['/verify', 'zzzzz-zzzzz-zzzzz-zzzzz'], { replaceUrl: true });
    http.expectOne('/api/v1/certificates/verify/zzzzz-zzzzz-zzzzz-zzzzz').flush({ status: 404 }, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(element('verify-not-found')).not.toBeNull();
    expect(element('state-error')).toBeNull();
    expect(element('verify-valid')).toBeNull();
  });
});
