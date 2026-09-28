import { DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { firstValueFrom, Observable } from 'rxjs';

/**
 * Awaits one HTTP response, cancelling and rejecting when its owner is destroyed.
 * Never retries; cancelling a write does not guarantee a server rollback.
 */
export function requestResult<T>(request: Observable<T>, lifetime: DestroyRef): Promise<T> {
  return firstValueFrom(request.pipe(takeUntilDestroyed(lifetime)));
}
