import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { I18nService } from '../../core/i18n/i18n.service';
import { expectAccessibleControls } from '../../shared/accessibility.spec-support';
import { VerifyEmailPage } from './verify-email-page';

describe('VerifyEmailPage (Increment 7 verification link)', () => {
  let fixture: ComponentFixture<VerifyEmailPage>;
  let http: HttpTestingController;

  const element = (testId: string): HTMLElement | null => fixture.nativeElement.querySelector(`[data-testid="${testId}"]`);

  async function create(token?: string): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [VerifyEmailPage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(VerifyEmailPage);
    if (token !== undefined) {
      fixture.componentRef.setInput('token', token);
    }
    fixture.detectChanges();
    await fixture.whenStable();
  }

  beforeEach(() => localStorage.clear());
  afterEach(() => http.verify());

  it('never posts the token on load; only the button does', async () => {
    await create('abc123');

    http.expectNone('/api/v1/registration/verify-email');
    expect(element('verify-email-submit')).not.toBeNull();
    expectAccessibleControls(fixture.nativeElement); // NFR-09

    element('verify-email-submit')!.click();
    const request = http.expectOne('/api/v1/registration/verify-email');
    expect(request.request.body).toEqual({ token: 'abc123' });
    request.flush({ outcome: 'Activated', requestedRole: 'Learner' });
    fixture.detectChanges();

    expect(element('verify-email-activated')).not.toBeNull();
    expect(element('verify-email-login')?.getAttribute('href')).toBe('/login');
  });

  it('tells a staff registrant the request awaits approval, naming the role', async () => {
    await create('abc123');
    element('verify-email-submit')!.click();
    http.expectOne('/api/v1/registration/verify-email').flush({ outcome: 'AwaitingApproval', requestedRole: 'Instructor' });
    fixture.detectChanges();

    expect(element('verify-email-awaiting')?.textContent).toContain('Instructor');
    expect(element('verify-email-activated')).toBeNull();
  });

  it('offers a resend form on an invalid link and answers uniformly', async () => {
    await create('expired');
    element('verify-email-submit')!.click();
    http.expectOne('/api/v1/registration/verify-email').flush({ title: 'registration.link_invalid', status: 404 }, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(element('verify-email-invalid')).not.toBeNull();
    expectAccessibleControls(fixture.nativeElement); // NFR-09, resend form state
    await fixture.whenStable(); // ngModel registers with the form in a microtask

    const email = fixture.nativeElement.querySelector('#verify-email-address') as HTMLInputElement;
    email.value = 'someone@example.org';
    email.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    (element('verify-email-resend-form') as HTMLFormElement).dispatchEvent(new Event('submit'));

    const request = http.expectOne('/api/v1/registration/resend-verification');
    expect(request.request.body).toEqual({ email: 'someone@example.org' });
    request.flush({ message: 'registration.check_inbox' }, { status: 202, statusText: 'Accepted' });
    fixture.detectChanges();

    expect(element('verify-email-resent')).not.toBeNull();
    expect(element('verify-email-resend-form')).toBeNull();
  });

  it('explains a missing token and renders in Arabic', async () => {
    await create();
    expect(element('verify-email-missing')).not.toBeNull();
    expect(element('verify-email-submit')).toBeNull();

    TestBed.inject(I18nService).use('ar');
    fixture.detectChanges();
    expect(element('verify-email-missing')?.textContent).toContain('غير مكتمل');
  });
});
