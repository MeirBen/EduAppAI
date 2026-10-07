import {
  afterRenderEffect,
  ChangeDetectionStrategy,
  Component,
  computed,
  DOCUMENT,
  ElementRef,
  inject,
  input,
  output,
  viewChild,
} from '@angular/core';
import { FieldTree, FormField } from '@angular/forms/signals';
import { AuthoringTurn } from '../../../core/api/models';
import { LoadingIndicator } from '../../../shared/loading-indicator/loading-indicator';
import { FieldDirection } from '../../../shared/forms/field-direction';
import { SuggestionChips } from '../../../shared/suggestion-chips/suggestion-chips';

/**
 * Suggestions only fill the composer; every request still goes through the parent's send. Starters
 * cover what generation supports best: a reading text, an early reader, a drill, word problems on a
 * shared story and questions on the parent's own text, each with formats the app can grade.
 */
const starterSuggestions = [
  'קטע מידע על החלל לכיתה ד׳, כ־250 מילים, עם 5 שאלות אמריקאיות',
  'סיפור קצר לכיתה א׳ עם 3 שאלות אמריקאיות',
  '10 תרגילי חיבור וחיסור עד 100 לכיתה ב׳',
  'סיפור קצר עם מספרים לכיתה ג׳ ו־4 בעיות מילוליות עליו',
  '5 שאלות הבנה על טקסט שאדביק, לכיתה ה׳',
];
/** Changes that suit any plan, so none can contradict its settings. */
const changeSuggestions = [
  'שאלות קלות יותר',
  'שאלות מאתגרות יותר',
  'עוד שתי שאלות',
  'שפה פשוטה יותר',
];

/**
 * The authoring conversation: the visible thread, the request in flight and the composer. The
 * route owns message text, unresolved context and all requests; this component only emits.
 */
@Component({
  imports: [FormField, FieldDirection, SuggestionChips, LoadingIndicator],
  selector: 'app-template-chat',
  templateUrl: './template-chat.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TemplateChat {
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
  protected readonly starterSuggestions = starterSuggestions;
  protected readonly changeSuggestions = changeSuggestions;
  private readonly document = inject(DOCUMENT);
  private readonly composer = viewChild.required<ElementRef<HTMLTextAreaElement>>('composer');
  private readonly stop = viewChild<ElementRef<HTMLButtonElement>>('stop');
  private wasBusy = false;
  /** The owner's heading names the field only for a first description, not an answer or a change. */
  protected readonly titled = computed(
    () => !this.refining() && !this.clarification() && !!this.labelledBy(),
  );
  protected readonly started = computed(() => !!this.thread().length || !!this.pending());
  protected readonly canSend = computed(
    () =>
      this.configured() &&
      !this.busy() &&
      !this.locked() &&
      !this.consolidationRequired() &&
      !!this.fields().message().value().trim(),
  );

  constructor() {
    // Send and stop swap places and the composer is disabled while a request runs; focus that
    // fell to the page follows the swap, but never moves away from a control the parent chose.
    afterRenderEffect(() => {
      const busy = this.busy();
      if (busy === this.wasBusy) return;
      this.wasBusy = busy;
      if (this.document.activeElement !== this.document.body) return;
      (busy ? this.stop() : this.composer())?.nativeElement.focus({ preventScroll: true });
    });
  }

  /** Enter sends; the `keydown.enter` binding already leaves Shift+Enter for a new line. */
  protected sendOnEnter(event: Event) {
    if (event instanceof KeyboardEvent && event.isComposing) return;
    event.preventDefault();
    if (this.canSend()) this.sent.emit();
  }

  protected suggest(text: string) {
    this.fields().message().value.set(text);
  }
}
