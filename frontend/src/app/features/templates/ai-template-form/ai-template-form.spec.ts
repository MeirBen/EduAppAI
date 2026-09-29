import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AiTemplateForm } from './ai-template-form';
import { readingDefinition } from './ai-template.fixture';

describe('Blueprint review', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }),
  );

  it('edits one default question count without changing dynamic parameters or the original template', async () => {
    const definition = structuredClone(readingDefinition);
    const fixture = TestBed.createComponent(AiTemplateForm);
    fixture.componentRef.setInput('definition', definition);
    await fixture.whenStable();
    const element: HTMLElement = fixture.nativeElement;
    const count = element.querySelector<HTMLInputElement>('#default-question-count')!;
    expect(count.value).toBe('5');
    expect(count.closest('details')).toBeNull();
    count.value = '4';
    count.dispatchEvent(new Event('input'));
    element.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    const http = TestBed.inject(HttpTestingController);
    const publication = http.expectOne('/api/templates');
    expect(publication.request.body.generation.questionCount).toBe(4);
    expect(publication.request.body.instanceParameters).toEqual(definition.instanceParameters);
    expect(definition.generation.questionCount).toBe(5);
    publication.flush({ id: 'template', definition: publication.request.body, currentVersion: 1 });
    await fixture.whenStable();
    http.verify();
  });

  it.each([false, true])(
    'cancels a pending publication when leaving the editor (revision: %s)',
    async (revision) => {
      const fixture = TestBed.createComponent(AiTemplateForm);
      fixture.componentRef.setInput('definition', readingDefinition);
      if (revision) {
        fixture.componentRef.setInput('existing', {
          id: 'template',
          currentVersion: 1,
          versionId: 'version',
          definition: readingDefinition,
        });
      }
      await fixture.whenStable();
      fixture.nativeElement
        .querySelector('form')
        .dispatchEvent(new Event('submit', { cancelable: true }));
      const http = TestBed.inject(HttpTestingController);
      const publication = http.expectOne(
        revision ? '/api/templates/template/versions' : '/api/templates',
      );
      fixture.destroy();
      expect(publication.cancelled).toBe(true);
      await Promise.resolve();
      http.verify();
    },
  );

  it('clears obsolete save feedback when a new AI proposal replaces the reviewed definition', async () => {
    const fixture = TestBed.createComponent(AiTemplateForm);
    fixture.componentRef.setInput('definition', readingDefinition);
    await fixture.whenStable();
    const element: HTMLElement = fixture.nativeElement;
    element.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/templates').flush({}, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    await vi.waitFor(() => expect(element.querySelector('[role="alert"]')).not.toBeNull());
    fixture.componentRef.setInput('definition', { ...readingDefinition, name: 'הצעה חדשה' });
    await fixture.whenStable();
    expect(element.querySelector<HTMLInputElement>('#ai-template-name')?.value).toBe('הצעה חדשה');
    expect(element.querySelector('[role="alert"]')).toBeNull();
    http.verify();
  });

  it('lets the parent explicitly remove an empty text default', async () => {
    const definition = structuredClone(readingDefinition);
    definition.instanceParameters[0].required = false;
    definition.instanceParameters[0].default = '';
    const fixture = TestBed.createComponent(AiTemplateForm);
    fixture.componentRef.setInput('definition', definition);
    await fixture.whenStable();
    const element: HTMLElement = fixture.nativeElement;
    const checkbox = Array.from(element.querySelectorAll('label'))
      .find((label) => label.textContent?.includes('שימוש בטקסט ריק כברירת מחדל'))
      ?.querySelector<HTMLInputElement>('input');
    expect(checkbox?.checked).toBe(true);
    checkbox!.click();
    element.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    const http = TestBed.inject(HttpTestingController);
    const save = http.expectOne('/api/templates');
    expect(save.request.body.instanceParameters[0].default).toBeUndefined();
    save.flush({
      id: 'template',
      definition: save.request.body,
      currentVersion: 1,
      versionId: 'version',
    });
    await fixture.whenStable();
    http.verify();
  });
});
