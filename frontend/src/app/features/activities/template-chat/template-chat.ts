import {
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  inject,
  input,
  output,
  viewChild,
} from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FieldTree, FormField } from '@angular/forms/signals';
import { Limits } from '../../../core/api/limits';
import { AuthoringTurn } from '../../../core/api/models';
import { LoadingIndicator } from '../../../shared/loading-indicator/loading-indicator';

/** Starter requests that only fill the composer; every request still goes through the parent's send. */
const suggestions = [
  'קטע קריאה על החלל לכיתה ג׳, כ־300 מילים, עם 5 שאלות אמריקאיות',
  '10 תרגילי חיבור וחיסור עד 100 לכיתה ב׳',
  'שאלות הבנה על טקסט שאדביק, לכיתה ה׳',
  'אוצר מילים באנגלית על בעלי חיים, לגיל 9',
];

/**
 * The authoring conversation: the visible thread, the request in flight and the composer. The
 * route owns message text, unresolved context and all requests; this component only emits.
 */
@Component({
  imports: [FormField, LoadingIndicator, DecimalPipe],
  selector: 'app-template-chat',
  templateUrl: './template-chat.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TemplateChat {
  protected readonly limits = inject(Limits).current;
  readonly fields = input.required<FieldTree<{ message: string; consolidated: string }>>();
  readonly configured = input(false);
  readonly busy = input(false);
  readonly locked = input(false);
  readonly clarification = input('');
  /** Turns shown in order: the last answered request, then any unresolved clarification turns. */
  readonly thread = input<AuthoringTurn[]>([]);
  /** The parent's message while its request runs; it returns to the composer if the request fails. */
  readonly pending = input('');
  /** Computed plan changes and stated assumptions, shown with the assistant's latest reply. */
  readonly changes = input<string[]>([]);
  readonly assumptions = input<string[]>([]);
  readonly consolidationRequired = input(false);
  /** A plan exists, so the composer asks for a change instead of the first description. */
  readonly refining = input(false);
  /** ID of the owner's visible heading that names the first-description field. */
  readonly labelledBy = input('');
  readonly sent = output<void>();
  readonly consolidated = output<void>();
  readonly cancelled = output<void>();
  protected readonly suggestions = suggestions;
  private readonly composer = viewChild.required<ElementRef<HTMLTextAreaElement>>('composer');
  /** The owner's heading names the field only for a first description, not an answer or a change. */
  protected readonly titled = computed(
    () => !this.refining() && !this.clarification() && !!this.labelledBy(),
  );
  protected readonly started = computed(() => !!this.thread().length || !!this.pending());
  protected readonly canSend = computed(() => {
    const message = this.fields().message();
    return (
      this.configured() &&
      !this.busy() &&
      !this.locked() &&
      !this.consolidationRequired() &&
      !message.invalid() &&
      !!message.value().trim()
    );
  });

  /** Enter sends; the `keydown.enter` binding already leaves Shift+Enter for a new line. */
  protected sendOnEnter(event: Event) {
    if (event instanceof KeyboardEvent && event.isComposing) return;
    event.preventDefault();
    if (this.canSend()) this.sent.emit();
  }

  protected suggest(text: string) {
    this.fields().message().value.set(text);
    this.composer().nativeElement.focus();
  }
}
