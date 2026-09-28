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
    auth.parent.set({ email: 'parent@example.test', familyId: 'family' });
    const logout = auth.logout();
    http.expectOne('/api/auth/csrf').flush({ token: 'before-signout' });
    await new Promise((resolve) => setTimeout(resolve, 0));
    http.expectOne('/api/auth/logout').flush(null);
    await new Promise((resolve) => setTimeout(resolve, 0));
    for (const request of http.match('/api/auth/csrf')) {
      request.flush({}, { status: 503, statusText: 'Unavailable' });
    }
    await expect(logout).resolves.toBeUndefined();
    expect(auth.parent()).toBeNull();
    http.verify();
  });
});
