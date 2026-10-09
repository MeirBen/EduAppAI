import { afterNextRender, computed, DestroyRef, inject, Injector, signal } from '@angular/core';
import { applyEach, disabled, form, maxLength, validate } from '@angular/forms/signals';
import { Subject } from 'rxjs';
import { apiError } from '../../../core/api/api-error';
import { LearningApi } from '../../../core/api/learning-api';
import { Limits } from '../../../core/api/limits';
import {
  ActivityDetail,
  GenerationOperation,
  ImportedChatTurn,
  LearningPlan,
  RevisionTarget,
} from '../../../core/api/models';
import { focusHolder } from '../../../shared/focus-holder';
import { DocumentForm } from '../activity-document-editor/document-form';
import { planChangeLabel } from '../activity-presentation';
import { isRunning } from '../generation-status/operation-state';

/**
 * Local conversation, composer and attached sources for one workspace. Create in its injection
 * context; the workspace owns content/revision fences and the server owns saved chat and operations.
 */
export class ChatSession {
  private readonly api = inject(LearningApi);
  private readonly limits = inject(Limits).current;
  private readonly lifetime = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly holdFocus = focusHolder();
  private readonly cancelled = new Subject<void>();
  private settledOperation: string | undefined;
  private targetPrefix = '';

  readonly selectedTarget = signal<RevisionTarget | undefined>(undefined);
  readonly targetLabel = computed(() => {
    const target = this.selectedTarget();
    if (!target) return '';
    const document = this.document();
    if (target.kind === 'material')
      return (
        document.materials.find((material) => material.id === target.id)?.title ||
        (document.materials.some((material) => material.id === target.id)
          ? 'הטקסט שנבחר'
          : 'החלק שנבחר אינו קיים עוד. בטלו את הבחירה או בחרו חלק אחר.')
      );
    const index = document.questions.findIndex((question) => question.id === target.id);
    return index < 0
      ? 'החלק שנבחר אינו קיים עוד. בטלו את הבחירה או בחרו חלק אחר.'
      : `שאלה ${index + 1}`;
  });
  readonly invalidTarget = computed(() => {
    const target = this.selectedTarget();
    return (
      !!target &&
      !(target.kind === 'material' ? this.document().materials : this.document().questions).some(
        (item) => item.id === target.id,
      )
    );
  });
  readonly addedSources = signal<{ label: string; text: string; confirmedText: string }[]>([]);
  readonly sourcesReady = computed(
    () =>
      this.additionFields().valid() &&
      this.addedSources().every((source) => source.text === source.confirmedText),
  );
  readonly chat = signal({ message: '', consolidated: '' });
  /** Unanswered authoring requests return to the composer on failure, Stop or a local edit. */
  private readonly request = signal<{ id: string; text: string; consolidate: boolean } | undefined>(
    undefined,
  );
  readonly authoring = computed(() => !!this.request());
  readonly pendingMessage = computed(() => this.request()?.text ?? '');
  /** Imported on first save; the saved draft's chat then becomes authoritative. */
  readonly authorThread = signal<ImportedChatTurn[]>([]);
  private readonly contextStart = signal(0);
  readonly authorError = signal('');
  readonly clarification = signal('');
  readonly changes = signal<string[]>([]);
  private readonly conversation = computed(() =>
    this.authorThread()
      .slice(this.contextStart())
      .map(({ role, text }) => ({ role, text })),
  );
  readonly needsConsolidation = computed(
    () =>
      this.conversation().length > this.limits.maxContextTurns ||
      this.conversation().reduce((sum, turn) => sum + turn.text.length, 0) >
        this.limits.contextLength,
  );
  readonly hasComposerWork = computed(
    () =>
      !!this.chat().message.trim() ||
      !!this.chat().consolidated.trim() ||
      this.addedSources().some((source) => !!source.label || !!source.text),
  );
  readonly chatFields = form(this.chat, (path) => {
    disabled(path, () => this.locked() || this.authoring());
    maxLength(path.message, this.limits.messageLength);
    maxLength(path.consolidated, this.limits.messageLength);
  });
  readonly additionFields = form(this.addedSources, (path) => {
    disabled(path, () => this.locked());
    applyEach(path, (source) => {
      validate(source.label, ({ value }) =>
        value().trim() ? undefined : { kind: 'required', message: 'יש להזין שם לטקסט.' },
      );
      maxLength(source.label, this.limits.nameLength);
      validate(source.text, ({ value }) =>
        value().trim() ? undefined : { kind: 'required', message: 'יש להזין טקסט.' },
      );
      maxLength(source.text, this.limits.bodyLength);
    });
  });

  constructor(
    private readonly document: () => DocumentForm,
    private readonly locked: () => boolean,
  ) {
    this.lifetime.onDestroy(() => {
      this.request.set(undefined);
      this.cancelled.next();
      this.cancelled.complete();
    });
  }

  selectTarget(target: RevisionTarget) {
    if (this.locked()) return;
    this.selectedTarget.set(target);
    const prefix = `לגבי ${this.targetLabel()}: `;
    const message = this.chat().message;
    // Only the exact prefix inserted here is ours to replace; parent wording stays untouched.
    if (!message || (this.targetPrefix && message.startsWith(this.targetPrefix))) {
      this.chat.update((chat) => ({
        ...chat,
        message: prefix + (message ? message.slice(this.targetPrefix.length) : ''),
      }));
      this.targetPrefix = prefix;
    } else this.targetPrefix = '';
  }

  clearTarget() {
    const prefix = this.targetPrefix;
    this.chat.update((chat) => ({
      ...chat,
      message:
        prefix && chat.message.startsWith(prefix)
          ? chat.message.slice(prefix.length)
          : chat.message,
    }));
    this.targetPrefix = '';
    this.selectedTarget.set(undefined);
  }

  addSource() {
    if (this.locked() || this.addedSources().length >= this.limits.maxMaterials) return;
    const index = this.addedSources().length;
    this.addedSources.update((sources) => [...sources, { label: '', text: '', confirmedText: '' }]);
    afterNextRender(() => this.additionFields[index].label().focusBoundControl(), {
      injector: this.injector,
    });
  }

  confirmAddedSource(index: number) {
    if (this.locked()) return;
    const fields = this.additionFields[index]();
    fields.markAsTouched();
    if (fields.invalid()) {
      this.authorError.set('תקנו את השדות המסומנים.');
      return;
    }
    const restoreFocus = this.holdFocus();
    this.authorError.set('');
    this.addedSources.update((sources) =>
      sources.map((source, i) =>
        i === index ? { ...source, confirmedText: source.text } : source,
      ),
    );
    restoreFocus('chat-message');
  }

  removeAddedSource(index: number) {
    if (this.locked()) return;
    const restoreFocus = this.holdFocus();
    this.addedSources.update((sources) => sources.filter((_, i) => i !== index));
    this.authorError.set('');
    restoreFocus('add-chat-source');
  }

  cancelAuthor() {
    const pending = this.request();
    this.request.set(undefined); // Invalidate identity before unsubscribing from transport.
    this.cancelled.next();
    if (pending)
      this.chat.update((chat) =>
        pending.consolidate
          ? { ...chat, consolidated: pending.text }
          : { ...chat, message: pending.text },
      );
  }

  /** Local content edits invalidate both the pending request and unresolved context. */
  resetContext() {
    this.cancelAuthor();
    this.contextStart.set(this.authorThread().length);
    this.clarification.set('');
  }

  clearFeedback() {
    this.changes.set([]);
    this.authorError.set('');
  }

  /** Returns a changed proposal only while the workspace's original buffer/revision still matches. */
  async author(
    baseDefinition: LearningPlan | undefined,
    baseRevision: number,
    isCurrent: () => boolean,
    consolidate = false,
  ): Promise<LearningPlan | undefined> {
    const message = consolidate ? this.chat().consolidated : this.chat().message;
    const requestId = crypto.randomUUID();
    const context = consolidate ? [] : this.conversation();
    this.request.set({ id: requestId, text: message, consolidate });
    this.chat.update((chat) =>
      consolidate ? { ...chat, consolidated: '' } : { ...chat, message: '' },
    );
    this.authorError.set('');
    try {
      const reply = await this.api.authorPlan(
        { message, baseDefinition, context, requestId, baseRevision },
        this.cancelled,
        this.lifetime,
      );
      if (
        this.lifetime.destroyed ||
        this.request()?.id !== requestId ||
        reply.requestId !== requestId ||
        reply.baseRevision !== baseRevision ||
        !isCurrent()
      )
        return;
      this.request.set(undefined);
      if (consolidate) this.contextStart.set(this.authorThread().length);
      if (reply.proposal) {
        this.clarification.set('');
        // A first plan has no earlier version to compare; the setup shows the proposal itself.
        this.changes.set(
          baseDefinition
            ? reply.changes.map((change) =>
                planChangeLabel(change, baseDefinition, reply.proposal!),
              )
            : [],
        );
        this.appendReply(
          message,
          !reply.changes.length
            ? 'ההגדרות כבר תואמות לבקשה.'
            : baseDefinition
              ? 'ההגדרות עודכנו. אפשר לבקש שינוי או לבטל אותו.'
              : 'הכנו הגדרות לפי הבקשה. בדקו אותן וצרו את הפעילות.',
          reply.assumptions,
          true,
        );
      } else if (reply.clarification) {
        this.appendReply(message, reply.clarification, reply.assumptions, false);
        this.clarification.set(reply.clarification);
      }
      this.chat.set({ message: '', consolidated: '' });
      this.targetPrefix = '';
      return reply.changes.length ? (reply.proposal ?? undefined) : undefined;
    } catch (error) {
      if (!this.lifetime.destroyed && this.request()?.id === requestId)
        this.authorError.set(apiError(error));
    } finally {
      if (this.request()?.id === requestId) this.cancelAuthor();
    }
    return undefined;
  }

  /** Saved replies consume only incorporated sources; external work never replaces local input. */
  settleOperation(draft: ActivityDetail, operation: GenerationOperation | undefined, own: boolean) {
    if (
      operation?.kind !== 'Revise' ||
      isRunning(operation) ||
      this.settledOperation === operation.id
    )
      return;
    this.settledOperation = operation.id;
    if (!own) return;
    const turn = draft.chat.find(
      (turn) => turn.role === 'parent' && turn.operationId === operation.id,
    );
    if (!turn) return;
    const completed = operation.status === 'completed';
    const unconsumed = (source: { text: string }) =>
      !completed ||
      !draft.plan.materials.some(
        (material) => material.source === 'supplied' && material.text === source.text,
      );
    if (turn.text === this.chat().message) {
      if (completed) {
        this.chat.update((chat) => ({ ...chat, message: '' }));
        this.targetPrefix = '';
        this.selectedTarget.set(undefined);
        this.addedSources.update((sources) => sources.filter(unconsumed));
      }
      return;
    }
    if (this.chat().message || this.chat().consolidated || this.addedSources().length) return;
    if (!operation.artifacts) {
      this.authorError.set(
        'פרטי הבקשה הקודמת כבר אינם זמינים. כתבו אותה מחדש והוסיפו את הטקסט הדרוש.',
      );
      return;
    }
    this.addedSources.set(
      (operation.artifacts.sources ?? [])
        .filter(unconsumed)
        .map((source) => ({ ...source, confirmedText: source.text })),
    );
    if (!completed) {
      this.chat.update((chat) => ({ ...chat, message: turn.text }));
      this.targetPrefix = '';
      this.selectedTarget.set(turn.target ?? undefined);
    }
  }

  private appendReply(message: string, reply: string, assumptions: string[], resolved: boolean) {
    const atUtc = new Date().toISOString();
    const turns: ImportedChatTurn[] = [
      ...this.authorThread(),
      { role: 'parent', text: message, atUtc, target: null, assumptions: null },
      { role: 'assistant', text: reply, atUtc, target: null, assumptions },
    ];
    const dropped = Math.max(0, turns.length - this.limits.maxChatTurns);
    this.authorThread.set(turns.slice(dropped));
    this.contextStart.set(
      resolved ? this.authorThread().length : Math.max(0, this.contextStart() - dropped),
    );
  }
}
