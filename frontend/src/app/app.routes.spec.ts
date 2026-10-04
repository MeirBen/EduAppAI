import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { RouterTestingHarness } from '@angular/router/testing';
import { appConfig } from './app.config';
import { ActivityWorkspace } from './features/activities/activity-workspace/activity-workspace';
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
    const navigation = harness.navigateByUrl(url, ActivityWorkspace);
    (await vi.waitFor(() => http.expectOne('/api/auth/me'))).flush({
      email: 'parent@example.test',
      familyId: 'family',
    });
    (await vi.waitFor(() => http.expectOne('/api/auth/csrf'))).flush({});
    await navigation;
    http
      .expectOne('/api/ai/status')
      .flush({ configured: false, schemaVersion: numericPlan.schemaVersion });
  }

  it('preserves entered choices when only the query or fragment changes', async () => {
    const harness = await RouterTestingHarness.create();
    await open(harness, '/templates/first/create');
    http.expectOne('/api/templates/first').flush({
      id: 'first',
      currentVersion: 1,
      versionId: 'first-version',
      definition: numericPlan,
    });
    await harness.fixture.whenStable();
    const field = harness.routeNativeElement!.querySelector<HTMLInputElement>('#activity-topic')!;
    field.value = 'החלל';
    field.dispatchEvent(new Event('input'));

    const navigation = harness.navigateByUrl(
      '/templates/first/create?source=library#practice-title',
    );
    (await vi.waitFor(() => http.expectOne('/api/auth/me'))).flush({
      email: 'parent@example.test',
      familyId: 'family',
    });
    await navigation;
    await harness.fixture.whenStable();

    expect(
      harness.routeNativeElement!.querySelector<HTMLInputElement>('#activity-topic')!.value,
    ).toBe('החלל');
    http.expectNone('/api/templates/first');
  });

  it('opens a saved activity and publishes a separate template without writing the activity', async () => {
    const harness = await RouterTestingHarness.create();
    await open(harness, '/activities/saved');
    http.expectOne('/api/activity-drafts/saved').flush({
      id: 'saved',
      revision: 4,
      plan: numericPlan,
      input: { settings: numericPlan.defaults },
      document: { title: '', instructions: null, materials: [], questions: [] },
      diagnostics: {},
      activeOperationId: null,
      templateVersionId: null,
      releasedSnapshotId: null,
      releasedSourceRevision: null,
      createdAtUtc: '2026-10-01T00:00:00Z',
      updatedAtUtc: '2026-10-01T00:00:00Z',
    });
    await harness.fixture.whenStable();
    harness.routeNativeElement!.querySelector<HTMLButtonElement>('#save-template')!.click();
    const publication = http.expectOne('/api/templates');
    expect(publication.request.body.name).toBe('מספרים');
    publication.flush({ id: 'new', currentVersion: 1, versionId: 'v1', definition: numericPlan });
    await vi.waitFor(() =>
      expect(harness.routeNativeElement!.textContent).toContain('התבנית נשמרה'),
    );
    http.expectNone(
      (request) => request.url.includes('/activity-drafts') && request.method !== 'GET',
    );
    http.expectNone('/api/ai/template-drafts');
  });
});
