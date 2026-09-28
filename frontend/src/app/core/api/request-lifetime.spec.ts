import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Library } from '../../features/library/library';
import { CreateInstance } from '../../features/instances/create-instance/create-instance';
import { InstancePreviewPage } from '../../features/instances/instance-preview/instance-preview';
import { AiTemplateAuthor } from '../../features/templates/ai-template-author/ai-template-author';
import { TemplateEditor } from '../../features/templates/template-editor/template-editor';

describe('Page HTTP reads', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }),
  );
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it.each([
    { component: TemplateEditor, input: 'templateId', path: '/api/templates/first' },
    { component: CreateInstance, input: 'templateId', path: '/api/templates/first' },
    { component: InstancePreviewPage, input: 'instanceId', path: '/api/instances/first' },
    { component: AiTemplateAuthor, input: undefined, path: '/api/ai/status' },
  ])('cancels $path when its page is destroyed', ({ component, input, path }) => {
    const fixture = TestBed.createComponent(component as Type<unknown>);
    if (input) fixture.componentRef.setInput(input, 'first');
    TestBed.tick();
    const request = TestBed.inject(HttpTestingController).expectOne(path);
    fixture.destroy();
    expect(request.cancelled).toBe(true);
  });

  it('cancels an obsolete template read when the route selects another template', () => {
    const fixture = TestBed.createComponent(TemplateEditor);
    fixture.componentRef.setInput('templateId', 'first');
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    const first = http.expectOne('/api/templates/first');
    fixture.componentRef.setInput('templateId', 'second');
    TestBed.tick();
    const second = http.expectOne('/api/templates/second');
    fixture.destroy();
    expect(first.cancelled).toBe(true);
    expect(second.cancelled).toBe(true);
  });

  it('cancels both library reads when leaving the page', () => {
    const fixture = TestBed.createComponent(Library);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    const templates = http.expectOne('/api/templates');
    const instances = http.expectOne('/api/instances');
    fixture.destroy();
    expect(templates.cancelled).toBe(true);
    expect(instances.cancelled).toBe(true);
  });

  it('cancels a pending library deletion on navigation without touching the destroyed view', async () => {
    const fixture = TestBed.createComponent(Library);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/templates').flush([{ id: 'template', name: 'Saved', currentVersion: 1 }]);
    http.expectOne('/api/instances').flush([]);
    await fixture.whenStable();
    const element: HTMLElement = fixture.nativeElement;
    const dialog = element.querySelector('dialog')!;
    // jsdom has no modal implementation; browser tests cover native dialog behavior.
    dialog.showModal = vi.fn(() => dialog.setAttribute('open', ''));
    element.querySelector<HTMLButtonElement>('[aria-label="מחיקת תבנית: Saved"]')!.click();
    await fixture.whenStable();
    dialog.querySelectorAll('button')[1].click();
    const deletion = http.expectOne('/api/templates/template');
    expect(deletion.request.method).toBe('DELETE');
    fixture.destroy();
    expect(deletion.cancelled).toBe(true);
    await Promise.resolve();
  });

  it('shows a failed AI status check without reading a resource in error', async () => {
    const fixture = TestBed.createComponent(AiTemplateAuthor);
    TestBed.tick();
    TestBed.inject(HttpTestingController)
      .expectOne('/api/ai/status')
      .flush({}, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    const element: HTMLElement = fixture.nativeElement;
    expect(element.querySelector('[role="alert"]')?.textContent).toContain('לא הצלחנו לבדוק');
    expect(element.querySelector<HTMLButtonElement>('button[type="submit"]')?.disabled).toBe(true);
  });
});
