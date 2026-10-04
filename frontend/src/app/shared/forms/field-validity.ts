import { computed, Directive, inject, input } from '@angular/core';
import { FormField } from '@angular/forms/signals';

/**
 * Marks a native field invalid once the parent has left it or tried to save, like Angular's
 * control status classes, so the field's own edge and `FieldErrors` agree.
 */
@Directive({
  selector: '[formField]',
  host: { '[attr.aria-invalid]': 'invalid() || null' },
})
export class FieldValidity {
  /** A problem another field's edit caused, shown at once since this field was never touched. */
  readonly mismatch = input(false);
  private readonly field = inject(FormField);
  protected readonly invalid = computed(() => {
    const state = this.field.state();
    return this.mismatch() || (state.touched() && state.invalid());
  });
}
