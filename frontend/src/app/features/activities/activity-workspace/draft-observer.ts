import { HttpErrorResponse } from '@angular/common/http';
import { computed, DestroyRef, DOCUMENT, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  catchError,
  defer,
  finalize,
  firstValueFrom,
  interval,
  map,
  Observable,
  of,
  Subject,
  Subscription,
  switchMap,
  takeUntil,
  tap,
  throwError,
} from 'rxjs';
import { LearningApi } from '../../../core/api/learning-api';
import { ActivityDetail, GenerationOperation } from '../../../core/api/models';
import { pageVisible } from '../../../core/page-visibility';
import { whenIdle } from '../../../core/when-idle';
import { isRunning } from '../generation-status/operation-state';

export interface DraftObservation {
  draft: ActivityDetail;
  operation?: GenerationOperation;
}

/**
 * Owns reads for one workspace. Hints coalesce behind an active read or command; only running
 * operations poll. Commands suspend older reads synchronously, then may read their own result.
 * Content application, operation presentation and all writes stay with the workspace.
 */
export function observeDraft(options: {
  draftId: () => string | undefined;
  operationId: () => string | undefined;
  enabled: () => boolean;
  busy: () => boolean;
  changes: Observable<void>;
  changed: (observation: DraftObservation) => void;
  failed: (error: unknown) => void;
}) {
  const api = inject(LearningApi);
  const document = inject(DOCUMENT);
  const lifetime = inject(DestroyRef);
  const draftId = computed(options.draftId);
  const operationId = computed(options.operationId);
  const enabled = computed(options.enabled);
  const busy = computed(options.busy);
  const reading = signal(false);
  const visible = signal(document.visibilityState === 'visible');
  const polling = signal(false);
  const interrupted = new Subject<void>();
  let background: Subscription | undefined;
  const suspend = () => background?.unsubscribe();

  effect((cleanup) => {
    polling.set(!!draftId() && !!operationId());
    cleanup(() => interrupted.next());
  });
  const refresh = whenIdle(
    () => !draftId() || !enabled() || !visible() || busy() || reading(),
    () => {
      background = query().subscribe({
        next: options.changed,
        error: (error: unknown) => {
          if (!(error instanceof HttpErrorResponse) || (error.status > 0 && error.status < 500))
            polling.set(false);
          options.failed(error);
        },
      });
    },
  );
  effect((cleanup) => {
    if (!draftId() || !enabled()) return;
    const subscription = options.changes.subscribe(refresh);
    cleanup(() => {
      subscription.unsubscribe();
      suspend();
    });
  });
  effect(() => {
    if (busy()) suspend();
  });
  effect((cleanup) => {
    if (!draftId() || !operationId() || !enabled() || !polling() || !visible() || busy()) return;
    const subscription = interval(2000).subscribe(refresh);
    cleanup(() => subscription.unsubscribe());
  });
  pageVisible(document)
    .pipe(takeUntilDestroyed())
    .subscribe((shown) => {
      const returning = !visible() && shown;
      visible.set(shown);
      if (!shown) suspend();
      else if (returning) refresh();
    });

  function query(acknowledged?: GenerationOperation): Observable<DraftObservation> {
    const id = draftId(),
      op = operationId();
    if (!id) return throwError(() => new Error('No draft to observe.'));
    // A cancellation acknowledgement already contains the authoritative operation result.
    let operation = of(acknowledged);
    if (!acknowledged && op)
      operation = api
        .operation(id, op)
        .pipe(
          catchError((error: unknown) =>
            error instanceof HttpErrorResponse && error.status === 404
              ? of(undefined)
              : throwError(() => error),
          ),
        );
    return defer(() => {
      reading.set(true);
      // Read the draft after status so a terminal response cannot hide its final checkpoint.
      // An absent operation still reads the draft; only the draft's own 404 means deletion.
      return operation.pipe(
        switchMap((operation) => api.readActivity(id).pipe(map((draft) => ({ draft, operation })))),
        tap(({ operation }) => polling.set(!!operation && isRunning(operation))),
        takeUntil(interrupted),
        takeUntilDestroyed(lifetime),
        finalize(() => reading.set(false)),
      );
    });
  }

  return {
    suspend,
    /** Reads after an explicit command; cancels earlier reads and rejects on failure or disposal. */
    read(acknowledged?: GenerationOperation) {
      interrupted.next();
      return firstValueFrom(query(acknowledged));
    },
  };
}
