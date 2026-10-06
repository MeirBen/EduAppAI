import { DatePipe, DecimalPipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
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
import { RouterLink } from '@angular/router';
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
    disabled(path, () => this.busy() || this.reading() || this.terminal() || !!this.blocked());
    applyEach(path, (row) => {
      maxLength(row.value, this.answerLength, {
        message: `אפשר להזין עד ${this.answerLength} תווים.`,
      });
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
          return { kind: 'choice', message: 'יש לבחור אחת מהאפשרויות.' };
        // Keep incomplete numeric keystrokes saveable. The server also enforces its decimal range at submission.
        if (
          this.triedSubmit() &&
          question?.interaction.type === 'numeric-input' &&
          !/^[+-]?[0-9]+(?:\.[0-9]+)?$(?![\s\S])/.test(text)
        )
          return {
            kind: 'number',
            message: 'יש להזין מספר רגיל, עם נקודה עשרונית לפי הצורך וללא מפרידי אלפים.',
          };
        return undefined;
      });
    });
  });

  constructor() {
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
    return !this.dirty() || window.confirm('יש תשובות שלא נשמרו. לצאת מהפעילות בלי לשמור?');
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
      const missing = this.answers().filter((answer) => !answer.value.trim()).length;
      if (
        submitting &&
        missing &&
        !window.confirm(`נותרו ${missing} שאלות ללא תשובה. להגיש בכל זאת?`)
      )
        return;
      const restore = this.holdFocus();
      this.busy.set(true);
      this.notice.set('');
      try {
        const answers = this.answers().map(({ questionId, value }) => ({ questionId, value }));
        const saved = await (submitting
          ? this.api.submit(this.assignmentId(), session.revision, answers, this.lifetime)
          : this.api.save(this.assignmentId(), session.revision, answers, this.lifetime));
        if (this.lifetime.destroyed) return;
        this.accept(saved);
        this.notice.set(submitting ? 'העבודה הוגשה ונשמרה.' : 'התשובות נשמרו.');
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
      this.error.set('יש תשובות שצריך לתקן לפני ההמשך.');
      this.fields().errorSummary()[0]?.fieldTree().focusBoundControl();
    }
  }
  private failed(error: unknown) {
    const status = childStatus(error);
    this.requestBlock.set(status === 401 || status === 404 || status === 410 ? status : undefined);
    this.recovery.set(true);
    this.error.set(
      childError(error) +
        (!this.blocked() ? ' ייתכן שהבקשה נשמרה. בדקו את העבודה השמורה לפני ניסיון נוסף.' : ''),
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
        this.notice.set('העבודה השמורה נבדקה. התשובות המקומיות נשארו כאן ואפשר להמשיך.');
      } else {
        this.savedSession.set(saved);
        this.recovery.set(true);
        this.error.set('');
        this.notice.set(
          'נמצאה עבודה שמורה. התשובות המקומיות נשארו כאן עד לבחירה בטעינת העבודה השמורה.',
        );
      }
    } catch (error) {
      if (this.lifetime.destroyed) return;
      if (!this.session() && childStatus(error) === 404) {
        // A lost start may never have created a session. A new explicit start rechecks ownership/withdrawal.
        this.recovery.set(false);
        this.error.set('לא נמצאה עבודה שהתחילה. אפשר לנסות לפתוח את הפעילות שוב.');
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
      (this.dirty() && !window.confirm('להחליף את התשובות שבעמוד בעבודה השמורה?'))
    )
      return;
    const restore = this.holdFocus();
    this.accept(saved);
    this.notice.set('העבודה השמורה מוצגת.');
    restore();
  }
}
