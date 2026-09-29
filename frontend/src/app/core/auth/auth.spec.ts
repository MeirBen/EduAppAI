import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { DestroyRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { Auth } from './auth';

describe('Auth session state', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }),
  );
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('keeps sign-in incomplete when the identity-bound token refresh fails', async () => {
    const auth = TestBed.inject(Auth);
    const http = TestBed.inject(HttpTestingController);
    const failedLogin = expect(
      auth.login('parent@example.test', 'TestOnly!Parent12345', TestBed.inject(DestroyRef)),
    ).rejects.toMatchObject({ status: 503 });
    http.expectOne('/api/auth/csrf').flush({ token: 'anonymous' });
    const credentials = await vi.waitFor(() => http.expectOne('/api/auth/login'));
    credentials.flush(null);
    const token = await vi.waitFor(() => http.expectOne('/api/auth/csrf'));
    token.flush({}, { status: 503, statusText: 'Unavailable' });
    await failedLogin;
    expect(auth.signedIn()).toBe(false);
  });

  it('finishes sign-out even when a subsequent token refresh would fail', async () => {
    const auth = TestBed.inject(Auth);
    const http = TestBed.inject(HttpTestingController);
    const session = firstValueFrom(auth.loadSession());
    http.expectOne('/api/auth/me').flush({ email: 'parent@example.test', familyId: 'family' });
    const sessionToken = await vi.waitFor(() => http.expectOne('/api/auth/csrf'));
    sessionToken.flush({ token: 'signed-in' });
    await session;
    expect(auth.signedIn()).toBe(true);
    const logout = auth.logout();
    http.expectOne('/api/auth/csrf').flush({ token: 'before-signout' });
    const logoutRequest = await vi.waitFor(() => http.expectOne('/api/auth/logout'));
    logoutRequest.flush(null);
    await expect(logout).resolves.toBeUndefined();
    http.expectNone('/api/auth/csrf');
    expect(auth.signedIn()).toBe(false);
  });
});
