import { distinctUntilChanged, fromEvent, map, startWith } from 'rxjs';

/** Emits whether the page is visible, at once and after each change. */
export const pageVisible = (document: Document) =>
  fromEvent(document, 'visibilitychange').pipe(
    startWith(null),
    map(() => document.visibilityState === 'visible'),
    distinctUntilChanged(),
  );
