import { effect, signal, untracked } from '@angular/core';

/**
 * Returns a request to run `action` once `busy` reads false; requests made meanwhile coalesce into
 * one run. Must run in an injection context.
 */
export function whenIdle(busy: () => boolean, action: () => void) {
  const requested = signal(false);
  effect(() => {
    if (requested() && !busy()) {
      requested.set(false);
      untracked(action);
    }
  });
  return () => requested.set(true);
}
