import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { appConfig, applicationConfig } from './app.config';
import { contentFirstRoutes } from './app.routes';
import { ActivityWorkspace } from './features/activities/activity-workspace/activity-workspace';
import { numericPlan } from './features/activities/learning-plan.fixture';
import { CreateInstance } from './features/instances/create-instance/create-instance';
import { readingDefinition } from './features/templates/ai-template-form/ai-template.fixture';

describe('Template navigation', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      providers: [...appConfig.providers, provideHttpClientTesting()],
    }),
  );
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('cancels the old generation and starts with an enabled form when switching templates', async () => {
    const harness = await RouterTestingHarness.create();
    await openTemplate(harness, 'first');
    harness
      .routeNativeElement!.querySelector('form')!
      .dispatchEvent(new Event('submit', { cancelable: true }));
    const http = TestBed.inject(HttpTestingController);
    const previous = http.expectOne('/api/templates/first/instances');

    await openTemplate(harness, 'second');

    expect(previous.cancelled).toBe(true);
    expect(TestBed.inject(Router).url).toBe('/templates/second/create');
    const form = harness.routeNativeElement!.querySelector('form')!;
    expect(form.querySelector<HTMLButtonElement>('button[type="submit"]')!.disabled).toBe(false);
    expect(harness.routeNativeElement!.querySelector('[role="alert"]')).toBeNull();
    form.dispatchEvent(new Event('submit', { cancelable: true }));
    const next = http.expectOne('/api/templates/second/instances');
    harness.fixture.destroy();
    expect(next.cancelled).toBe(true);
  });

  it('preserves entered choices when only the query or fragment changes', async () => {
    const harness = await RouterTestingHarness.create();
    await openTemplate(harness, 'first');
    const field =
      harness.routeNativeElement!.querySelector<HTMLInputElement>('input[type="text"]')!;
    field.value = 'החלל';
    field.dispatchEvent(new Event('input'));

    await harness.navigateByUrl('/templates/first/create?source=library#practice-title');
    await harness.fixture.whenStable();

    expect(
      harness.routeNativeElement!.querySelector<HTMLInputElement>('input[type="text"]')!.value,
    ).toBe('החלל');
    TestBed.inject(HttpTestingController).expectNone('/api/templates/first');
  });
});

async function openTemplate(harness: RouterTestingHarness, id: string) {
  const http = TestBed.inject(HttpTestingController);
  const navigation = harness.navigateByUrl(`/templates/${id}/create`, CreateInstance);
  (await vi.waitFor(() => http.expectOne('/api/auth/me'))).flush({
    email: 'parent@example.test',
    familyId: 'family',
  });
  (await vi.waitFor(() => http.expectOne('/api/auth/csrf'))).flush({});
  await navigation;
  http.expectOne(`/api/templates/${id}`).flush({
    id,
    currentVersion: 1,
    versionId: `${id}-version`,
    definition: readingDefinition,
  });
  await harness.fixture.whenStable();
}

describe('Staged workspace route composition', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      providers: [...applicationConfig(contentFirstRoutes).providers, provideHttpClientTesting()],
    }),
  );
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('opens a saved activity and publishes a separate template without writing the activity', async () => {
    const harness = await RouterTestingHarness.create(),
      http = TestBed.inject(HttpTestingController);
    const navigation = harness.navigateByUrl('/activities/saved', ActivityWorkspace);
    (await vi.waitFor(() => http.expectOne('/api/auth/me'))).flush({
      email: 'parent@example.test',
      familyId: 'family',
    });
    (await vi.waitFor(() => http.expectOne('/api/auth/csrf'))).flush({ token: 'isolated' });
    await navigation;
    http.expectOne('/api/ai/status').flush({ configured: false, schemaVersion: 1 });
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
