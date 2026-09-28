import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/** Tracks the parent session for navigation; the browser keeps the HttpOnly authentication cookie. */
@Injectable({ providedIn: 'root' })
export class Auth {
  private readonly http = inject(HttpClient);
  readonly signedIn = signal(false);

  /** Obtains an identity-bound request token that HttpClient sends on same-origin writes. */
  async refreshCsrf(): Promise<void> {
    await firstValueFrom(this.http.get('/api/auth/csrf'));
  }

  /**
   * Reads the cookie-backed session and refreshes its antiforgery token.
   * @returns True after session/token refresh, or false for HTTP 401; other failures reject.
   */
  async loadSession(): Promise<boolean> {
    try {
      await firstValueFrom(this.http.get('/api/auth/me'));
      this.signedIn.set(true);
      await this.refreshCsrf();
      return true;
    } catch (error) {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401) throw error;
      this.signedIn.set(false);
      return false;
    }
  }

  /** Signs in with a CSRF token for the current identity, then reloads state for the new identity. */
  async login(email: string, password: string): Promise<void> {
    await this.refreshCsrf();
    await firstValueFrom(this.http.post('/api/auth/login', { email, password }));
    await this.loadSession();
  }

  /** Sends a protected sign-out request and clears local state only after the server accepts it. */
  async logout(): Promise<void> {
    await this.refreshCsrf();
    await firstValueFrom(this.http.post('/api/auth/logout', {}));
    this.signedIn.set(false);
    // Refresh the anonymous token on the next login, not after a successful sign-out.
  }
}
