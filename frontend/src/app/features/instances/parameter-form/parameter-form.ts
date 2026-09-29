import { ChangeDetectionStrategy, Component, input, linkedSignal, output } from '@angular/core';
import { applyEach, disabled, form, FormField, submit, validate } from '@angular/forms/signals';
import { ParameterDefinition, ParameterValues } from '../../../core/api/models';
import { isIntegerInput } from '../../../shared/forms/integer-input';

/** Numeric input stays as text so an empty optional field cannot silently become zero. */
interface ParameterEntry {
  text: string;
  checked: boolean;
}

/**
 * Renders reviewed field definitions with client validation; the API validates again.
 * Cleared optional text stays explicit; blank optional numbers/selects are omitted.
 */
@Component({
  selector: 'app-parameter-form',
  imports: [FormField],
  templateUrl: './parameter-form.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ParameterForm {
  /** Replacing the schema resets the form to that schema's defaults. */
  readonly definitions = input.required<ParameterDefinition[]>();
  readonly busy = input(false);
  /** Emits values only; this component neither calls the API nor creates task content. */
  readonly generated = output<ParameterValues>();
  protected readonly model = linkedSignal(() =>
    this.definitions().map((definition) => ({
      text: definition.default == null ? '' : String(definition.default),
      checked: definition.default === true,
    })),
  );
  protected readonly fields = form(this.model, (path) => {
    applyEach(path, (entry) => {
      disabled(entry, { when: () => this.busy() });
      validate(entry, ({ value, state }) => {
        const definition = this.definitions()[Number(state.keyInParent())];
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
      // Empty optional text is an explicit value; omitting it would restore a cleared default.
      this.definitions().forEach((definition, index) => {
        const entry = this.model()[index];
        if (definition.type === 'boolean') values[definition.key] = entry.checked;
        else if (definition.type === 'text') values[definition.key] = entry.text;
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
  const label = definition.label;
  if (value.text === '' || (definition.required && !value.text.trim()))
    return definition.required ? `יש למלא את השדה „${label}”.` : undefined;
  if (definition.type === 'integer') {
    const number = Number(value.text);
    if (!isIntegerInput(value.text)) return `יש להזין מספר שלם בשדה „${label}”.`;
    if (definition.min != null && number < definition.min)
      return `הערך בשדה „${label}” חייב להיות לפחות ${definition.min}.`;
    if (definition.max != null && number > definition.max)
      return `הערך בשדה „${label}” חייב להיות לכל היותר ${definition.max}.`;
  }
  if (definition.type === 'text' && value.text.length > (definition.maxLength ?? 500))
    return `הטקסט בשדה „${label}” ארוך מדי.`;
  if (definition.type === 'select' && !definition.options?.includes(value.text))
    return `יש לבחור אחת מהאפשרויות בשדה „${label}”.`;
  return;
}
