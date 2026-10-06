import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { parentRoutes } from '../../app.routes';
import { Auth } from './auth';

@Component({ template: '' })
class TestPage {
  departures = 0;
}

describe('Login navigation', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          ...parentRoutes.filter(
            (route) => route.path === 'login' || route.path === 'access-unavailable',
          ),
          {
            path: 'private',
            component: TestPage,
            canDeactivate: [
              (page: TestPage) => {
                page.departures++;
                return true;
              },
            ],
          },
          { path: 'home', component: TestPage },
          { path: 'public', component: TestPage },
          { path: '', pathMatch: 'full', redirectTo: 'home' },
        ]),
      ],
    }),
  );
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('skips login for a verified session, but rechecks and shows the form after expiry', async () => {
    const harness = await RouterTestingHarness.create();
    const http = TestBed.inject(HttpTestingController);
    const navigation = harness.navigateByUrl('/login');
    (await vi.waitFor(() => http.expectOne('/api/auth/me'))).flush({
      email: 'parent@example.test',
    });
    (await vi.waitFor(() => http.expectOne('/api/auth/csrf'))).flush({});
    await navigation;
    expect(TestBed.inject(Router).url).toBe('/home');
    expect(TestBed.inject(Auth).signedIn()).toBe(true);
    expect(harness.routeNativeElement?.querySelector('form')).toBeNull();

    const expired = harness.navigateByUrl('/login');
    (await vi.waitFor(() => http.expectOne('/api/auth/me'))).flush(
      {},
      { status: 401, statusText: 'Unauthorized' },
    );
    await expired;
    expect(TestBed.inject(Router).url).toBe('/login');
    expect(TestBed.inject(Auth).signedIn()).toBe(false);
    expect(harness.routeNativeElement?.querySelector('form')).not.toBeNull();
  });

  it('asks to leave the outgoing page only once when login redirects to the app', async () => {
    const harness = await RouterTestingHarness.create();
    const page = await harness.navigateByUrl('/private', TestPage);
    const http = TestBed.inject(HttpTestingController);
    const navigation = harness.navigateByUrl('/login');
    (await vi.waitFor(() => http.expectOne('/api/auth/me'))).flush({
      email: 'parent@example.test',
    });
    (await vi.waitFor(() => http.expectOne('/api/auth/csrf'))).flush({});
    await navigation;
    expect(TestBed.inject(Router).url).toBe('/home');
    expect(page.departures).toBe(1);
  });

  it.each(['session', 'token'])(
    'cancels the login %s check when navigation is superseded',
    async (stage) => {
      const harness = await RouterTestingHarness.create();
      const http = TestBed.inject(HttpTestingController);
      const navigation = harness.navigateByUrl('/login');
      let request = await vi.waitFor(() => http.expectOne('/api/auth/me'));
      if (stage === 'token') {
        request.flush({ email: 'parent@example.test' });
        request = await vi.waitFor(() => http.expectOne('/api/auth/csrf'));
      }
      await harness.navigateByUrl('/public');
      await navigation;
      expect(request.cancelled).toBe(true);
      expect(TestBed.inject(Router).url).toBe('/public');
      expect(TestBed.inject(Auth).signedIn()).toBe(false);
    },
  );

  it('shows retry instead of credentials when session discovery fails', async () => {
    const harness = await RouterTestingHarness.create();
    const http = TestBed.inject(HttpTestingController);
    const navigation = harness.navigateByUrl('/login');
    (await vi.waitFor(() => http.expectOne('/api/auth/me'))).error(new ProgressEvent('error'));
    await navigation;
    expect(TestBed.inject(Router).url).toBe('/access-unavailable');
    expect(harness.routeNativeElement?.querySelector('form')).toBeNull();
    expect(harness.routeNativeElement?.querySelector('[role="alert"]')).not.toBeNull();
    http.expectNone('/api/auth/logout');
  });
});
