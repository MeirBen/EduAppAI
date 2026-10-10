import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, inject, Injectable, signal } from '@angular/core';
import { catchError, map, of, switchMap, tap, throwError } from 'rxjs';
import { ChildSessionIdentity } from '../api/child-models';
import { requestResult } from '../api/request-result';
import { DeviceEntry } from './device-entry';

/** Device identity stays in memory; only the server's HttpOnly cookie grants child access. */
@Injectable({ providedIn: 'root' })
export class ChildAuth {
  private readonly http = inject(HttpClient);
  private readonly lifetime = inject(DestroyRef);
  private readonly entry = inject(DeviceEntry);
  private readonly session = signal<ChildSessionIdentity | null>(null);
  readonly identity = this.session.asReadonly();

  /** Cancellable route check. Only 401 means missing access; availability errors propagate. */
  loadSession() {
    return this.http.get<ChildSessionIdentity>('/api/child/auth/me').pipe(
      switchMap((identity) => {
        const previous = this.session();
        return previous?.childId === identity.childId &&
          previous.expiresAtUtc === identity.expiresAtUtc
          ? of(identity)
          : this.refreshCsrf().pipe(map(() => identity));
      }),
      catchError((error: unknown) =>
        error instanceof HttpErrorResponse && error.status === 401
          ? of(null)
          : throwError(() => error),
      ),
      tap((identity) => {
        this.session.set(identity);
        if (identity) this.entry.remember('child');
      }),
    );
  }

  /** Redeems once. If the response is lost, the caller must check the session, never replay the code. */
  async activate(code: string, lifetime: DestroyRef) {
    await requestResult(this.refreshCsrf(), lifetime);
    await requestResult(this.http.post('/api/child/auth/activate', { code }), lifetime);
    return requestResult(this.loadSession(), lifetime);
  }

  /** Revokes this grant after navigation has accepted any unsaved-work warning. */
  async logout() {
    try {
      await requestResult(this.refreshCsrf(), this.lifetime);
      await requestResult(this.http.post('/api/child/auth/logout', {}), this.lifetime);
    } catch (error) {
      if (!(error instanceof HttpErrorResponse && error.status === 401)) throw error;
    }
    this.session.set(null);
  }
  private refreshCsrf() {
    return this.http.get('/api/child/auth/csrf');
  }
}
