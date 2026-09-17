/** TypeScript mirrors of the LMS content contracts (API-02). */
export interface SectionSummary {
  id: string;
  code: string;
  termName: string;
  status: string;
  courseId: string;
  courseCode: string;
  courseNameEn: string;
  courseNameAr: string;
  instructorUserId: string;
}

export type ContentItemType = 'Page' | 'Link' | 'File';

export const CONTENT_ITEM_TYPES: readonly ContentItemType[] = ['Page', 'Link', 'File'];

export interface ResourceItem {
  id: string;
  contentItemId: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  contentHash: string;
}

export interface ContentItem {
  id: string;
  courseContentId: string;
  titleEn: string;
  titleAr: string;
  itemType: ContentItemType;
  body: string | null;
  sortOrder: number;
  isPublished: boolean;
  resources: ResourceItem[];
}

export interface CourseContent {
  id: string;
  sectionId: string;
  titleEn: string;
  titleAr: string;
  sortOrder: number;
  items: ContentItem[];
  createdAtUtc: string;
  modifiedAtUtc: string | null;
}

/** The server decides the scope (SEC-12); `canManage` only drives which controls are shown (17.3). */
export interface SectionContent {
  section: SectionSummary;
  canManage: boolean;
  contents: CourseContent[];
}
