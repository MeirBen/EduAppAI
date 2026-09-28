import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import {
  form,
  FormField,
  max,
  maxLength,
  min,
  required,
  submit,
  validate,
} from '@angular/forms/signals';
import { LearningApi } from '../../../core/api/learning-api';
import { apiError } from '../../../core/api/api-error';

/** Authors the supported multiplication blueprint; defaults remain editable per task instance. */
@Component({
  selector: 'app-create-template',
  imports: [FormField, RouterLink],
  templateUrl: './create-template.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CreateTemplate {
  private readonly api = inject(LearningApi);
  private readonly router = inject(Router);
  protected readonly model = signal({ name: '', difficulty: 'medium', questionCount: 5 });
  protected readonly fields = form(this.model, (path) => {
    required(path.name);
    maxLength(path.name, 100);
    validate(path.name, ({ value }) =>
      value().trim() ? undefined : { kind: 'required', message: 'יש להזין שם לתבנית.' },
    );
    min(path.questionCount, 1);
    max(path.questionCount, 20);
    validate(path.questionCount, ({ value }) =>
      Number.isInteger(value()) ? undefined : { kind: 'integer', message: 'יש להזין מספר שלם.' },
    );
  });
  protected readonly error = signal('');

  protected async save(event: Event) {
    event.preventDefault();
    this.error.set('');
    await submit(this.fields, async () => {
      const value = this.model();
      try {
        const template = await this.api.createTemplate({
          schemaVersion: 1,
          name: value.name.trim(),
          instanceParameters: [
            {
              key: 'difficulty',
              label: 'רמת קושי',
              type: 'select',
              required: true,
              default: value.difficulty,
              options: ['easy', 'medium', 'hard'],
            },
            {
              key: 'questionCount',
              label: 'מספר שאלות',
              type: 'integer',
              required: true,
              default: value.questionCount,
              min: 1,
              max: 20,
            },
          ],
          generation: {
            mode: 'deterministic',
            generator: 'math-v1',
            fixedSettings: { operation: 'multiplication' },
          },
        });
        await this.router.navigate(['/templates', template.id, 'create']);
      } catch (error) {
        this.error.set(apiError(error));
      }
    });
  }
}
