import { DatePipe, DecimalPipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  DOCUMENT,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import {
  applyEach,
  disabled,
  form,
  FormField,
  maxLength,
  submit,
  validate,
} from '@angular/forms/signals';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { interval } from 'rxjs';
import { ChildApi } from '../../core/api/child-api';
import { LearnerAnswer, LearnerSession } from '../../core/api/child-models';
import { ChildAuth } from '../../core/auth/child-auth';
import { DisabledInteractive } from '../../shared/disabled-interactive';
import { focusHolder } from '../../shared/focus-holder';
import { FieldDirection } from '../../shared/forms/field-direction';
import { FieldErrors } from '../../shared/forms/field-errors';
import { FieldValidity } from '../../shared/forms/field-validity';
import { LoadingIndicator } from '../../shared/loading-indicator/loading-indicator';
import { childError, childStatus } from './child-error';
import { writeError } from '../../core/api/api-error';
import { pageVisible } from '../../core/page-visibility';
import { ActionBar, ActionBarToggle } from '../../shared/action-bar/action-bar';
import { elapsedClock } from './elapsed-clock';

/** Page-owned answer buffer. Reads offer a checkpoint; only acknowledgement or explicit acceptance replaces edits. */
@Component({
  selector: 'app-child-player',
  imports: [
    DatePipe,
    DecimalPipe,
    RouterLink,
    FormField,
    FieldErrors,
    FieldValidity,
    FieldDirection,
    DisabledInteractive,
    LoadingIndicator,
    ActionBar,
    ActionBarToggle,
  ],
  templateUrl: './child-player.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(window:beforeunload)': 'beforeUnload($event)' },
})
export class ChildPlayer {
  readonly assignmentId = input.required<string>();
  private readonly api = inject(ChildApi);
  private readonly identity = inject(ChildAuth).identity;
  // Keep the bootstrap limit for retained local text even if a later guard discovers expired access.
  private readonly answerLength = this.identity()!.answerLength;
  private readonly lifetime = inject(DestroyRef);
  private readonly holdFocus = focusHolder();
  protected readonly assignment = this.api.assignment(this.assignmentId);
  protected readonly session = signal<LearnerSession | undefined>(undefined);
  protected readonly savedSession = signal<LearnerSession | undefined>(undefined);
  protected readonly answers = signal<LearnerAnswer[]>([]);
  protected readonly answered = computed(
    () => this.answers().filter((answer) => answer.value.trim()).length,
  );
  protected readonly busy = signal(false);
  protected readonly reading = signal(false);
  protected readonly recovery = signal(false);
  private readonly requestBlock = signal<number | undefined>(undefined);
  protected readonly blocked = computed(
    () => this.requestBlock() ?? (this.identity() ? undefined : 401),
  );
  protected readonly error = signal('');
  protected readonly errorMessage = computed(() =>
    this.blocked() ? childError(this.blocked()) : this.error(),
  );
  protected readonly notice = signal('');
  protected readonly triedSubmit = signal(false);
  protected readonly failure = childError;
  protected readonly status = childStatus;
  protected readonly outcome = computed(() => this.savedSession() ?? this.session());
  protected readonly terminal = computed(
    () => !!this.outcome() && this.outcome()!.status !== 'assigned',
  );
  protected readonly dirty = computed(() => {
    const saved = new Map(
      this.session()?.answers.map((answer) => [answer.questionId, answer.value]),
    );
    return this.answers().some((answer) => answer.value !== (saved.get(answer.questionId) ?? ''));
  });
  protected readonly fields = form(this.answers, (path) => {
    disabled(path, {
      when: () => this.busy() || this.reading() || this.terminal() || !!this.blocked(),
    });
    applyEach(path, (row) => {
      maxLength(row.value, this.answerLength);
      validate(row.value, ({ value, valueOf }) => {
        const text = value();
        if (!text.trim()) return undefined;
        const question = this.assignment.hasValue()
          ? this.assignment.value().document.questions.find((q) => q.id === valueOf(row.questionId))
          : undefined;
        if (
          question?.interaction.type === 'single-choice' &&
          !question.interaction.options?.includes(text)
        )
          return { kind: 'choice', message: 'בחרו אחת מהתשובות.' };
        // Keep incomplete numeric keystrokes saveable. The server also enforces its decimal range at submission.
        if (
          this.triedSubmit() &&
          question?.interaction.type === 'numeric-input' &&
          !/^[+-]?[0-9]+(?:\.[0-9]+)?$(?![\s\S])/.test(text.trim())
        )
          return {
            kind: 'number',
            message: 'כתבו רק מספר, בלי פסיקים.',
          };
        return undefined;
      });
    });
  });

  /** A number is sent without the surrounding spaces mobile keyboards add; text keeps its own. */
  private readonly numericQuestions = computed(() =>
    this.assignment.hasValue()
      ? new Set(
          this.assignment
            .value()
            .document.questions.filter((q) => q.interaction.type === 'numeric-input')
            .map((q) => q.id),
        )
      : new Set<string>(),
  );
  /** Open work the child can still change. */
  protected readonly working = computed(
    () => this.session()?.status === 'assigned' && !this.terminal(),
  );
  /** The bar's one line of feedback, so a slow request never adds a row while the child scrolls. */
  protected readonly statusText = computed(() => {
    if (this.reading()) return 'בודקים…';
    if (!this.session()) return '';
    if (this.busy()) return 'שומרים…';
    if (!this.working()) return '';
    return this.dirty() ? 'לא נשמר' : 'נשמר';
  });
  private readonly visible = toSignal(pageVisible(inject(DOCUMENT)), { requireSync: true });
  private readonly now = signal(Date.now());
  /** Wall-clock time since the server start, as the parent's report counts it; nothing is measured here. */
  protected readonly elapsed = computed(() => {
    const started = this.session()?.startedAtUtc;
    return started ? elapsedClock(started, this.now()) : '';
  });

  constructor() {
    // The clock ticks only while work is open and the page is visible; it reads the server start again on return.
    effect((onCleanup) => {
      if (!this.visible() || !this.working()) return;
      this.now.set(Date.now());
      const tick = interval(1000).subscribe(() => this.now.set(Date.now()));
      onCleanup(() => tick.unsubscribe());
    });
    let opened = false;
    effect(() => {
      if (this.assignment.hasValue() && !opened) {
        opened = true;
        // Entering this player is the explicit start; inbox and content reads never create work.
        untracked(() => void this.start());
      }
    });
  }
  /** Leaving may cancel transport, but cannot undo a write already committed on the server. */
  canLeave() {
    return !this.dirty() || window.confirm('יש תשובות שלא נשמרו. לצאת בכל זאת?');
  }
  protected beforeUnload(event: BeforeUnloadEvent) {
    if (this.dirty()) event.preventDefault();
  }

  private accept(session: LearnerSession) {
    this.session.set(session);
    const saved = new Map(session.answers.map((answer) => [answer.questionId, answer.value]));
    this.fields().reset(
      this.assignment.value()!.document.questions.map((question) => ({
        questionId: question.id,
        value: saved.get(question.id) ?? '',
      })),
    );
    this.savedSession.set(undefined);
    this.recovery.set(false);
    this.triedSubmit.set(false);
    this.error.set('');
  }
  protected async start() {
    if (this.busy() || this.reading() || this.session() || this.blocked() || this.recovery())
      return;
    this.busy.set(true);
    this.error.set('');
    try {
      const session = await this.api.start(this.assignmentId(), this.lifetime);
      if (!this.lifetime.destroyed) this.accept(session);
    } catch (error) {
      if (!this.lifetime.destroyed) this.failed(error);
    } finally {
      if (!this.lifetime.destroyed) this.busy.set(false);
    }
  }
  protected async write(submitting: boolean, event?: Event) {
    event?.preventDefault();
    const session = this.session();
    if (
      !session ||
      this.busy() ||
      this.reading() ||
      this.recovery() ||
      this.terminal() ||
      this.blocked()
    )
      return;
    this.triedSubmit.set(submitting);
    this.error.set('');
    await submit(this.fields, async () => {
      // Submitting is final, so it always asks; unanswered questions are named first.
      const missing = this.answers().length - this.answered();
      const unanswered = !missing
        ? ''
        : missing === 1
          ? 'נשארה שאלה אחת בלי תשובה. '
          : `נשארו ${missing} שאלות בלי תשובה. `;
      if (
        submitting &&
        !window.confirm(`${unanswered}להגיש להורה? אחרי ההגשה אי אפשר לשנות את התשובות.`)
      )
        return;
      const restore = this.holdFocus();
      this.busy.set(true);
      this.notice.set('');
      try {
        const answers = this.answers().map(({ questionId, value }) => ({
          questionId,
          value: this.numericQuestions().has(questionId) ? value.trim() : value,
        }));
        const saved = await (submitting
          ? this.api.submit(this.assignmentId(), session.revision, answers, this.lifetime)
          : this.api.save(this.assignmentId(), session.revision, answers, this.lifetime));
        if (this.lifetime.destroyed) return;
        this.accept(saved);
        // The status line already reports a save; only a submission changes the page enough to announce.
        this.notice.set(submitting ? 'כל הכבוד, סיימתם!' : '');
      } catch (error) {
        if (!this.lifetime.destroyed) this.failed(error);
      } finally {
        if (!this.lifetime.destroyed) {
          this.busy.set(false);
          restore();
        }
      }
    });
    if (this.fields().invalid()) {
      this.error.set('תקנו את התשובות המסומנות.');
      this.fields().errorSummary()[0]?.fieldTree().focusBoundControl();
    }
  }
  private failed(error: unknown) {
    const status = childStatus(error);
    this.requestBlock.set(status === 401 || status === 404 || status === 410 ? status : undefined);
    this.recovery.set(true);
    this.error.set(
      writeError(
        childError(error),
        error,
        'אולי זה כבר נשמר. לחצו על "בדיקת עדכונים" לפני שמנסים שוב.',
      ),
    );
  }
  protected async readSaved() {
    if (this.busy() || this.reading() || this.blocked()) return;
    this.reading.set(true);
    const restore = this.holdFocus();
    try {
      const saved = await this.api.readSession(this.assignmentId(), this.lifetime);
      if (this.lifetime.destroyed) return;
      if (!this.session()) this.accept(saved);
      else if (saved.status === 'assigned' && saved.revision === this.session()!.revision) {
        this.recovery.set(false);
        this.savedSession.set(undefined);
        this.error.set('');
        this.notice.set('הכול בסדר, אפשר להמשיך.');
      } else {
        this.savedSession.set(saved);
        this.recovery.set(true);
        this.error.set('');
        this.notice.set('יש תשובות שמורות חדשות יותר. אפשר לטעון אותן במקום התשובות שכאן.');
      }
    } catch (error) {
      if (this.lifetime.destroyed) return;
      if (!this.session() && childStatus(error) === 404) {
        // A lost start may never have created a session. A new explicit start rechecks ownership/withdrawal.
        this.recovery.set(false);
        this.error.set('עוד לא נשמר כלום. נסו להתחיל שוב.');
      } else this.failed(error);
    } finally {
      if (!this.lifetime.destroyed) {
        this.reading.set(false);
        restore();
      }
    }
  }
  protected useSaved() {
    const saved = this.savedSession();
    if (
      !saved ||
      this.blocked() ||
      this.busy() ||
      this.reading() ||
      (this.dirty() && !window.confirm('להחליף את התשובות שכאן בתשובות השמורות?'))
    )
      return;
    const restore = this.holdFocus();
    this.accept(saved);
    this.notice.set('התשובות השמורות מוצגות.');
    restore();
  }
}
