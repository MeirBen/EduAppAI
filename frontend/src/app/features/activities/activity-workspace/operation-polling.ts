import {
  distinctUntilChanged,
  EMPTY,
  exhaustMap,
  fromEvent,
  map,
  startWith,
  switchMap,
  takeWhile,
  timer,
} from 'rxjs';
import { LearningApi } from '../../../core/api/learning-api';
import { isRunning } from '../generation-status/operation-state';

/**
 * Read-only polling of one operation until it is terminal, emitting the terminal result last.
 * Reads never overlap, and each status is paired with the draft checkpoint read after it, so a
 * terminal result cannot hide its final commit. A hidden page pauses polling while the server keeps
 * working, and reads at once when shown again. Unsubscribing stops polling.
 */
export const pollOperation = (
  api: LearningApi,
  document: Document,
  draftId: string,
  operationId: string,
) =>
  fromEvent(document, 'visibilitychange').pipe(
    startWith(null),
    map(() => document.visibilityState === 'visible'),
    distinctUntilChanged(),
    switchMap((visible, index) => (visible ? timer(index ? 0 : 2000, 2000) : EMPTY)),
    exhaustMap(() =>
      api
        .operation(draftId, operationId)
        .pipe(
          switchMap((operation) =>
            api.readActivity(draftId).pipe(map((draft) => ({ operation, draft }))),
          ),
        ),
    ),
    takeWhile((result) => isRunning(result.operation), true),
  );
