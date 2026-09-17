import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ConfirmService } from '../../shared/ui';
import { BlobSaver } from './content.api';
import { ContentItem, SectionContent } from './content.models';
import { SectionContentPage } from './section-content-page';

function item(overrides: Partial<ContentItem> = {}): ContentItem {
  return {
    id: 'i1', courseContentId: 'u1', titleEn: 'Welcome', titleAr: 'مرحبًا', itemType: 'Page', body: 'Read this first.',
    sortOrder: 0, isPublished: true, resources: [],
    ...overrides,
  };
}

function content(canManage: boolean, items: ContentItem[]): SectionContent {
  return {
    section: { id: 's1', code: 'CS101-A', termName: '2026 Autumn', status: 'Open', courseId: 'c1', courseCode: 'CS101', courseNameEn: 'Programming', courseNameAr: 'برمجة', instructorUserId: 'ins' },
    canManage,
    contents: [{ id: 'u1', sectionId: 's1', titleEn: 'Week 1', titleAr: 'الأسبوع 1', sortOrder: 0, items, createdAtUtc: '2026-09-16T00:00:00Z', modifiedAtUtc: null }],
  };
}

const fileItem = item({
  id: 'i2', itemType: 'File', body: null, isPublished: false,
  resources: [{ id: 'r1', contentItemId: 'i2', fileName: 'Lecture 1.pdf', contentType: 'application/pdf', sizeBytes: 2048, contentHash: 'abc' }],
});

describe('SectionContentPage (15.3 content delivery, SEC-12 scope from the server)', () => {
  let fixture: ComponentFixture<SectionContentPage>;
  let http: HttpTestingController;
  let saved: string[];

  const element = (testId: string): HTMLElement | null => fixture.nativeElement.querySelector(`[data-testid="${testId}"]`);

  beforeEach(async () => {
    localStorage.clear();
    saved = [];
    await TestBed.configureTestingModule({
      imports: [SectionContentPage],
      providers: [
        provideRouter([]), provideHttpClient(), provideHttpClientTesting(),
        { provide: ConfirmService, useValue: { confirm: () => true } },
        { provide: BlobSaver, useValue: { save: (_blob: Blob, name: string) => saved.push(name) } },
      ],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(SectionContentPage);
    fixture.componentRef.setInput('id', 's1');
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  async function flushInitial(body: SectionContent): Promise<void> {
    await fixture.whenStable();
    http.expectOne('/api/v1/sections/s1/content').flush(body);
    fixture.detectChanges();
  }

  it('shows a learner the published items without any management control', async () => {
    await flushInitial(content(false, [item()]));

    expect(element('section-title')?.textContent).toContain('Programming');
    expect(fixture.nativeElement.textContent).toContain('Read this first.');
    expect(element('manage-badge')).toBeNull();
    expect(element('unit-form')).toBeNull();
    expect(element('publish-i1')).toBeNull();
    expect(element('remove-item-i1')).toBeNull();
  });

  it('shows a manager the controls, the draft badge and the upload input', async () => {
    await flushInitial(content(true, [item(), fileItem]));

    expect(element('manage-badge')).not.toBeNull();
    expect(element('unit-form')).not.toBeNull();
    expect(element('published-i2')?.textContent).toContain('Draft');
    expect(element('publish-i2')?.textContent).toContain('Publish');
    expect(element('upload-i2')).not.toBeNull();
    expect(element('download-r1')?.textContent).toContain('Lecture 1.pdf');
  });

  it('publishes an item through the publish route and reloads', async () => {
    await flushInitial(content(true, [fileItem]));

    (element('publish-i2') as HTMLButtonElement).click();

    const request = http.expectOne('/api/v1/content/u1/items/i2/publish');
    expect(request.request.method).toBe('POST');
    request.flush({ ...fileItem, isPublished: true });
    http.expectOne('/api/v1/sections/s1/content').flush(content(true, [{ ...fileItem, isPublished: true }]));
    fixture.detectChanges();

    expect(element('published-i2')?.textContent).toContain('Published');
    expect(element('notice')).not.toBeNull();
  });

  it('uploads the chosen file as multipart form data and shows a field error when refused (SEC-22)', async () => {
    await flushInitial(content(true, [fileItem]));
    const input = element('upload-i2') as HTMLInputElement;
    const file = new File(['MZ'], 'payload.exe', { type: 'application/octet-stream' });
    Object.defineProperty(input, 'files', { value: [file] });

    input.dispatchEvent(new Event('change'));

    const request = http.expectOne('/api/v1/content/u1/items/i2/resources');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toBeInstanceOf(FormData);
    expect(((request.request.body as FormData).get('file') as File).name).toBe('payload.exe');
    request.flush(
      { type: 'urn:opencampus:error:validation', title: 'validation', status: 400, errors: { File: ['The file type is not permitted.'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('The file type is not permitted.');
  });

  it('downloads through the authorised endpoint and hands the blob to the browser under the display name', async () => {
    await flushInitial(content(false, [{ ...fileItem, isPublished: true }]));
    (element('download-r1') as HTMLButtonElement).click();

    const request = http.expectOne('/api/v1/resources/r1/download');
    expect(request.request.responseType).toBe('blob');
    request.flush(new Blob(['%PDF'], { type: 'application/pdf' }));

    expect(saved).toEqual(['Lecture 1.pdf']);
  });

  it('tells the caller when the section is not available to them (404, API-06)', async () => {
    await fixture.whenStable();
    http.expectOne('/api/v1/sections/s1/content').flush({ status: 404, title: 'sections.not_found' }, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(element('content-not-found')).not.toBeNull();
  });
});
