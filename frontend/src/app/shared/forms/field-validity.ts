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
  /** A problem shown at once: another field's edit caused it, or the saved check found it. */
  readonly flagged = input(false);
  private readonly field = inject(FormField);
  protected readonly invalid = computed(() => {
    const state = this.field.state();
    return this.flagged() || (state.touched() && state.invalid());
  });
}
