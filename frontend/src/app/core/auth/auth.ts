import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

interface Parent {
  email: string;
  familyId: string;
}

@Injectable({ providedIn: 'root' })
export class Auth {
  private readonly http = inject(HttpClient);
  readonly parent = signal<Parent | null>(null);

  async refreshCsrf(): Promise<void> {
    await firstValueFrom(this.http.get('/api/auth/csrf'));
  }

  async loadSession(): Promise<boolean> {
    try {
      this.parent.set(await firstValueFrom(this.http.get<Parent>('/api/auth/me')));
      await this.refreshCsrf();
      return true;
    } catch (error) {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401) throw error;
      this.parent.set(null);
      return false;
    }
  }

  async login(email: string, password: string): Promise<void> {
    await this.refreshCsrf();
    await firstValueFrom(this.http.post('/api/auth/login', { email, password }));
    // Antiforgery tokens are tied to the current identity.
    await this.loadSession();
  }

  async logout(): Promise<void> {
    await this.refreshCsrf();
    await firstValueFrom(this.http.post('/api/auth/logout', {}));
    this.parent.set(null);
    // The next login obtains an anonymous token. A refresh failure must not block sign-out.
  }
}
