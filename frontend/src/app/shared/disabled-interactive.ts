import { DestroyRef, Directive, ElementRef, inject, input, Renderer2 } from '@angular/core';

/**
 * A natively disabled button drops keyboard focus to the page, so a button that its own action
 * makes unavailable would lose the parent's place. This keeps it focusable, as ARIA recommends for
 * controls the parent should still find, announces `aria-disabled` and ignores activation,
 * including implicit form submission. Its capture listener runs before the button's own handlers.
 */
@Directive({
  selector: 'button[disabledInteractive]',
  host: { '[attr.aria-disabled]': 'disabledInteractive() || null' },
})
export class DisabledInteractive {
  readonly disabledInteractive = input(false);

  constructor() {
    const stop = inject(Renderer2).listen(
      inject(ElementRef).nativeElement,
      'click',
      (event: Event) => {
        if (!this.disabledInteractive()) return;
        event.preventDefault();
        event.stopImmediatePropagation();
      },
      { capture: true },
    );
    inject(DestroyRef).onDestroy(stop);
  }
}
