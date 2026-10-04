import { HttpErrorResponse } from '@angular/common/http';
import { computed, DestroyRef, effect, inject, signal } from '@angular/core';
import { LearningApi } from '../../../core/api/learning-api';
import { ActivityDetail } from '../../../core/api/models';
import { requestResult } from '../../../core/api/request-result';
import { whenIdle } from '../../../core/when-idle';

/**
 * Whether the followed draft was changed or deleted elsewhere, such as on another device. While
 * `draft` returns the newest version this page knows, each change note reads the saved draft. The
 * page's own writes explain their changes, so reads wait while `busy`, never overlap, and report
 * nothing while busy. Must run in an injection context.
 */
export function draftElsewhere(draft: () => ActivityDetail | undefined, busy: () => boolean) {
  const api = inject(LearningApi);
  const lifetime = inject(DestroyRef);
  const id = computed(() => draft()?.id);
  // A missing revision means the read found the draft deleted.
  const latest = signal<{ id: string; revision?: number } | undefined>(undefined);
  const reading = signal(false);
  const read = whenIdle(
    () => busy() || reading(),
    async () => {
      const current = id();
      if (!current) return;
      reading.set(true);
      try {
        const { revision } = await requestResult(api.readActivity(current), lifetime);
        latest.set({ id: current, revision });
      } catch (error) {
        // Any other failure changes nothing; the next change note reads again.
        if (error instanceof HttpErrorResponse && error.status === 404) latest.set({ id: current });
      } finally {
        reading.set(false);
      }
    },
  );
  effect((cleanup) => {
    if (!id()) return;
    const subscription = api.libraryChanges().subscribe(read);
    cleanup(() => subscription.unsubscribe());
  });
  return computed(() => {
    const known = draft(),
      seen = latest();
    if (!known || seen?.id !== known.id || busy()) return undefined;
    if (seen.revision === undefined) return 'deleted';
    return seen.revision > known.revision ? 'changed' : undefined;
  });
}
