import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

/**
 * A field's messages once the parent has left it or tried to save; name this element in the
 * control's `aria-describedby`. Rules without a message, such as a length limit, add none, and
 * with nothing to show it takes no layout slot.
 */
@Component({
  selector: 'app-field-errors',
  template: `
    @for (message of messages(); track $index) {
      <p class="field-error mt-2">{{ message }}</p>
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[hidden]': '!messages().length' },
})
export class FieldErrors {
  readonly field =
    input.required<() => { touched(): boolean; errors(): readonly { message?: string }[] }>();
  protected readonly messages = computed(() => {
    const state = this.field()();
    return state.touched() ? state.errors().flatMap(({ message }) => message ?? []) : [];
  });
}
