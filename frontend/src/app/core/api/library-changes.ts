import { DOCUMENT, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { EMPTY, finalize, Observable, startWith, Subject, switchMap } from 'rxjs';
import { pageVisible } from '../page-visibility';

/**
 * Page-owned family invalidations. One consumer subscribes while it has content to follow.
 * Native EventSource reconnects transient failures; a refused connection needs explicit retry.
 * Retry also requests a read, so current state remains checkable when the stream is unavailable.
 */
export function libraryChanges(path = '/api/library/changes') {
  const document = inject(DOCUMENT);
  const state = signal<'paused' | 'connecting' | 'connected' | 'unavailable'>('paused');
  const retry = new Subject<boolean>();
  const changes = pageVisible(document).pipe(
    switchMap((visible) => {
      if (!visible) {
        state.set('paused');
        return EMPTY;
      }
      return retry.pipe(
        startWith(false),
        switchMap(
          (refresh) =>
            new Observable<void>((subscriber) => {
              state.set('connecting');
              // The service worker caches assets only; long-lived streams bypass it.
              const source = new EventSource(`${path}?ngsw-bypass`);
              source.onopen = () => state.set('connected');
              source.onmessage = () => subscriber.next();
              source.onerror = () => {
                if (source.readyState === EventSource.CLOSED) {
                  state.set('unavailable');
                  subscriber.complete();
                } else state.set('connecting');
              };
              if (refresh) subscriber.next();
              return () => source.close();
            }),
        ),
      );
    }),
    takeUntilDestroyed(),
    finalize(() => state.set('paused')),
  );
  return { changes, state: state.asReadonly(), reconnect: () => retry.next(true) };
}
