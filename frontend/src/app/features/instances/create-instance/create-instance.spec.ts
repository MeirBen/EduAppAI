import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Subject } from 'rxjs';
import { CreateInstance } from './create-instance';
import { readingDefinition } from '../../templates/ai-template-form/ai-template.fixture';

describe('AI task request lifetime', () => {
  it.each(['cancelled', 'failed'])(
    'retains the saved task when preview navigation is delayed or %s',
    async (outcome) => {
      const navigation = new Subject<boolean>();
      let blocked = true;
      TestBed.configureTestingModule({
        providers: [
          provideHttpClient(),
          provideHttpClientTesting(),
          provideRouter(
            [
              { path: 'templates/:templateId/create', component: CreateInstance },
              {
                path: 'instances/:instanceId',
                children: [],
                canActivate: [() => (blocked ? navigation : true)],
              },
            ],
            withComponentInputBinding(),
          ),
        ],
      });
      const harness = await RouterTestingHarness.create('/templates/template/create');
      const http = TestBed.inject(HttpTestingController);
      http.expectOne('/api/templates/template').flush({
        id: 'template',
        currentVersion: 1,
        versionId: 'version',
        definition: readingDefinition,
      });
      await harness.fixture.whenStable();
      const page = harness.routeNativeElement!;
      page.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
      http.expectOne('/api/templates/template/instances').flush({ id: 'saved-task' });
      await vi.waitFor(() => {
        TestBed.tick();
        expect(page.querySelector('form')).toBeNull();
        expect(page.querySelector('p[role="status"]')?.textContent).toContain('הטיוטה נשמרה');
      });

      if (outcome === 'failed') navigation.error(new Error('Navigation failed'));
      else navigation.next(false);
      await harness.fixture.whenStable();
      expect(page.querySelector('[role="alert"]')).toBeNull();
      expect(page.querySelector('form')).toBeNull();
      blocked = false;
      page.querySelector<HTMLAnchorElement>('a[href="/instances/saved-task"]')!.click();
      await harness.fixture.whenStable();
      expect(TestBed.inject(Router).url).toBe('/instances/saved-task');
      http.expectNone((request) => request.method === 'POST');
      http.verify();
    },
  );

  it('cancels generation on navigation away and cannot redirect the destroyed page', async () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    const fixture = TestBed.createComponent(CreateInstance);
    fixture.componentRef.setInput('templateId', 'template');
    TestBed.tick();
    const element: HTMLElement = fixture.nativeElement;
    expect(element.querySelector('app-loading-indicator')?.textContent).toContain('טוענים');
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/templates/template').flush({
      id: 'template',
      currentVersion: 1,
      versionId: 'version',
      definition: readingDefinition,
    });
    await fixture.whenStable();
    expect(element.querySelector('.loader-mark')).toBeNull();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate');
    fixture.nativeElement
      .querySelector('form')
      .dispatchEvent(new Event('submit', { cancelable: true }));
    TestBed.tick();
    const generation = http.expectOne('/api/templates/template/instances');
    const loading = element.querySelector('section app-loading-indicator');
    expect(loading?.textContent).toContain('יוצרים את התרגול');
    expect(loading?.closest('[aria-busy="true"]')).toBeNull();
    fixture.destroy();
    expect(generation.cancelled).toBe(true);
    await Promise.resolve();
    expect(navigate).not.toHaveBeenCalled();
    http.verify();
  });
});
