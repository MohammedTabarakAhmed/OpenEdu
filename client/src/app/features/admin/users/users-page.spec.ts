import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { SessionService } from '../../../core/auth/session.service';
import { expectAccessibleControls } from '../../../shared/accessibility.spec-support';
import { ConfirmService } from '../../../shared/ui';
import { User } from '../admin.models';
import { UsersPage } from './users-page';

function user(id: string, overrides: Partial<User> = {}): User {
  return {
    id, userName: `user-${id}`, email: `${id}@example.test`, fullNameEn: `User ${id}`, fullNameAr: `مستخدم ${id}`,
    isActive: true, mfaEnabled: false, lockedUntilUtc: null, roles: ['Learner'], createdAtUtc: '2026-09-01T00:00:00Z', modifiedAtUtc: null,
    registrationStatus: 'None', requestedRole: null, emailVerifiedAtUtc: null,
    ...overrides,
  };
}

const page = (items: User[]) => ({ items, page: 1, pageSize: 20, totalCount: items.length, totalPages: 1 });

describe('UsersPage (Increment 7 registration approval)', () => {
  let fixture: ComponentFixture<UsersPage>;
  let http: HttpTestingController;
  let permissions: string[];

  const element = (testId: string): HTMLElement | null => fixture.nativeElement.querySelector(`[data-testid="${testId}"]`);
  const all = (testId: string): HTMLElement[] => Array.from(fixture.nativeElement.querySelectorAll(`[data-testid="${testId}"]`));

  async function create(granted: string[]): Promise<void> {
    permissions = granted;
    await TestBed.configureTestingModule({
      imports: [UsersPage],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        { provide: ConfirmService, useValue: { confirm: () => true } },
        { provide: SessionService, useValue: { hasPermission: (code: string) => permissions.includes(code), principal: () => null } },
      ],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(UsersPage);
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function flushList(items: User[]): void {
    http.expectOne((r) => r.url === '/api/v1/users').flush(page(items));
    http.expectOne('/api/v1/roles').flush([]);
    fixture.detectChanges();
  }

  const pendingInstructor = () => user('p1', { isActive: false, roles: [], registrationStatus: 'AwaitingApproval', requestedRole: 'Instructor', emailVerifiedAtUtc: '2026-09-20T09:00:00Z' });
  const unverified = () => user('p2', { isActive: false, roles: [], registrationStatus: 'AwaitingVerification', requestedRole: 'Learner' });

  beforeEach(() => localStorage.clear());
  afterEach(() => http.verify());

  it('sends the registration filter as a query parameter', async () => {
    await create(['identity.user.read']);
    flushList([]);

    const select = element('user-registration-filter') as HTMLSelectElement;
    select.value = 'AwaitingApproval';
    select.dispatchEvent(new Event('change'));

    const request = http.expectOne((r) => r.url === '/api/v1/users');
    expect(request.request.params.get('registrationStatus')).toBe('AwaitingApproval');
    request.flush(page([pendingInstructor()]));
    fixture.detectChanges();

    expect(element('user-awaiting-approval')?.textContent).toContain('Awaiting approval');
    expect(element('user-awaiting-approval')?.textContent).toContain('Instructor');
    expectAccessibleControls(fixture.nativeElement); // NFR-09
  });

  it('shows Approve/Reject only with identity.role.assign, and never Activate for a pending row', async () => {
    await create(['identity.user.read', 'identity.user.deactivate']);
    flushList([pendingInstructor(), unverified(), user('ok')]);

    expect(all('user-approve')).toHaveLength(0);
    expect(all('user-reject')).toHaveLength(0);
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Deactivate'); // the ordinary account
    expect(text.match(/Activate\b/g) ?? []).toHaveLength(0); // no Activate offered on the two pending rows
  });

  it('approves and rejects through the registration endpoints and reloads', async () => {
    await create(['identity.user.read', 'identity.role.assign']);
    flushList([pendingInstructor(), unverified()]);

    expect(all('user-approve')).toHaveLength(1); // only the verified one can be approved
    expect(all('user-reject')).toHaveLength(2);
    expect(element('user-awaiting-verification')?.textContent).toContain('Awaiting verification');

    element('user-approve')!.click();
    http.expectOne({ method: 'POST', url: '/api/v1/users/p1/registration/approve' }).flush(user('p1', { roles: ['Instructor'], registrationStatus: 'Approved' }));
    http.expectOne((r) => r.url === '/api/v1/users').flush(page([user('p1', { roles: ['Instructor'], registrationStatus: 'Approved' }), unverified()]));
    fixture.detectChanges();
    expect(all('user-approve')).toHaveLength(0);

    element('user-reject')!.click();
    http.expectOne({ method: 'POST', url: '/api/v1/users/p2/registration/reject' }).flush(null, { status: 204, statusText: 'No Content' });
    http.expectOne((r) => r.url === '/api/v1/users').flush(page([user('p1', { roles: ['Instructor'], registrationStatus: 'Approved' })]));
    fixture.detectChanges();
    expect(all('user-reject')).toHaveLength(0);
  });
});
