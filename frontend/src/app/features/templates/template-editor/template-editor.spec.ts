import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Subject } from 'rxjs';
import { CreateInstance } from '../../instances/create-instance/create-instance';
import { readingDefinition } from '../ai-template-form/ai-template.fixture';
import { TemplateEditor } from './template-editor';

describe('Template publication navigation', () => {
  it.each([false, true])(
    'keeps a successful publication saved while navigation is delayed or fails (revision: %s)',
    async (revision) => {
      const navigation = new Subject<boolean>();
      let blocked = true;
      TestBed.configureTestingModule({
        providers: [
          provideHttpClient(),
          provideHttpClientTesting(),
          provideRouter(
            [
              { path: 'templates/new', component: TemplateEditor },
              { path: 'templates/:templateId/edit', component: TemplateEditor },
              {
                path: 'templates/:templateId/create',
                component: CreateInstance,
                canActivate: [() => (blocked ? navigation : true)],
              },
            ],
            withComponentInputBinding(),
          ),
        ],
      });
      const harness = await RouterTestingHarness.create(
        revision ? '/templates/template/edit' : '/templates/new',
      );
      const http = TestBed.inject(HttpTestingController);
      const template = {
        id: 'template',
        currentVersion: 1,
        versionId: 'version',
        definition: readingDefinition,
      };
      if (revision) http.expectOne('/api/templates/template').flush(template);
      else {
        http.expectOne('/api/ai/status').flush({ configured: true });
        await harness.fixture.whenStable();
        const prompt =
          harness.routeNativeElement!.querySelector<HTMLTextAreaElement>('#parent-prompt')!;
        prompt.value = 'תרגול קריאה';
        prompt.dispatchEvent(new Event('input'));
        harness
          .routeNativeElement!.querySelector('form')!
          .dispatchEvent(new Event('submit', { cancelable: true }));
        http.expectOne('/api/ai/template-drafts').flush({ definition: readingDefinition });
      }
      await harness.fixture.whenStable();
      const editor = harness.routeNativeElement!;
      editor
        .querySelector('app-ai-template-form form')!
        .dispatchEvent(new Event('submit', { cancelable: true }));
      http
        .expectOne(revision ? '/api/templates/template/versions' : '/api/templates')
        .flush(template);
      await vi.waitFor(() => {
        TestBed.tick();
        expect(editor.querySelector('form')).toBeNull();
        expect(editor.querySelector('p[role="status"]')?.textContent).toContain('התבנית נשמרה');
      });

      // Exercise both rejected and cancelled navigation after the write already succeeded.
      if (revision) navigation.error(new Error('Navigation failed'));
      else navigation.next(false);
      await harness.fixture.whenStable();
      blocked = false;
      editor.querySelector<HTMLAnchorElement>('a[href="/templates/template/create"]')!.click();
      (await vi.waitFor(() => http.expectOne('/api/templates/template'))).flush(template);
      await harness.fixture.whenStable();
      expect(TestBed.inject(Router).url).toBe('/templates/template/create');
      http.expectNone((request) => request.method === 'POST');
      http.verify();
    },
  );
});
