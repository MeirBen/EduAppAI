import { Component, signal } from '@angular/core';
import { form, apply } from '@angular/forms/signals';
import { TestBed } from '@angular/core/testing';
import { ActivityDocumentEditor, DocumentEdit } from './activity-document-editor';
import { documentForm, documentSchema } from './document-form';
import { limits, provideLimits } from '../../../core/api/limits.fixture';
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
      .find((b) => b.textContent?.includes('מחיקת אפשרות 1'))!
      .click();
    await fixture.whenStable();
    expect(root.querySelector<HTMLInputElement>('#question-0-answer')!.value).toBe('א');
    for (const input of root.querySelectorAll<HTMLInputElement>('input,textarea,select'))
      expect(root.querySelector(`label[for="${input.id}"]`)).not.toBeNull();
  });
});
