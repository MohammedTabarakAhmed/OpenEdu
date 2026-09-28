import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { I18nService } from '../../core/i18n/i18n.service';
import { expectAccessibleControls } from '../../shared/accessibility.spec-support';
import { passwordStrength, RegisterPage } from './register-page';

describe('RegisterPage (Increment 7 self-registration)', () => {
  let fixture: ComponentFixture<RegisterPage>;
  let http: HttpTestingController;

  const element = (testId: string): HTMLElement | null => fixture.nativeElement.querySelector(`[data-testid="${testId}"]`);
  const input = (id: string): HTMLInputElement => fixture.nativeElement.querySelector(`#${id}`);

  function type(id: string, value: string): void {
    const control = input(id);
    control.value = value;
    control.dispatchEvent(new Event('input'));
    control.dispatchEvent(new Event('blur'));
  }

  async function create(): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [RegisterPage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(RegisterPage);
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function fillValidForm(): void {
    type('register-userName', 'newbie');
    type('register-email', 'newbie@example.org');
    type('register-password', 'Correct-Horse-Battery-Staple-1');
    type('register-confirmPassword', 'Correct-Horse-Battery-Staple-1');
    type('register-fullNameEn', 'New Person');
    type('register-fullNameAr', 'شخص جديد');
    fixture.detectChanges();
  }

  beforeEach(() => {
    localStorage.clear();
    vi.useFakeTimers();
  });
  afterEach(() => {
    http.verify();
    vi.useRealTimers();
  });

  it('starts by asking what the account is for, with the four roles and approval notes on the staff ones', async () => {
    await create();

    expect(element('register-roles')).not.toBeNull();
    expect(element('register-role-Learner')).not.toBeNull();
    expect(element('register-role-Administrator')?.textContent).toContain('Requires administrator approval');
    expect(element('register-role-Learner')?.textContent).not.toContain('Requires administrator approval');
    expect(element('register-form')).toBeNull();
    expectAccessibleControls(fixture.nativeElement); // NFR-09
  });

  it('role → form → 202 → "check your inbox" with a 60 s resend countdown', async () => {
    await create();
    element('register-role-Learner')!.click();
    fixture.detectChanges();

    expect(element('register-chosen')?.textContent).toContain('Learner');
    expectAccessibleControls(fixture.nativeElement); // NFR-09, form state
    fillValidForm();
    element('register-submit')!.click();

    const request = http.expectOne('/api/v1/registration');
    expect(request.request.body).toEqual({
      accountType: 'Learner', userName: 'newbie', email: 'newbie@example.org', password: 'Correct-Horse-Battery-Staple-1', fullNameEn: 'New Person', fullNameAr: 'شخص جديد',
    });
    request.flush({ message: 'registration.check_inbox' }, { status: 202, statusText: 'Accepted' });
    fixture.detectChanges();

    expect(element('register-sent')?.textContent).toContain('newbie@example.org');
    const resend = element('register-resend') as HTMLButtonElement;
    expect(resend.disabled).toBe(true);
    expect(resend.textContent).toContain('(60)');

    vi.advanceTimersByTime(60_000);
    fixture.detectChanges();
    expect(resend.disabled).toBe(false);

    resend.click();
    http.expectOne('/api/v1/registration/resend-verification').flush({ message: 'registration.check_inbox' }, { status: 202, statusText: 'Accepted' });
    fixture.detectChanges();
    expect(element('register-resent')).not.toBeNull();
    expect((element('register-resend') as HTMLButtonElement).disabled).toBe(true);
  });

  it('tells a staff registrant that an administrator must approve', async () => {
    await create();
    element('register-role-Instructor')!.click();
    fixture.detectChanges();
    fillValidForm();
    element('register-submit')!.click();
    http.expectOne('/api/v1/registration').flush({ message: 'registration.check_inbox' }, { status: 202, statusText: 'Accepted' });
    fixture.detectChanges();

    expect(element('register-sent')?.textContent).toContain('an administrator must approve');
  });

  it('blocks submission while the passwords differ and sends nothing', async () => {
    await create();
    element('register-role-Learner')!.click();
    fixture.detectChanges();
    fillValidForm();
    type('register-confirmPassword', 'Something-Else-Entirely-9');
    fixture.detectChanges();
    element('register-submit')!.click();
    fixture.detectChanges();

    expect(element('register-mismatch')).not.toBeNull();
    http.expectNone('/api/v1/registration');
  });

  it('shows a taken user name under the field (409) and a disabled system as a banner (404)', async () => {
    await create();
    element('register-role-Registrar')!.click();
    fixture.detectChanges();
    fillValidForm();

    element('register-submit')!.click();
    http.expectOne('/api/v1/registration').flush({ title: 'users.user_name_taken', status: 409 }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();
    expect(input('register-userName').classList).toContain('is-invalid');
    expect(fixture.nativeElement.textContent).toContain('This user name is already in use.');

    element('register-submit')!.click();
    http.expectOne('/api/v1/registration').flush({ title: 'registration.disabled', status: 404 }, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();
    expect(element('register-error')?.textContent).toContain('not available');
  });

  it('renders in Arabic', async () => {
    await create();
    TestBed.inject(I18nService).use('ar');
    fixture.detectChanges();

    expect(element('register-role-Learner')?.textContent).toContain('متعلّم');
  });

  it('rates password strength by length and variety', () => {
    expect(passwordStrength('short')).toBe('weak');
    expect(passwordStrength('twelvechars!')).toBe('fair');
    expect(passwordStrength('Correct-Horse-Battery-Staple-1')).toBe('strong');
    expect(passwordStrength('aaaaaaaaaaaaaaaaaaaa')).toBe('fair');
  });
});
