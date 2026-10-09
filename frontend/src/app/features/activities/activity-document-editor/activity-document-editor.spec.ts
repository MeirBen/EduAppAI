import { Component, signal } from '@angular/core';
import { form, apply } from '@angular/forms/signals';
import { TestBed } from '@angular/core/testing';
import { ActivityDocumentEditor } from './activity-document-editor';
import { documentForm, documentSchema } from './document-form';
import { limits, provideLimits } from '../../../core/api/limits.fixture';
@Component({
  imports: [ActivityDocumentEditor],
  template: `<app-activity-document-editor [fields]="fields" [materials]="[]" />`,
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
}
describe('ActivityDocumentEditor native fields', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideLimits()] }));
  it('preserves the old answer when an option changes and labels each control', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const option = root.querySelector<HTMLInputElement>('#question-0-option-0')!;
    option.value = 'ג';
    option.dispatchEvent(new Event('input', { bubbles: true }));
    await fixture.whenStable();
    expect(root.querySelector<HTMLInputElement>('#question-0-answer')!.value).toBe('א');
    expect(
      fixture.componentInstance.raw().questions[0].options.map((option) => option.value),
    ).toEqual(['ג', 'ב']);
    expect(root.querySelector('#question-0-points')!.closest('details')).not.toBeNull();
    for (const input of root.querySelectorAll<HTMLInputElement>('input,textarea,select'))
      expect(root.querySelector(`label[for="${input.id}"]`)).not.toBeNull();
  });

  it('isolates learner values in answer choices so a calculation keeps its own order', async () => {
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.raw.update((raw) => ({
      ...raw,
      questions: raw.questions.map((q) => ({
        ...q,
        options: [{ value: '58 - 23' }, { value: '-35' }],
        answer: '-7',
      })),
    }));
    await fixture.whenStable();
    const answer = (fixture.nativeElement as HTMLElement).querySelector('#question-0-answer')!;
    // <option> cannot hold <bdi>, so first-strong isolates (FSI…PDI) carry the same contract.
    expect(Array.from(answer.querySelectorAll('option'), (o) => o.textContent?.trim())).toEqual([
      'בחרו את התשובה הנכונה',
      '⁨-7⁩ — אינה תואמת לאף אפשרות',
      '1. ⁨58 - 23⁩',
      '2. ⁨-35⁩',
    ]);
  });
});
