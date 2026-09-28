import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
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
