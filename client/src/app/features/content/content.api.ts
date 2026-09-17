import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { ContentItem, ContentItemType, CourseContent, ResourceItem, SectionContent, SectionSummary } from './content.models';

/** Typed client over the content delivery routes (15.3). Downloads go through the authorised endpoint only (SEC-25). */
@Injectable({ providedIn: 'root' })
export class ContentApi {
  private readonly http = inject(HttpClient);

  teaching(): Observable<SectionSummary[]> {
    return this.http.get<SectionSummary[]>('/api/v1/me/sections/teaching');
  }

  enrolled(): Observable<SectionSummary[]> {
    return this.http.get<SectionSummary[]>('/api/v1/me/sections/enrolled');
  }

  sectionContent(sectionId: string): Observable<SectionContent> {
    return this.http.get<SectionContent>(`/api/v1/sections/${sectionId}/content`);
  }

  createUnit(sectionId: string, body: { titleEn: string; titleAr: string; sortOrder: number | null }): Observable<CourseContent> {
    return this.http.post<CourseContent>(`/api/v1/sections/${sectionId}/content`, body);
  }

  updateUnit(id: string, body: { titleEn: string; titleAr: string; sortOrder: number }): Observable<CourseContent> {
    return this.http.put<CourseContent>(`/api/v1/content/${id}`, body);
  }

  deleteUnit(id: string): Observable<void> {
    return this.http.delete<void>(`/api/v1/content/${id}`);
  }

  addItem(contentId: string, body: { titleEn: string; titleAr: string; itemType: ContentItemType; body: string | null; sortOrder: number | null }): Observable<ContentItem> {
    return this.http.post<ContentItem>(`/api/v1/content/${contentId}/items`, body);
  }

  removeItem(contentId: string, itemId: string): Observable<void> {
    return this.http.delete<void>(`/api/v1/content/${contentId}/items/${itemId}`);
  }

  publish(contentId: string, itemId: string): Observable<ContentItem> {
    return this.http.post<ContentItem>(`/api/v1/content/${contentId}/items/${itemId}/publish`, null);
  }

  unpublish(contentId: string, itemId: string): Observable<ContentItem> {
    return this.http.post<ContentItem>(`/api/v1/content/${contentId}/items/${itemId}/unpublish`, null);
  }

  upload(contentId: string, itemId: string, file: File): Observable<ResourceItem> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.http.post<ResourceItem>(`/api/v1/content/${contentId}/items/${itemId}/resources`, form);
  }

  removeResource(contentId: string, itemId: string, resourceId: string): Observable<void> {
    return this.http.delete<void>(`/api/v1/content/${contentId}/items/${itemId}/resources/${resourceId}`);
  }

  /** The bearer token travels with the request through the interceptor (UI-03); the browser never sees a file path (SEC-24). */
  download(resourceId: string): Observable<Blob> {
    return this.http.get(`/api/v1/resources/${resourceId}/download`, { responseType: 'blob' });
  }
}

/** Hands a downloaded blob to the browser under its display name (SEC-23). A service so pages stay testable. */
@Injectable({ providedIn: 'root' })
export class BlobSaver {
  save(blob: Blob, fileName: string): void {
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = fileName;
    anchor.rel = 'noopener';
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    URL.revokeObjectURL(url);
  }
}
