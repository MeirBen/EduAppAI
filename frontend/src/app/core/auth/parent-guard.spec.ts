import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Limits } from '../api/limits';
import { limits } from '../api/limits.fixture';
import { Auth } from './auth';
import { parentGuard } from './parent-guard';

@Component({ template: '' })
class TestPage {}

describe('Parent navigation', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          { path: 'private', canActivate: [parentGuard], component: TestPage },
          { path: 'public', component: TestPage },
          { path: 'login', component: TestPage },
        ]),
      ],
    }),
  );
  afterEach(() => TestBed.inject(HttpTestingController).verify());

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

  it('loads the server limits once, before activating the private page', async () => {
    const harness = await RouterTestingHarness.create();
    const http = TestBed.inject(HttpTestingController);
    for (const visit of [1, 2]) {
      const navigation = harness.navigateByUrl('/private');
      (await vi.waitFor(() => http.expectOne('/api/auth/me'))).flush({
        email: 'parent@example.test',
      });
      (await vi.waitFor(() => http.expectOne('/api/auth/csrf'))).flush({});
      if (visit === 1) {
        expect(TestBed.inject(Router).url).not.toBe('/private');
        (await vi.waitFor(() => http.expectOne('/api/limits'))).flush(limits);
      }
      await navigation;
      expect(TestBed.inject(Router).url).toBe('/private');
      await harness.navigateByUrl('/public');
    }
    expect(TestBed.inject(Limits).current).toEqual(limits);
  });

  it.each([
    [401, '/login'],
    [503, '/login?connection=unavailable'],
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
  });
});
