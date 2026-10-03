import { exhaustMap, map, switchMap, takeWhile, timer } from 'rxjs';
import { LearningApi } from '../../../core/api/learning-api';
import { isRunning } from '../generation-status/operation-state';

/**
 * Read-only polling of one operation until it is terminal, emitting the terminal result last.
 * Reads never overlap, and each status is paired with the draft checkpoint read after it, so a
 * terminal result cannot hide its final commit. Unsubscribing stops polling.
 */
export const pollOperation = (api: LearningApi, draftId: string, operationId: string) =>
  timer(2000, 2000).pipe(
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
