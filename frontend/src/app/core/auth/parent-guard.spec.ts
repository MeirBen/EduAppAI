import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Login } from '../../features/auth/login/login';
import { AccessUnavailable } from '../../features/auth/access-unavailable/access-unavailable';
import { Limits } from '../api/limits';
import { limits } from '../api/limits.fixture';
import { Auth } from './auth';
import { parentGuard } from './parent-guard';

@Component({ template: '' })
class TestPage {
  departures = 0;
}

describe('Parent navigation', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          { path: 'private', canMatch: [parentGuard], component: TestPage },
          {
            path: 'public',
            component: TestPage,
            canDeactivate: [
              (page: TestPage) => {
                page.departures++;
                return true;
              },
            ],
          },
          { path: 'login', component: Login },
          { path: 'access-unavailable', component: AccessUnavailable },
          { path: '', pathMatch: 'full', redirectTo: 'private' },
        ]),
      ],
    }),
  );
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it.each([401, 503])('asks to leave only once when access returns HTTP %s', async (status) => {
    const harness = await RouterTestingHarness.create();
    const page = await harness.navigateByUrl('/public', TestPage);
    const navigation = harness.navigateByUrl('/private');
    (await vi.waitFor(() => TestBed.inject(HttpTestingController).expectOne('/api/auth/me'))).flush(
      {},
      { status, statusText: 'Unavailable' },
    );
    await navigation;
    expect(page.departures).toBe(1);
  });

  it.each(['session', 'token'])(
    'cancels the %s check when another navigation replaces it',
    async (stage) => {
      const harness = await RouterTestingHarness.create();
      const http = TestBed.inject(HttpTestingController);
      const navigation = harness.navigateByUrl('/private');
      let request = await vi.waitFor(() => http.expectOne('/api/auth/me'));
      if (stage === 'token') {
        request.flush({ email: 'parent@example.test', familyId: 'family' });
        request = await vi.waitFor(() => http.expectOne('/api/auth/csrf'));
      }
      await harness.navigateByUrl('/public');
      await navigation;
      expect(request.cancelled).toBe(true);
      expect(TestBed.inject(Auth).signedIn()).toBe(false);
      expect(TestBed.inject(Router).url).toBe('/public');
    },
  );

  it('checks the session on every visit but reads the token and limits once', async () => {
    const harness = await RouterTestingHarness.create();
    const http = TestBed.inject(HttpTestingController);
    for (const visit of [1, 2]) {
      const navigation = harness.navigateByUrl('/private');
      (await vi.waitFor(() => http.expectOne('/api/auth/me'))).flush({
        email: 'parent@example.test',
      });
      if (visit === 1) {
        (await vi.waitFor(() => http.expectOne('/api/auth/csrf'))).flush({});
        expect(TestBed.inject(Router).url).not.toBe('/private');
        (await vi.waitFor(() => http.expectOne('/api/limits'))).flush(limits);
      }
      await navigation;
      expect(TestBed.inject(Router).url).toBe('/private');
      http.expectNone('/api/auth/csrf');
      await harness.navigateByUrl('/public');
    }
    expect(TestBed.inject(Limits).current).toEqual(limits);
  });

  it.each([
    [401, '/login'],
    [503, '/access-unavailable'],
  ])('handles session HTTP %s without activating the private page', async (status, url) => {
    const harness = await RouterTestingHarness.create();
    const navigation = harness.navigateByUrl('/private');
    const request = await vi.waitFor(() =>
      TestBed.inject(HttpTestingController).expectOne('/api/auth/me'),
    );
    request.flush({}, { status, statusText: 'Unavailable' });
    await navigation;
    expect(TestBed.inject(Router).url).toBe(url);
    expect(TestBed.inject(Auth).signedIn()).toBe(false);
    expect(harness.routeNativeElement?.querySelector('[role="alert"]') !== null).toBe(
      status !== 401,
    );
  });

  it('retries failed page setup without sending credentials again or clearing the session', async () => {
    const harness = await RouterTestingHarness.create('/login');
    const http = TestBed.inject(HttpTestingController);
    const page = harness.routeNativeElement!;
    for (const [id, value] of [
      ['email', 'parent@example.test'],
      ['password', 'TestOnly!Parent12345'],
    ]) {
      const input = page.querySelector<HTMLInputElement>(`#${id}`)!;
      input.value = value;
      input.dispatchEvent(new Event('input', { bubbles: true }));
    }
    page.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    (await vi.waitFor(() => http.expectOne('/api/auth/csrf'))).flush({});
    (await vi.waitFor(() => http.expectOne('/api/auth/login'))).flush(null);
    (await vi.waitFor(() => http.expectOne('/api/auth/csrf'))).flush({});
    for (const attempt of [1, 2]) {
      (await vi.waitFor(() => http.expectOne('/api/auth/me'))).flush({
        email: 'parent@example.test',
      });
      (await vi.waitFor(() => http.expectOne('/api/limits'))).flush(
        {},
        { status: 503, statusText: 'Unavailable' },
      );
      await harness.fixture.whenStable();
      harness.detectChanges();
      expect(TestBed.inject(Router).url, `attempt ${attempt}`).toBe('/access-unavailable');
      expect(TestBed.inject(Auth).signedIn()).toBe(true);
      expect(harness.routeNativeElement?.querySelector('form')).toBeNull();
      harness.routeNativeElement!.querySelector('button')!.click();
      http.expectNone('/api/auth/login');
    }
    (await vi.waitFor(() => http.expectOne('/api/auth/me'))).flush({
      email: 'parent@example.test',
    });
    (await vi.waitFor(() => http.expectOne('/api/limits'))).flush(limits);
    await harness.fixture.whenStable();
    expect(TestBed.inject(Router).url).toBe('/private');
  });
});
