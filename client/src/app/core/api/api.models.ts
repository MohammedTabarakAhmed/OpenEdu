/** API-03: the envelope returned by every collection endpoint. */
export interface PagedResponse<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

/** API-04: the single problem format; `errors` is present on validation failures (field-keyed). */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  errors?: Record<string, string[]>;
}

/** Query parameters accepted by paged list endpoints; undefined values are omitted from the request. */
export type QueryParams = Record<string, string | number | boolean | null | undefined>;

export function toHttpParams(params: QueryParams): Record<string, string> {
  const result: Record<string, string> = {};
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== '') {
      result[key] = String(value);
    }
  }
  return result;
}
