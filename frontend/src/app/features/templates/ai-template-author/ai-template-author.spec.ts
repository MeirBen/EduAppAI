import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AiTemplateAuthor } from './ai-template-author';
import { readingDefinition } from '../ai-template-form/ai-template.fixture';

describe('Prompt-first authoring', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }),
  );
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('keeps an AI proposal unsaved until the parent explicitly saves their edits', async () => {
    const fixture = TestBed.createComponent(AiTemplateAuthor);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/ai/status').flush({ configured: true });
    await fixture.whenStable();
    const element: HTMLElement = fixture.nativeElement;
    const prompt = element.querySelector<HTMLTextAreaElement>('#parent-prompt')!;
    prompt.value = 'הבנת הנקרא עם נושא משתנה';
    prompt.dispatchEvent(new Event('input', { bubbles: true }));
    element.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    TestBed.tick();
    http
      .expectOne('/api/ai/template-drafts')
      .flush({ definition: readingDefinition, generationMetadata: {} });
    await fixture.whenStable();
    http.expectNone('/api/templates');
    await vi.waitFor(() => {
      TestBed.tick();
      expect(element.querySelector<HTMLInputElement>('#ai-template-name')?.disabled).toBe(false);
    });
    const name = element.querySelector<HTMLInputElement>('#ai-template-name')!;
    name.value = 'התבנית הערוכה שלי';
    name.dispatchEvent(new Event('input', { bubbles: true }));
    element
      .querySelector('app-ai-template-form form')!
      .dispatchEvent(new Event('submit', { cancelable: true }));
    TestBed.tick();
    const save = http.expectOne('/api/templates');
    expect(element.querySelector<HTMLButtonElement>('form button[type="submit"]')?.disabled).toBe(
      true,
    );
    expect(
      element.querySelector<HTMLButtonElement>('app-ai-template-form + button')?.disabled,
    ).toBe(true);
    expect(prompt.disabled).toBe(true);
    expect(save.request.body.name).toBe('התבנית הערוכה שלי');
    expect(save.request.body.generation.instructions).toBe(
      readingDefinition.generation.instructions,
    );
    save.flush({
      id: 'template',
      definition: save.request.body,
      currentVersion: 1,
      versionId: 'version',
    });
    await fixture.whenStable();
  });

  it('preserves the prompt on provider failure and never silently creates fallback content', async () => {
    const fixture = TestBed.createComponent(AiTemplateAuthor);
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/ai/status').flush({ configured: true });
    await fixture.whenStable();
    const element: HTMLElement = fixture.nativeElement;
    const prompt = element.querySelector<HTMLTextAreaElement>('#parent-prompt')!;
    prompt.value = 'ניסוי מדעי';
    prompt.dispatchEvent(new Event('input', { bubbles: true }));
    element.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    TestBed.tick();
    http.expectOne('/api/ai/template-drafts').flush({}, { status: 502, statusText: 'Bad Gateway' });
    await fixture.whenStable();
    expect(prompt.value).toBe('ניסוי מדעי');
    expect(element.querySelector('app-ai-template-form')).toBeNull();
    await vi.waitFor(() => {
      TestBed.tick();
      expect(element.querySelector('[role="alert"]')?.textContent).toContain('לא נשמר דבר');
    });
    http.expectNone('/api/templates');
  });
});
