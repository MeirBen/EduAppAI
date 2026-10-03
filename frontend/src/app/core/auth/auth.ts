import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, inject, Injectable, signal } from '@angular/core';
import { catchError, map, Observable, of, switchMap, tap, throwError } from 'rxjs';
import { requestResult } from '../api/request-result';

/** Tracks the parent session for navigation; the browser keeps the HttpOnly authentication cookie. */
@Injectable({ providedIn: 'root' })
export class Auth {
  private readonly http = inject(HttpClient);
  private readonly lifetime = inject(DestroyRef);
  private readonly session = signal(false);
  readonly signedIn = this.session.asReadonly();

  /**
   * Reads the session as one cancellable route-guard subscription. The request token is read only
   * when this tab has no session yet; it stays valid for the same identity, and sign-in renews it.
   * Emits true for a session, false for HTTP 401; other failures remain errors.
   */
  loadSession(): Observable<boolean> {
    return this.http.get('/api/auth/me').pipe(
      switchMap(() => (this.session() ? of(null) : this.refreshCsrf())),
      map(() => true),
      catchError((error: unknown) =>
        error instanceof HttpErrorResponse && error.status === 401
          ? of(false)
          : throwError(() => error),
      ),
      tap((signedIn) => this.session.set(signedIn)),
    );
  }

  /** Completes sign-in after the identity-bound token is ready; leaving the page cancels remaining requests. */
  async login(email: string, password: string, lifetime: DestroyRef): Promise<void> {
    await requestResult(this.refreshCsrf(), lifetime);
    await requestResult(this.http.post('/api/auth/login', { email, password }), lifetime);
    await requestResult(this.refreshCsrf(), lifetime);
    this.session.set(true);
  }

  /** Sends a protected sign-out request and clears local state only after the server accepts it. */
  async logout(): Promise<void> {
    await requestResult(this.refreshCsrf(), this.lifetime);
    await requestResult(this.http.post('/api/auth/logout', {}), this.lifetime);
    this.session.set(false);
    // Refresh the anonymous token on the next login, not after a successful sign-out.
  }

  /** Obtains an identity-bound request token that HttpClient sends on same-origin writes. */
  private refreshCsrf() {
    return this.http.get('/api/auth/csrf');
  }
}
