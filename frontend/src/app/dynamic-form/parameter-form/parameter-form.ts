import { ChangeDetectionStrategy, Component, input, linkedSignal, output } from '@angular/core';
import { applyEach, disabled, form, FormField, submit, validate } from '@angular/forms/signals';
import { ParameterDefinition, ParameterValues } from '../../core/api/models';

interface ParameterEntry {
  text: string;
  checked: boolean;
}

@Component({
  selector: 'app-parameter-form',
  imports: [FormField],
  templateUrl: './parameter-form.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ParameterForm {
  readonly definitions = input.required<ParameterDefinition[]>();
  readonly busy = input(false);
  readonly generated = output<ParameterValues>();
  protected readonly model = linkedSignal(() => ({
    entries: this.definitions().map((definition) => ({
      text: definition.default == null ? '' : String(definition.default),
      checked: definition.default === true,
    })),
  }));
  protected readonly fields = form(this.model, (path) => {
    applyEach(path.entries, (entry) => {
      disabled(entry, { when: () => this.busy() });
      validate(entry, ({ value, pathKeys }) => {
        const definition = this.definitions()[Number(pathKeys()[1])];
        const message = parameterError(definition, value());
        return message ? { kind: 'parameter', message } : undefined;
      });
    });
  });

  protected async generate(event: Event) {
    event.preventDefault();
    if (this.busy()) return;
    await submit(this.fields, async () => {
      const values: ParameterValues = {};
      this.definitions().forEach((definition, index) => {
        const entry = this.model().entries[index];
        if (definition.type === 'boolean') values[definition.key] = entry.checked;
        else if (entry.text !== '')
          values[definition.key] = definition.type === 'integer' ? Number(entry.text) : entry.text;
      });
      this.generated.emit(values);
    });
  }
}

function parameterError(
  definition: ParameterDefinition,
  value: ParameterEntry,
): string | undefined {
  if (definition.type === 'boolean') return;
  if (!value.text.trim())
    return definition.required ? `${definition.label} is required.` : undefined;
  if (definition.type === 'integer') {
    const number = Number(value.text);
    if (!/^-?\d+$/.test(value.text) || !Number.isSafeInteger(number))
      return `${definition.label} must be a whole number.`;
    if (definition.min != null && number < definition.min)
      return `${definition.label} must be at least ${definition.min}.`;
    if (definition.max != null && number > definition.max)
      return `${definition.label} must be at most ${definition.max}.`;
  }
  if (definition.type === 'text' && value.text.length > (definition.maxLength ?? 500))
    return `${definition.label} is too long.`;
  if (definition.type === 'select' && !definition.options?.includes(value.text))
    return `Choose a valid ${definition.label.toLowerCase()}.`;
  return;
}
