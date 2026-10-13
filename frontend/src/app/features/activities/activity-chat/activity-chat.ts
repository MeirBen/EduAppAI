import {
  afterNextRender,
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
import { DisabledInteractive } from '../../../shared/disabled-interactive';
import { LoadingIndicator } from '../../../shared/loading-indicator/loading-indicator';
import { FieldDirection } from '../../../shared/forms/field-direction';
import { scrollBehavior } from '../../../shared/scroll-behavior';
import { SuggestionChips } from '../../../shared/suggestion-chips/suggestion-chips';

/**
 * Suggestions only fill the composer; every request still goes through the parent's send. Built-in
 * starters cover what generation supports best: a reading text, an early reader, a drill, word
 * problems on a shared story and questions on the parent's own text, each gradable by the app.
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
 * A request never moves focus: the composer turns read-only and one control sends, then stops.
 */
@Component({
  imports: [FormField, FieldDirection, SuggestionChips, LoadingIndicator, DisabledInteractive],
  selector: 'app-activity-chat',
  host: {
    class: 'flex min-h-0 flex-1 flex-col',
  },
  templateUrl: './activity-chat.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ActivityChat {
  readonly fields = input.required<FieldTree<{ message: string }>>();
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
  /** A plan exists, so the composer asks for a change instead of the first description. */
  readonly refining = input(false);
  /** ID of the owner's visible heading that names the first-description field. */
  readonly labelledBy = input('');
  /** The latest approved activities' first messages; no starters show until they are known. */
  readonly firstMessages = input<readonly string[]>();
  readonly sent = output<void>();
  readonly cancelled = output<void>();
  /** Approved activities' first messages lead, completed by built-in starters, each once. */
  protected readonly starters = computed(() => {
    const saved = this.firstMessages();
    return (
      saved && [...new Set([...saved, ...starterSuggestions])].slice(0, starterSuggestions.length)
    );
  });
  protected readonly changeSuggestions = changeSuggestions;
  private readonly document = inject(DOCUMENT);
  private readonly history = viewChild.required<ElementRef<HTMLElement>>('history');
  private readonly turns = viewChild.required<ElementRef<HTMLElement>>('turns');
  private followLatest = true;
  /** The parent scrolled a bounded history away from the latest turn, which a control brings back. */
  protected readonly moreAfter = signal(false);
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

  /** Enter sends; Shift+Enter and IME composition keep editing the message. */
  protected sendOnEnter(event: Event) {
    if (event instanceof KeyboardEvent && (event.isComposing || event.shiftKey)) return;
    event.preventDefault();
    this.send();
  }

  /** Sending returns the history to the latest turns, so the parent sees their request and its reply. */
  protected send() {
    if (!this.canSend()) return;
    this.followLatest = true;
    this.sent.emit();
  }

  /** While a request runs, the composer's one control stops it instead of sending. */
  protected stopRequest() {
    if (this.busy()) this.cancelled.emit();
  }

  protected suggest(text: string) {
    this.fields().message().value.set(text);
  }
}
