import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { PagedResponse } from '../../core/api/api.models';
import { CatalogueEntry } from '../admin/admin.models';
import { CataloguePage } from './catalogue-page';

function entry(overrides: Partial<CatalogueEntry> = {}): CatalogueEntry {
  return {
    sectionId: 's1', sectionCode: 'CS101-A', termName: '2026 Autumn', startDate: '2026-09-07', endDate: '2026-12-18',
    deliveryMode: 'InPerson', capacity: 30, placesRemaining: 12,
    courseId: 'c1', courseCode: 'CS101', courseNameEn: 'Introduction to Programming', courseNameAr: 'مقدمة في البرمجة',
    descriptionEn: 'Fundamentals', descriptionAr: 'الأساسيات', credits: 4,
    programmeId: 'p1', programmeNameEn: 'Computer Science', programmeNameAr: 'علوم الحاسوب',
    instructorNameEn: 'Dr. Haddad', instructorNameAr: 'د. حداد', isEnrolled: false,
    ...overrides,
  };
}

function paged(items: CatalogueEntry[]): PagedResponse<CatalogueEntry> {
  return { items, page: 1, pageSize: 12, totalCount: items.length, totalPages: 1 };
}

describe('CataloguePage (15.3 learner self-service)', () => {
  let fixture: ComponentFixture<CataloguePage>;
  let http: HttpTestingController;

  const element = (testId: string): HTMLElement | null => fixture.nativeElement.querySelector(`[data-testid="${testId}"]`);

  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [CataloguePage],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(CataloguePage);
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  function flushInitial(entries: CatalogueEntry[]): void {
    http.expectOne((r) => r.url === '/api/v1/catalogue').flush(paged(entries));
    http.expectOne((r) => r.url === '/api/v1/programmes').flush({ items: [], page: 1, pageSize: 100, totalCount: 0, totalPages: 0 });
    fixture.detectChanges();
  }

  it('lists open sections with places remaining and an enrol control', () => {
    flushInitial([entry(), entry({ sectionId: 's2', courseCode: 'CS305', placesRemaining: 0 })]);

    expect(fixture.nativeElement.textContent).toContain('Introduction to Programming');
    expect(element('places')?.textContent).toContain('12 / 30');
    expect((element('enrol-CS101') as HTMLButtonElement).disabled).toBe(false);
    // A full section offers no enrolment (BR-01 would refuse it anyway).
    expect((element('enrol-CS305') as HTMLButtonElement).disabled).toBe(true);
    expect(element('enrol-CS305')?.textContent).toContain('Full');
  });

  it('enrols through api/v1/me/enrolments and reloads the catalogue', () => {
    flushInitial([entry()]);

    (element('enrol-CS101') as HTMLButtonElement).click();

    const request = http.expectOne('/api/v1/me/enrolments');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ sectionId: 's1' });
    request.flush({ id: 'e1' });
    http.expectOne((r) => r.url === '/api/v1/catalogue').flush(paged([entry({ isEnrolled: true, placesRemaining: 11 })]));
    fixture.detectChanges();

    expect(element('enrol-success')).not.toBeNull();
    expect(element('enrolled-badge')).not.toBeNull();
    expect(element('enrol-CS101')).toBeNull();
  });

  it('shows the rule reference and message when the server refuses the enrolment', () => {
    flushInitial([entry()]);

    (element('enrol-CS101') as HTMLButtonElement).click();
    http.expectOne('/api/v1/me/enrolments').flush(
      { type: 'urn:opencampus:error:BR-02', title: 'BR-02', status: 422, detail: 'Enrolment refused: the learner already holds an active enrolment in this section.' },
      { status: 422, statusText: 'Unprocessable Entity' },
    );
    fixture.detectChanges();

    expect(element('submit-error')?.textContent).toContain('BR-02');
    expect(element('submit-error')?.textContent).toContain('already holds an active enrolment');
    expect(element('enrol-success')).toBeNull();
  });

  it('filters server-side by programme', () => {
    flushInitial([entry()]);

    const select = element('catalogue-programme') as HTMLSelectElement;
    select.value = '';
    select.dispatchEvent(new Event('change'));
    http.expectOne((r) => r.url === '/api/v1/catalogue' && !r.params.has('programmeId')).flush(paged([]));

    fixture.componentInstance['onProgramme']('p1');
    const filtered = http.expectOne((r) => r.url === '/api/v1/catalogue' && r.params.get('programmeId') === 'p1');
    expect(filtered.request.params.get('page')).toBe('1');
    filtered.flush(paged([]));
    fixture.detectChanges();

    expect(element('state-empty')).not.toBeNull();
  });
});
