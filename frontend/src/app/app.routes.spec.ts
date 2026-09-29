import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { appConfig } from './app.config';
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
