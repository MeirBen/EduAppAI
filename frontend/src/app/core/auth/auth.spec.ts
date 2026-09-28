import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Auth } from './auth';

describe('Auth logout', () => {
  it('finishes sign-out even when a subsequent token refresh would fail', async () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const auth = TestBed.inject(Auth);
    const http = TestBed.inject(HttpTestingController);
    const session = auth.loadSession();
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
    http.verify();
  });
});
