import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { PagedResponse } from '../core/api/api.models';
import { errorsFor, toPresentableError } from '../core/api/problem';
import { PageState } from './page-state';

function page(items: string[], pageNumber: number, totalCount: number, pageSize = 2): PagedResponse<string> {
  return { items, page: pageNumber, pageSize, totalCount, totalPages: Math.ceil(totalCount / pageSize) };
}

describe('PageState (17.5 collection views)', () => {
  it('loads a page from the server and exposes the envelope', () => {
    const fetch = vi.fn((p: number, size: number) => of(page(['a', 'b'], p, 5, size)));
    const state = new PageState<string>(fetch);
    state.pageSize.set(2);

    state.load();

    expect(fetch).toHaveBeenCalledWith(1, 2);
    expect(state.items()).toEqual(['a', 'b']);
    expect(state.totalPages()).toBe(3);
    expect(state.loading()).toBe(false);
    expect(state.isEmpty).toBe(false);
  });

  it('pages server-side: next and previous request the neighbouring page and stop at the bounds', () => {
    const fetch = vi.fn((p: number, size: number) => of(page([`p${p}`], p, 4, size)));
    const state = new PageState<string>(fetch);
    state.pageSize.set(2);
    state.load();

    state.next();
    expect(fetch).toHaveBeenLastCalledWith(2, 2);
    state.next();
    expect(fetch).toHaveBeenCalledTimes(2);
    state.previous();
    expect(fetch).toHaveBeenLastCalledWith(1, 2);
    state.previous();
    expect(fetch).toHaveBeenCalledTimes(3);
  });

  it('shows the empty state for an empty page and the error state for a failure', () => {
    const empty = new PageState<string>(() => of(page([], 1, 0)));
    empty.load();
    expect(empty.isEmpty).toBe(true);

    const failing = new PageState<string>(() => throwError(() => new HttpErrorResponse({ status: 500, error: { title: 'internal_error', detail: 'boom' } })));
    failing.load();
    expect(failing.error()?.status).toBe(500);
    expect(failing.error()?.message).toBe('boom');
    expect(failing.isEmpty).toBe(false);
  });

  it('steps back a page when the last item of a later page was removed', () => {
    const fetch = vi.fn((p: number, size: number) => of(page(p === 2 ? ['only'] : ['a', 'b'], p, 3, size)));
    const state = new PageState<string>(fetch);
    state.pageSize.set(2);
    state.load(2);

    state.reloadAfterRemoval();

    expect(fetch).toHaveBeenLastCalledWith(1, 2);
  });
});

describe('problem translation (API-04, API-07)', () => {
  it('extracts field-keyed errors from a 400 and resolves camelCase control names', () => {
    const error = toPresentableError(new HttpErrorResponse({
      status: 400,
      error: { title: 'validation', detail: 'One or more fields failed validation.', errors: { Code: ['required'], InstructorUserId: ['unknown'] } },
    }));

    expect(error.status).toBe(400);
    expect(errorsFor(error, 'code')).toEqual(['required']);
    expect(errorsFor(error, 'instructorUserId')).toEqual(['unknown']);
    expect(errorsFor(error, 'nameEn')).toEqual([]);
    expect(errorsFor(null, 'code')).toEqual([]);
  });

  it('carries the rule reference of a 422 business rule refusal', () => {
    const error = toPresentableError(new HttpErrorResponse({
      status: 422,
      error: { type: 'urn:opencampus:error:BR-01', title: 'BR-01', detail: 'Enrolment refused: the section has reached its capacity.' },
    }));

    expect(error.code).toBe('BR-01');
    expect(error.message).toContain('capacity');
  });

  it('degrades gracefully for non-HTTP failures', () => {
    expect(toPresentableError(new Error('offline'))).toEqual({ status: 0, code: null, message: null, fieldErrors: {} });
  });
});
