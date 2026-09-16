import { signal } from '@angular/core';
import { Observable } from 'rxjs';
import { PagedResponse } from '../core/api/api.models';
import { PresentableError, toPresentableError } from '../core/api/problem';

/**
 * State of a server-paged collection view (17.5): loading, empty and error states are explicit,
 * and every page is fetched from the server rather than sliced in the client (NFR-03).
 */
export class PageState<T> {
  readonly items = signal<T[]>([]);
  readonly page = signal(1);
  readonly pageSize = signal(10);
  readonly totalCount = signal(0);
  readonly totalPages = signal(0);
  readonly loading = signal(false);
  readonly error = signal<PresentableError | null>(null);

  constructor(private readonly fetch: (page: number, pageSize: number) => Observable<PagedResponse<T>>) {}

  get isEmpty(): boolean {
    return !this.loading() && !this.error() && this.items().length === 0;
  }

  load(page = this.page()): void {
    this.loading.set(true);
    this.error.set(null);
    this.fetch(page, this.pageSize()).subscribe({
      next: (response) => {
        this.items.set(response.items);
        this.page.set(response.page);
        this.pageSize.set(response.pageSize);
        this.totalCount.set(response.totalCount);
        this.totalPages.set(response.totalPages);
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        this.error.set(toPresentableError(failure));
        this.loading.set(false);
      },
    });
  }

  reload(): void {
    this.load(this.page());
  }

  /** After a deletion the current page may have emptied; step back rather than show an empty page. */
  reloadAfterRemoval(): void {
    const lastOnPage = this.items().length === 1 && this.page() > 1;
    this.load(lastOnPage ? this.page() - 1 : this.page());
  }

  next(): void {
    if (this.page() < this.totalPages()) {
      this.load(this.page() + 1);
    }
  }

  previous(): void {
    if (this.page() > 1) {
      this.load(this.page() - 1);
    }
  }
}
