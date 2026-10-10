import {
  afterNextRender,
  afterRenderEffect,
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  DOCUMENT,
  ElementRef,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { FieldTree, FormField } from '@angular/forms/signals';
import { ActivityChatTurn, AuthoringTurn } from '../../../core/api/models';
import { LoadingIndicator } from '../../../shared/loading-indicator/loading-indicator';
import { FieldDirection } from '../../../shared/forms/field-direction';
import { scrollBehavior } from '../../../shared/scroll-behavior';
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
/** General refinement prompts; the server validates requested changes against supported limits. */
const changeSuggestions = [
  'שאלות קלות יותר',
  'שאלות מאתגרות יותר',
  'עוד שתי שאלות',
  'שפה פשוטה יותר',
];

/**
 * Shared conversation view; its host supplies thread, composer fields, request state and actions.
 */
@Component({
  imports: [FormField, FieldDirection, SuggestionChips, LoadingIndicator],
  selector: 'app-activity-chat',
  host: {
    class: 'flex min-h-0 flex-col',
    '(focusin)': 'focusInside = true',
    '(focusout)': 'leaveFocus($event)',
  },
  templateUrl: './activity-chat.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ActivityChat {
  readonly fields = input.required<FieldTree<{ message: string; consolidated: string }>>();
  readonly configured = input(false);
  readonly busy = input(false);
  readonly stopping = input(false);
  readonly locked = input(false);
  readonly clarification = input('');
  /** Bounded authoring history before save; persisted draft turns after save. */
  readonly thread = input<
    readonly (AuthoringTurn &
      Partial<Pick<ActivityChatTurn, 'assumptions' | 'target' | 'changes'>>)[]
  >([]);
  /** The parent's message while its request runs; it returns to the composer if the request fails. */
  readonly pending = input('');
  readonly targetLabel = input('');
  readonly invalidTarget = input(false);
  readonly targetCleared = output<void>();
  /** Plan-change labels for the latest unsaved reply; saved summaries carry their own `changes`. */
  readonly changes = input<string[]>([]);
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
  private readonly history = viewChild.required<ElementRef<HTMLElement>>('history');
  private readonly turns = viewChild.required<ElementRef<HTMLElement>>('turns');
  private followLatest = true;
  private wasBusy = false;
  /** The parent scrolled a bounded history away from the latest turn, which a control brings back. */
  protected readonly moreAfter = signal(false);
  protected focusInside = false;
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
    // The history follows its latest turn unless the parent scrolled back to read. Turns, the
    // composer and the thinking row resize it without scrolling, so every resize of the history or
    // its turns keeps following and re-checks what is hidden.
    const destroyRef = inject(DestroyRef);
    afterNextRender(() => {
      const history = this.history().nativeElement;
      const resized = new ResizeObserver(() => {
        if (this.followLatest) history.scrollTop = history.scrollHeight;
        this.followHistory();
      });
      resized.observe(history);
      resized.observe(this.turns().nativeElement);
      destroyRef.onDestroy(() => resized.disconnect());
    });
    // The composer is disabled while a request runs and Stop leaves when it ends; focus that fell to
    // the page moves to Stop, then to the history holding the reply. Only a stopped first request,
    // with no history yet, returns focus to the composer that holds it again.
    afterRenderEffect(() => {
      const busy = this.busy();
      if (busy === this.wasBusy) return;
      this.wasBusy = busy;
      if (!this.focusInside || this.document.activeElement !== this.document.body) return;
      const successor = busy
        ? this.stop()
        : this.thread().length
          ? this.history()
          : this.composer();
      successor?.nativeElement.focus({ preventScroll: true });
    });
  }

  /**
   * Only a bounded, scrolling history can hide turns. At fractional zoom the scroll offset snaps to
   * device pixels while heights are whole pixels, so the end is anywhere within a pixel of it.
   */
  protected followHistory() {
    const history = this.history().nativeElement;
    const scrolls = this.document.defaultView?.getComputedStyle(history).overflowY !== 'visible';
    const after = scrolls && history.scrollHeight - history.clientHeight - history.scrollTop > 1;
    this.followLatest = !after;
    this.moreAfter.set(after);
  }

  /**
   * Scrolls to the latest turn, smoothly unless motion is reduced. The control then disappears, so
   * focus moves to the history it scrolled rather than to the page.
   */
  protected showLatest() {
    const history = this.history().nativeElement;
    history.scrollTo({ top: history.scrollHeight, behavior: scrollBehavior(this.document) });
    history.focus({ preventScroll: true });
  }

  protected leaveFocus(event: FocusEvent) {
    if (
      event.relatedTarget instanceof Node &&
      !(event.currentTarget as HTMLElement).contains(event.relatedTarget)
    )
      this.focusInside = false;
  }

  /**
   * Explicit canvas targeting focuses the composer, scrolling only when it is out of view: a pinned
   * chat or the sheet stays in place, even near the scroll padding kept for the action bar.
   */
  focusComposer(): void {
    const composer = this.composer().nativeElement;
    const { top, bottom } = composer.getBoundingClientRect();
    const visible = top >= 0 && bottom <= (this.document.defaultView?.innerHeight ?? 0);
    composer.focus({ preventScroll: visible });
  }

  /** Enter sends; Shift+Enter and IME composition keep editing the message. */
  protected sendOnEnter(event: Event) {
    if (event instanceof KeyboardEvent && (event.isComposing || event.shiftKey)) return;
    event.preventDefault();
    this.send();
  }

  /** Sending returns the history to the latest turns, so the parent sees their request and its reply. */
  protected send(consolidate = false) {
    if (!consolidate && !this.canSend()) return;
    this.followLatest = true;
    (consolidate ? this.consolidated : this.sent).emit();
  }

  protected suggest(text: string) {
    this.fields().message().value.set(text);
  }
}
