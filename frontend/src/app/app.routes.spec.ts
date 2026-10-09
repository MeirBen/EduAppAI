import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
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
    (await vi.waitFor(() => http.expectOne('/api/auth/me'))).flush({
      email: 'parent@example.test',
      familyId: 'family',
    });
    (await vi.waitFor(() => http.expectOne('/api/auth/csrf'))).flush({});
    await navigation;
    http.expectOne('/api/ai/status').flush({ configured: false });
  }

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
