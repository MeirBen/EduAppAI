import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { appConfig } from './app.config';
import { numericPlan } from './features/activities/learning-plan.fixture';
import { provideLimits } from './core/api/limits.fixture';

describe('Workspace routes', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [...appConfig.providers, provideHttpClientTesting(), provideLimits()],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  /** Navigates as a signed-in parent while AI is unconfigured. */
  async function open(harness: RouterTestingHarness, url: string) {
    const navigation = harness.navigateByUrl(url);
    if (url === '/')
      (await vi.waitFor(() => http.expectOne('/api/child/auth/me'))).flush(
        {},
        { status: 401, statusText: 'Unauthorized' },
      );
    (await vi.waitFor(() => http.expectOne('/api/auth/me'))).flush({
      email: 'parent@example.test',
      familyId: 'family',
    });
    (await vi.waitFor(() => http.expectOne('/api/auth/csrf'))).flush({});
    if (url === '/')
      (await vi.waitFor(() => http.expectOne('/api/auth/me'))).flush({
        email: 'parent@example.test',
        familyId: 'family',
      });
    await navigation;
    http.expectOne('/api/ai/status').flush({ configured: false });
  }

  it('opens the parent workspace from the app start URL with a parent session', async () => {
    const harness = await RouterTestingHarness.create();
    await open(harness, '/');
    await harness.fixture.whenStable();
    expect(TestBed.inject(Router).url).toBe('/activities/new');
    expect(harness.routeNativeElement!.querySelector('#chat-message')).not.toBeNull();
    http.expectNone('/api/child/auth/csrf');
  });

  it('offers both entry screens when the device has no preference or session', async () => {
    const harness = await RouterTestingHarness.create();
    const navigation = harness.navigateByUrl('/');
    for (const endpoint of ['/api/child/auth/me', '/api/auth/me'])
      (await vi.waitFor(() => http.expectOne(endpoint))).flush(
        {},
        { status: 401, statusText: 'Unauthorized' },
      );
    await navigation;
    await harness.fixture.whenStable();
    expect(TestBed.inject(Router).url).toBe('/');
    expect(harness.routeNativeElement!.querySelector('a[href="/child/activate"]')).not.toBeNull();
    expect(harness.routeNativeElement!.querySelector('a[href="/login"]')).not.toBeNull();
    http.expectNone('/api/child/auth/csrf');
  });

  it('keeps a remembered parent device at parent login after expiry', async () => {
    localStorage.setItem('entry-mode', 'parent');
    const harness = await RouterTestingHarness.create();
    const navigation = harness.navigateByUrl('/');
    for (let check = 0; check < 2; check++)
      (await vi.waitFor(() => http.expectOne('/api/auth/me'))).flush(
        {},
        { status: 401, statusText: 'Unauthorized' },
      );
    await navigation;
    expect(TestBed.inject(Router).url).toBe('/login');
    expect(localStorage.getItem('entry-mode')).toBe('parent');
    http.expectNone((r) => r.url.startsWith('/api/child'));
  });

  it('preserves the unsent request when only the query or fragment changes', async () => {
    const harness = await RouterTestingHarness.create();
    await open(harness, '/activities/new');
    await harness.fixture.whenStable();
    const field = harness.routeNativeElement!.querySelector<HTMLInputElement>('#chat-message')!;
    field.value = 'החלל';
    field.dispatchEvent(new Event('input'));

    const navigation = harness.navigateByUrl('/activities/new?source=library#practice-title');
    (await vi.waitFor(() => http.expectOne('/api/auth/me'))).flush({
      email: 'parent@example.test',
      familyId: 'family',
    });
    await navigation;
    await harness.fixture.whenStable();

    expect(
      harness.routeNativeElement!.querySelector<HTMLInputElement>('#chat-message')!.value,
    ).toBe('החלל');
    http.expectNone('/api/ai/activity-plans');
  });

  it('opens saved activities without template publication or automatic writes', async () => {
    const harness = await RouterTestingHarness.create();
    await open(harness, '/activities/saved');
    http.expectOne('/api/activity-drafts/saved').flush({
      id: 'saved',
      revision: 4,
      plan: numericPlan,
      document: { title: '', instructions: null, materials: [], questions: [] },
      diagnostics: {},
      measurements: [],
      activeOperationId: null,
      releasedSnapshotId: null,
      releasedSourceRevision: null,
      chat: [],
      canUndo: false,
      createdAtUtc: '2026-10-01T00:00:00Z',
      updatedAtUtc: '2026-10-01T00:00:00Z',
    });
    await harness.fixture.whenStable();
    expect(harness.routeNativeElement!.querySelector('#save-template')).toBeNull();
    http.expectNone((request) => request.method !== 'GET');
  });
});
