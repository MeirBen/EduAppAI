import { computed, Directive, inject, input } from '@angular/core';
import { FormField } from '@angular/forms/signals';

/**
 * `dir="auto"` resolves an empty field to left-to-right, which puts the caret and placeholder on
 * the wrong side of an RTL page. While empty, an auto field follows the page direction; once it
 * has text, the base styles give each paragraph its own direction. Other values pass through.
 */
@Directive({
  selector: 'input[formField][dir], textarea[formField][dir]',
  host: { '[attr.dir]': 'resolved()' },
})
export class FieldDirection {
  readonly dir = input.required<'auto' | 'ltr' | 'rtl'>();
  private readonly field = inject(FormField);
  protected readonly resolved = computed(() =>
    this.dir() === 'auto' && !this.field.state().value() ? null : this.dir(),
  );
}
