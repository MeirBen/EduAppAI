import { DOCUMENT, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { distinctUntilChanged, filter, fromEvent, map, skip, startWith } from 'rxjs';

/** Emits whether the page is visible, at once and after each change. */
export const pageVisible = (document: Document) =>
  fromEvent(document, 'visibilitychange').pipe(
    startWith(null),
    map(() => document.visibilityState === 'visible'),
    distinctUntilChanged(),
  );

/**
 * Refreshes data each time the page becomes visible again, as data libraries refetch on window
 * focus, so a list reflects work done meanwhile on another device. Must run in an injection context.
 */
export function refreshOnReturn(refresh: () => void) {
  pageVisible(inject(DOCUMENT))
    .pipe(skip(1), filter(Boolean), takeUntilDestroyed())
    .subscribe(() => refresh());
}
