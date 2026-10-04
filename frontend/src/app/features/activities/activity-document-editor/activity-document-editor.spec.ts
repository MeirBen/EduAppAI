import { Component, signal } from '@angular/core';
import { form, apply } from '@angular/forms/signals';
import { TestBed } from '@angular/core/testing';
import { ActivityDocumentEditor, DocumentEdit } from './activity-document-editor';
import { documentForm, documentSchema } from './document-form';
import { limits, provideLimits } from '../../../core/api/limits.fixture';
import { editDocumentStructure, workspaceForm } from '../activity-workspace/workspace-form';
import { numericPlan } from '../learning-plan.fixture';
@Component({
  imports: [ActivityDocumentEditor],
  template: `<app-activity-document-editor
    [fields]="fields"
    [materials]="[]"
    (structureChanged)="edit($event)"
  />`,
})
class Host {
  readonly raw = signal(
    documentForm({
      title: 'בדיקה',
      instructions: null,
      materials: [],
      questions: [
        {
          id: 'q',
          prompt: 'בחרו',
          interaction: { type: 'single-choice', options: ['א', 'ב'] },
          answer: { value: 'א' },
          points: 1,
        },
      ],
    }),
  );
  readonly fields = form(this.raw, (path) => apply(path, documentSchema(limits)));
  edit(event: DocumentEdit) {
    if (event.kind === 'remove-option')
      this.raw.update((raw) => ({
        ...raw,
        questions: raw.questions.map((q) => ({
          ...q,
          options: q.options.filter((_, i) => i !== event.option),
        })),
      }));
  }
}
describe('ActivityDocumentEditor native fields', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideLimits()] }));
  it('preserves the old answer when an option is changed or removed and labels each control', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const option = root.querySelector<HTMLInputElement>('#question-0-option-0')!;
    option.value = 'ג';
    option.dispatchEvent(new Event('input', { bubbles: true }));
    await fixture.whenStable();
    expect(root.querySelector<HTMLInputElement>('#question-0-answer')!.value).toBe('א');
    Array.from(root.querySelectorAll('button'))
      .find((b) => b.textContent?.includes('הסרת אפשרות 1'))!
      .click();
    await fixture.whenStable();
    expect(root.querySelector<HTMLInputElement>('#question-0-answer')!.value).toBe('א');
    for (const input of root.querySelectorAll<HTMLInputElement>('input,textarea,select'))
      expect(root.querySelector(`label[for="${input.id}"]`)).not.toBeNull();
  });
});

const choiceQuestion = (id: string) => ({
  id,
  prompt: 'שאלה ' + id,
  interaction: { type: 'single-choice' as const, options: ['א', 'ב'] },
  answer: { value: 'א' },
  points: 1,
  origin: { kind: 'manual' },
  acceptance: null,
});
@Component({
  imports: [ActivityDocumentEditor],
  template: `<app-activity-document-editor
    [fields]="fields.document"
    [materials]="[]"
    (structureChanged)="edit($event)"
  />`,
})
class Questions {
  readonly raw = signal(
    workspaceForm(numericPlan, undefined, {
      title: 'בדיקה',
      instructions: null,
      materials: [],
      questions: ['a', 'b', 'c'].map(choiceQuestion),
    }),
  );
  readonly fields = form(this.raw, (path) => apply(path.document, documentSchema(limits)));
  edit(event: DocumentEdit) {
    const next = editDocumentStructure(this.raw(), event, limits);
    if (next) this.raw.set(next);
  }
}
describe('ActivityDocumentEditor structure focus', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideLimits()] }));
  async function press(
    fixture: { whenStable(): Promise<unknown> },
    root: HTMLElement,
    text: string,
  ) {
    const button = Array.from(root.querySelectorAll('button')).find(
      (b) => b.textContent?.trim() === text,
    )!;
    button.focus();
    button.click();
    await fixture.whenStable();
    return document.activeElement as HTMLElement;
  }

  it("edits a question's options beside them, keeping the disclosure for the question itself", async () => {
    const fixture = TestBed.createComponent(Questions);
    await fixture.whenStable();
    const root: HTMLElement = fixture.nativeElement;
    const button = (text: string) =>
      Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === text)!;
    for (const text of ['הוספת אפשרות', 'הסרת אפשרות 1'])
      expect(button(text).closest('details')).toBeNull();
    expect(button('הסרת שאלה 1').closest('details')).not.toBeNull();
  });

  it('moves focus from a removed question to its neighbour, else to the add button', async () => {
    const fixture = TestBed.createComponent(Questions);
    await fixture.whenStable();
    const root: HTMLElement = fixture.nativeElement;
    expect((await press(fixture, root, 'הסרת שאלה 2')).id).toBe('question-1-more');
    expect((await press(fixture, root, 'הסרת שאלה 2')).id).toBe('question-0-more');
    expect((await press(fixture, root, 'הסרת שאלה 1')).id).toBe('add-question');
  });

  it("keeps focus on a moved question and in a removed option's place, else on adding one", async () => {
    const fixture = TestBed.createComponent(Questions);
    await fixture.whenStable();
    const root: HTMLElement = fixture.nativeElement;
    expect((await press(fixture, root, 'הזזת שאלה 1 למטה')).textContent?.trim()).toBe(
      'הזזת שאלה 2 למטה',
    );
    expect(fixture.componentInstance.raw().document.questions[1].prompt).toBe('שאלה a');
    // Options are positional: the next option takes the removed one's place and its button.
    expect((await press(fixture, root, 'הסרת אפשרות 1')).textContent?.trim()).toBe('הסרת אפשרות 1');
    expect((await press(fixture, root, 'הסרת אפשרות 1')).id).toBe('question-0-add-option');
  });
});
