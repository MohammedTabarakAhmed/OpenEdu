import { TestBed } from '@angular/core/testing';
import { SearchBox } from './ui';

describe('SearchBox', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    TestBed.configureTestingModule({ imports: [SearchBox] });
  });
  afterEach(() => vi.useRealTimers());

  it('emits once after the debounce while typing, and immediately on submit', () => {
    const fixture = TestBed.createComponent(SearchBox);
    const emitted: string[] = [];
    fixture.componentInstance.search.subscribe((term) => emitted.push(term));
    fixture.detectChanges();
    const input: HTMLInputElement = fixture.nativeElement.querySelector('input');

    for (const value of ['c', 'cs', 'cs1']) {
      input.value = value;
      input.dispatchEvent(new Event('input'));
      vi.advanceTimersByTime(100);
    }
    expect(emitted).toEqual([]); // still inside the quiet period — no request per keystroke

    vi.advanceTimersByTime(SearchBox.DebounceMs);
    expect(emitted).toEqual(['cs1']);

    fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit'));
    expect(emitted).toEqual(['cs1', 'cs1']);
  });
});
